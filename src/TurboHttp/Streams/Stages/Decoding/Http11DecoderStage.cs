using System.Net;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http11;

namespace TurboHttp.Streams.Stages.Decoding;

public sealed class Http11DecoderStage : GraphStage<FlowShape<IInputItem, HttpResponseMessage>>
{
    private readonly Inlet<IInputItem> _in = new("Http11Decoder.In");
    private readonly Outlet<HttpResponseMessage> _out = new("Http11Decoder.Out");

    public Http11DecoderStage()
    {
        Shape = new FlowShape<IInputItem, HttpResponseMessage>(_in, _out);
    }

    public override FlowShape<IInputItem, HttpResponseMessage> Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http11DecoderStage _stage;
        private readonly Http11Decoder _decoder = new();

        /// <summary>
        /// Holds a response whose body is delimited by connection close (no Content-Length,
        /// no Transfer-Encoding). Body data is accumulated in <see cref="_bodyOwners"/>
        /// until a <see cref="CloseSignalItem"/> arrives.
        /// </summary>
        private HttpResponseMessage? _pendingResponse;
        private List<NetworkBuffer>? _bodyOwners;

        /// <summary>
        /// Body bytes flushed from the decoder remainder when the close-delimited response
        /// is first detected (decoder internal buffer — one unavoidable copy).
        /// </summary>
        private byte[]? _initialBodyBytes;

        /// <summary>
        /// Set when <c>_in</c> receives <c>onUpstreamFinish</c>. The stage defers
        /// <see cref="GraphStageLogic.CompleteStage"/> until any pending
        /// <see cref="GraphStageLogic.EmitMultiple{T}(Outlet{T},IEnumerable{T})"/>
        /// emissions have drained, preventing response loss on fast connection close.
        /// </summary>
        private bool _inputFinished;

