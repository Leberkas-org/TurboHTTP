using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

public sealed class Http20StreamStage : GraphStage<FlowShape<Http2Frame, HttpResponseMessage>>
{
    private readonly Inlet<Http2Frame> _inlet = new("h2.stream.in");

    private readonly Outlet<HttpResponseMessage> _outlet = new("h2.stream.out");

    public override FlowShape<Http2Frame, HttpResponseMessage> Shape => new(_inlet, _outlet);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private sealed class StreamState : IDisposable
        {
            private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

            private IMemoryOwner<byte>? _headerOwner;
            private IMemoryOwner<byte>? _bodyOwner;

            public Memory<byte> HeaderBuffer;
            public Memory<byte> BodyBuffer;

            public int HeaderLength;
            public int BodyLength;

            public HttpResponseMessage? Response;

            // Captured during DecodeHeaders for use in HandleData decompression.
            public string? ContentEncoding;

            public void Dispose()
            {
                _headerOwner?.Dispose();
                _bodyOwner?.Dispose();
            }

            public void AppendHeader(ReadOnlySpan<byte> data)
            {
                EnsureHeaderCapacity(HeaderLength + data.Length);

                data.CopyTo(HeaderBuffer.Span[HeaderLength..]);
                HeaderLength += data.Length;
            }

            public void AppendBody(ReadOnlySpan<byte> data)
            {
                EnsureBodyCapacity(BodyLength + data.Length);

                data.CopyTo(BodyBuffer.Span[BodyLength..]);
                BodyLength += data.Length;
            }

            private void EnsureHeaderCapacity(int required)
            {
                if (_headerOwner == null || required > HeaderBuffer.Length)
                {
                    RentNewHeaderBuffer(required);
                }
            }

            private void EnsureBodyCapacity(int required)
            {
                if (_bodyOwner == null || required > BodyBuffer.Length)
                {
                    RentNewBodyBuffer(required);
                }
            }

            private void RentNewHeaderBuffer(int size)
            {
                var newOwner = _pool.Rent(size);

                if (_headerOwner != null)
                {
                    HeaderBuffer.Span.CopyTo(newOwner.Memory.Span);
                    _headerOwner.Dispose();
                }

                _headerOwner = newOwner;
                HeaderBuffer = newOwner.Memory;
            }

            private void RentNewBodyBuffer(int size)
            {
                var newOwner = _pool.Rent(size);

                if (_bodyOwner != null)
                {
                    BodyBuffer.Span.CopyTo(newOwner.Memory.Span);
                    _bodyOwner.Dispose();
                }

                _bodyOwner = newOwner;
                BodyBuffer = newOwner.Memory;
            }
        }

        private readonly Http20StreamStage _stage;
        private readonly Dictionary<int, StreamState> _streams = new();

        private readonly HpackDecoder _hpack = new();

        // Set when Push(outlet) is called in the current onPush turn.
        // Prevents calling Pull(inlet) twice (once from onPush, once from outlet.onPull).
        private bool _responsePushed;

        public Logic(Http20StreamStage stage) : base(stage.Shape)
        {
            _stage = stage;
            SetHandler(stage._inlet, () =>
            {
                var frame = Grab(stage._inlet);
                _streams.TryAdd(frame.StreamId, new StreamState());
                _responsePushed = false;
                switch (frame)
                {
                    case HeadersFrame h:
                        HandleHeaders(h);
                        break;

                    case ContinuationFrame c:
                        HandleContinuation(c);
                        break;

                    case DataFrame d:
                        HandleData(d);
                        break;
                }

                if (!_responsePushed)
                {
                    Pull(stage._inlet);
                }
            });

            SetHandler(stage._outlet, () =>
            {
                _responsePushed = false;
                Pull(stage._inlet);
            });
        }

        private void HandleHeaders(HeadersFrame frame)
        {
            var state = _streams[frame.StreamId];

            state.AppendHeader(frame.HeaderBlockFragment.Span);

            if (!frame.EndHeaders)
            {
                return;
            }

            DecodeHeaders(frame.StreamId, frame.EndStream);
        }

        private void HandleContinuation(ContinuationFrame frame)
        {
            var state = _streams[frame.StreamId];

            state.AppendHeader(frame.HeaderBlockFragment.Span);

            if (frame.EndHeaders)
            {
                DecodeHeaders(frame.StreamId, false);
            }
        }

        private void HandleData(DataFrame frame)
        {
            var state = _streams[frame.StreamId];

            state.AppendBody(frame.Data.Span);

            if (!frame.EndStream)
            {
                return;
            }

            var response = state.Response ?? new HttpResponseMessage();

            var bodyBytes = state.BodyBuffer[..state.BodyLength].ToArray();

            // RFC 9110 §8.4 — apply content-encoding decompression (gzip, deflate, br)
            if (!string.IsNullOrEmpty(state.ContentEncoding))
            {
                bodyBytes = ContentEncodingDecoder.Decompress(bodyBytes, state.ContentEncoding);
            }

            response.Content = new ByteArrayContent(bodyBytes);

            _responsePushed = true;
            Push(_stage._outlet, response);

            state.Dispose();
            _streams.Remove(frame.StreamId);
        }

        private void DecodeHeaders(int streamId, bool endStream)
        {
            var state = _streams[streamId];

            var headers = _hpack.Decode(state.HeaderBuffer[..state.HeaderLength].Span);

            var response = new HttpResponseMessage();

            foreach (var h in headers)
            {
                if (h.Name == ":status")
                {
                    response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
                }
                else if (!h.Name.StartsWith(':'))
                {
                    response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                    if (h.Name.Equals("content-encoding", StringComparison.OrdinalIgnoreCase))
                    {
                        state.ContentEncoding = h.Value;
                    }
                }
            }

            state.Response = response;

            if (!endStream)
            {
                return;
            }

            _responsePushed = true;
            Push(_stage._outlet, response);

            state.Dispose();
            _streams.Remove(streamId);
        }
    }
}