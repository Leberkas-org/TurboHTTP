using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;
using TurboHttp.Protocol.RFC9110;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Protocol.RFC9204;

namespace TurboHttp.Streams.Stages.Decoding;

/// <summary>
/// RFC 9114 §4.1 — Assembles HTTP/3 HEADERS and DATA frames into HttpResponseMessage.
///
/// Unlike HTTP/2, HTTP/3 frames carry no stream identifier (QUIC provides multiplexing)
/// and no flags byte. Stream completion is signaled by QUIC FIN (upstream completion).
/// HEADERS frames are always complete (no CONTINUATION frames in HTTP/3).
///
/// Uses QPACK (RFC 9204) for header decompression and ContentEncodingDecoder
/// for body decompression (gzip, deflate, br).
/// </summary>
public sealed class Http30StreamStage : GraphStage<FlowShape<Http3Frame, (HttpResponseMessage Response, long StreamId)>>
{
    private readonly Inlet<Http3Frame> _in = new("Http30Stream.In");

    private readonly Outlet<(HttpResponseMessage Response, long StreamId)> _out = new("Http30Stream.Out");

    public override FlowShape<Http3Frame, (HttpResponseMessage Response, long StreamId)> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http30StreamStage _stage;
        private readonly QpackDecoder _qpack = new();
        private long _nextStreamId;

        private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;
        private IMemoryOwner<byte>? _bodyOwner;
        private Memory<byte> _bodyBuffer;
        private int _bodyLength;

        private HttpResponseMessage? _response;
        private string? _contentEncoding;

        public Logic(Http30StreamStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in, onPush: () =>
            {
                var frame = Grab(stage._in);

                switch (frame)
                {
                    case Http3HeadersFrame h:
                        HandleHeaders(h);
                        break;

                    case Http3DataFrame d:
                        HandleData(d);
                        break;
                }

                Pull(stage._in);
            }, onUpstreamFinish: () =>
            {
                // QUIC FIN — stream complete. Emit the assembled response.
                if (_response is not null)
                {
                    EmitResponse();
                }

                CompleteStage();
            });

            SetHandler(stage._out, () =>
            {
                Pull(stage._in);
            });
        }

        private void HandleHeaders(Http3HeadersFrame frame)
        {
            if (_response is not null)
            {
                // Trailing HEADERS frame — skip for now (trailers not yet supported)
                Log.Debug("Http30StreamStage: Trailing HEADERS frame received — skipping.");
                return;
            }

            var headers = _qpack.Decode(frame.HeaderBlock.Span);

            // RFC 9114 §4.2 — validate field names/values before building response
            Http3FieldValidator.Validate(headers);

            _response = new HttpResponseMessage();

            foreach (var h in headers)
            {
                if (h.Name == ":status")
                {
                    _response.StatusCode = (HttpStatusCode)int.Parse(h.Value);
                }
                else if (!h.Name.StartsWith(':'))
                {
                    _response.Headers.TryAddWithoutValidation(h.Name, h.Value);

                    if (h.Name.Equals(WellKnownHeaders.Names.ContentEncoding, StringComparison.OrdinalIgnoreCase))
                    {
                        _contentEncoding = h.Value;
                    }
                }
            }
        }

        private void HandleData(Http3DataFrame frame)
        {
            if (_response is null)
            {
                Log.Warning("Http30StreamStage: DATA frame received before HEADERS — dropping.");
                return;
            }

            var data = frame.Data.Span;
            if (data.Length == 0)
            {
                return;
            }

            EnsureBodyCapacity(_bodyLength + data.Length);
            data.CopyTo(_bodyBuffer.Span[_bodyLength..]);
            _bodyLength += data.Length;
        }

        private void EmitResponse()
        {
            var response = _response!;

            if (_bodyLength > 0)
            {
                var bodyBytes = _bodyBuffer[.._bodyLength].ToArray();

                // RFC 9110 §8.4 — apply content-encoding decompression (gzip, deflate, br)
                if (!string.IsNullOrEmpty(_contentEncoding))
                {
                    bodyBytes = ContentEncodingDecoder.Decompress(bodyBytes, _contentEncoding);
                }

                response.Content = new ByteArrayContent(bodyBytes);
            }

            var streamId = _nextStreamId;
            _nextStreamId += 4;

            Emit(_stage._out, (response, streamId));

            _bodyOwner?.Dispose();
            _bodyOwner = null;
            _bodyLength = 0;
            _response = null;
            _contentEncoding = null;
        }

        private void EnsureBodyCapacity(int required)
        {
            if (_bodyOwner == null || required > _bodyBuffer.Length)
            {
                var newOwner = _pool.Rent(required);

                if (_bodyOwner != null)
                {
                    _bodyBuffer.Span.CopyTo(newOwner.Memory.Span);
                    _bodyOwner.Dispose();
                }

                _bodyOwner = newOwner;
                _bodyBuffer = newOwner.Memory;
            }
        }

        public override void PostStop()
        {
            _bodyOwner?.Dispose();
            base.PostStop();
        }
    }
}