        public Logic(Http11DecoderStage stage) : base(stage.Shape)
        {
            _stage = stage;
            SetHandler(stage._in,
                onPush: () =>
                {
                    var item = Grab(stage._in);

                    if (item is CloseSignalItem closeSignal)
                    {
                        HandleClose(closeSignal.CloseKind);
                        return;
                    }

                    if (item is not NetworkBuffer dataItem)
                    {
                        Pull(stage._in);
                        return;
                    }

                    try
                    {
                        // If we're accumulating a connection-close-delimited body,
                        // take ownership of the buffer instead of copying to byte[].
                        if (_pendingResponse is not null)
                        {
                            _bodyOwners ??= [];
                            _bodyOwners.Add(dataItem);
                            // Ownership transferred — do NOT call dataItem.Dispose()
                            Pull(stage._in);
                            return;
                        }

                        var data = dataItem.Memory;

                        if (_decoder.TryDecode(data, out var responses))
                        {
                            dataItem.Dispose();

                            // Check if the last response is connection-close-delimited
                            var last = responses[^1];
                            if (IsCloseDelimited(last))
                            {
                                // Emit all responses except the last one
                                if (responses.Count > 1)
                                {
                                    EmitMultiple(stage._out, responses.Take(responses.Count - 1));
                                }

                                // Hold the last response — body is delimited by connection close
                                _pendingResponse = last;
                                _bodyOwners = [];

                                // Flush any body data the decoder stored in its remainder
                                // (happens when body data is in the same chunk as headers)
                                var remainder = _decoder.FlushRemainder();
                                _initialBodyBytes = remainder.Length > 0 ? remainder : null;

                                Pull(stage._in);
                            }
                            else
                            {
                                EmitMultiple(stage._out, responses);
                            }
                        }
                        else
                        {
                            // Not enough data yet – return the buffer and wait for more
                            dataItem.Dispose();
                            Pull(stage._in);
                        }
                    }
                    catch (Exception ex)
                    {
                        dataItem.Dispose();
                        Log.Warning("Http11DecoderStage: Failed to decode response: {0}", ex.Message);
                        _decoder.Reset();
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: () =>
                {
                    _inputFinished = true;
                    // Do NOT CompleteStage here — EmitMultiple may have queued
                    // responses that haven't been pushed yet. CompleteStage would
                    // drop them, causing the correlation stage to see an orphaned
                    // request. The _out onPull handler will complete after all
                    // pending emissions have drained.
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http11DecoderStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_inputFinished)
                    {
                        CompleteStage();
                        return;
                    }

                    Pull(stage._in);
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        /// <summary>
        /// RFC 9112 §6.3: A response without Content-Length or Transfer-Encoding
        /// has its body delimited by connection close.
        /// </summary>
        // 1xx (informational), 204 (No Content), 304 (Not Modified) never carry a message body.
        private static bool IsStatusWithoutBody(int status) =>
            status is >= 100 and < 200 or 204 or 304;

        private static bool IsCloseDelimited(HttpResponseMessage response)
        {
            var status = (int)response.StatusCode;

            if (IsStatusWithoutBody(status))
            {
                return false;
            }

            // Transfer-Encoding present — body is chunked
            if (response.Headers.TransferEncodingChunked == true)
            {
                return false;
            }

            // Content-Length explicitly set — body length is known
            if (response.Content.Headers.Contains("Content-Length"))
            {
                return false;
            }

            return true;
        }

        private void HandleClose(TlsCloseKind closeKind)
        {
            if (_pendingResponse is not null)
            {
                if (closeKind == TlsCloseKind.CleanClose)
                {
                    // RFC 9112 §9.8: connection close is a valid body delimiter.
                    var content = new PooledChunksContent(_initialBodyBytes, _bodyOwners);
                    _pendingResponse.Content = content;
                    var response = _pendingResponse;
                    _pendingResponse = null;
                    _bodyOwners = null;
                    _initialBodyBytes = null;
                    Emit(_stage._out, response, CompleteStage);
                }
                else
                {
                    Log.Warning("Http11DecoderStage: Abrupt connection close — discarding incomplete response");
                    if (_bodyOwners is not null)
                    {
                        foreach (var buf in _bodyOwners)
                        {
                            buf.Dispose();
                        }
                    }

                    _pendingResponse = null;
                    _bodyOwners = null;
                    _initialBodyBytes = null;
                    CompleteStage();
                }

                return;
            }

            if (closeKind == TlsCloseKind.CleanClose)
            {
                // Flush any partially buffered response whose body was delimited by close.
                if (_decoder.TryDecodeEof(out var response) && response is not null)
                {
                    Emit(_stage._out, response, CompleteStage);
                    return;
                }
            }
            else
            {
                Log.Warning("Http11DecoderStage: Abrupt connection close — discarding incomplete response");
            }

            CompleteStage();
        }
    }

    /// <summary>
    /// An <see cref="HttpContent"/> that holds pooled <see cref="IMemoryOwner{T}"/> chunks
    /// accumulated during connection-close-delimited body streaming. Disposes all owners
    /// when the content is disposed, returning buffers to the pool without an extra copy.
    /// </summary>
    private sealed class PooledChunksContent : HttpContent
    {
        private readonly byte[]? _initial;
        private readonly List<NetworkBuffer>? _chunks;

        public PooledChunksContent(byte[]? initial, List<NetworkBuffer>? chunks)
        {
            _initial = initial;
            _chunks = chunks;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            if (_initial is { Length: > 0 })
            {
                await stream.WriteAsync(_initial, ct).ConfigureAwait(false);
            }

            if (_chunks is not null)
            {
                foreach (var buf in _chunks)
                {
                    await stream.WriteAsync(buf.Memory, ct).ConfigureAwait(false);
                }
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _initial?.Length ?? 0;
            if (_chunks is not null)
            {
                foreach (var buf in _chunks)
                {
                    length += buf.Length;
                }
            }

            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _chunks is not null)
            {
                foreach (var buf in _chunks)
                {
                    buf.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
