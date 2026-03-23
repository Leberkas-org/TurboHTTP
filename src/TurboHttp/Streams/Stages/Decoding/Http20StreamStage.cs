using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages.Decoding;

public sealed class Http20StreamStage : GraphStage<FlowShape<Http2Frame, (HttpResponseMessage Response, int StreamId)>>
{
    private readonly Inlet<Http2Frame> _in = new("Http20Stream.In");

    private readonly Outlet<(HttpResponseMessage Response, int StreamId)> _out = new("Http20Stream.Out");

    public override FlowShape<Http2Frame, (HttpResponseMessage Response, int StreamId)> Shape => new(_in, _out);


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

            // Content headers captured during DecodeHeaders, applied when Content is created.
            public List<(string Name, string Value)>? ContentHeaders;

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

            SetHandler(stage._in, () =>
            {
                var frame = Grab(stage._in);
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
                    Pull(stage._in);
                }
            });

            SetHandler(stage._out, () =>
            {
                _responsePushed = false;
                Pull(stage._in);
            });
        }

        private void HandleHeaders(HeadersFrame frame)
        {
            if (!_streams.TryGetValue(frame.StreamId, out var state))
            {
                state = new StreamState();
                _streams[frame.StreamId] = state;
            }

            state.AppendHeader(frame.HeaderBlockFragment.Span);

            if (!frame.EndHeaders)
            {
                return;
            }

            DecodeHeaders(frame.StreamId, frame.EndStream);
        }

        private void HandleContinuation(ContinuationFrame frame)
        {
            if (!_streams.TryGetValue(frame.StreamId, out var state))
            {
                Log.Warning("Http20StreamStage: Received CONTINUATION for unknown stream {0} — dropping.", frame.StreamId);
                return;
            }

            state.AppendHeader(frame.HeaderBlockFragment.Span);

            if (frame.EndHeaders)
            {
                DecodeHeaders(frame.StreamId, false);
            }
        }

        private void HandleData(DataFrame frame)
        {
            if (!_streams.TryGetValue(frame.StreamId, out var state))
            {
                Log.Warning("Http20StreamStage: Received DATA for unknown stream {0} — dropping.", frame.StreamId);
                return;
            }

            state.AppendBody(frame.Data.Span);

            if (!frame.EndStream)
            {
                return;
            }

            var response = state.Response ?? new HttpResponseMessage();

            var bodyBytes = state.BodyBuffer[..state.BodyLength].ToArray();

            // RFC 9110 §8.4 — apply content-encoding decompression (gzip, deflate, br)
            if (!string.IsNullOrEmpty(state.ContentEncoding) &&
                !state.ContentEncoding.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                bodyBytes = ContentEncodingDecoder.Decompress(bodyBytes, state.ContentEncoding);
            }

            response.Content = new ByteArrayContent(bodyBytes);
            ApplyContentHeaders(response, state);

            _responsePushed = true;
            Push(_stage._out, (response, frame.StreamId));

            state.Dispose();
            _streams.Remove(frame.StreamId);
        }

        private void DecodeHeaders(int streamId, bool endStream)
        {
            if (!_streams.TryGetValue(streamId, out var state))
            {
                Log.Warning("Http20StreamStage: DecodeHeaders called for unknown stream {0} — dropping.", streamId);
                return;
            }

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

                    if (IsContentHeader(h.Name))
                    {
                        state.ContentHeaders ??= new List<(string, string)>();
                        state.ContentHeaders.Add((h.Name, h.Value));
                    }

                    if (h.Name.Equals(WellKnownHeaders.Names.ContentEncoding, StringComparison.OrdinalIgnoreCase))
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

            // Headers-only response (no body) — create empty content and apply content headers
            response.Content = new ByteArrayContent([]);
            ApplyContentHeaders(response, state);

            _responsePushed = true;
            Push(_stage._out, (response, streamId));

            state.Dispose();
            _streams.Remove(streamId);
        }
        private static void ApplyContentHeaders(HttpResponseMessage response, StreamState state)
        {
            if (state.ContentHeaders is null || response.Content is null)
            {
                return;
            }

            var wasDecompressed = !string.IsNullOrEmpty(state.ContentEncoding) &&
                                  !state.ContentEncoding.Equals(
                                      WellKnownHeaders.Identity,
                                      StringComparison.OrdinalIgnoreCase);

            foreach (var (name, value) in state.ContentHeaders)
            {
                // RFC 9110 §8.4 — after decompression, strip Content-Encoding and
                // Content-Length so downstream stages don't attempt double decompression.
                if (wasDecompressed)
                {
                    if (name.Equals(WellKnownHeaders.Names.ContentEncoding, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (name.Equals(WellKnownHeaders.Names.ContentLength, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        private static bool IsContentHeader(string name) =>
            name.StartsWith("content-", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("expires", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("last-modified", StringComparison.OrdinalIgnoreCase);
    }
}