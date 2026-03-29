using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9112;

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
            /// no Transfer-Encoding). Body data is accumulated in <see cref="_bodyChunks"/>
            /// until a <see cref="CloseSignalItem"/> arrives.
            /// </summary>
            private HttpResponseMessage? _pendingResponse;
            private List<byte[]>? _bodyChunks;

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

                        if (item is not DataItem dataItem)
                        {
                            Pull(stage._in);
                            return;
                        }

                        try
                        {
                            // If we're accumulating a connection-close-delimited body,
                            // buffer the raw bytes instead of feeding to the decoder.
                            if (_pendingResponse is not null)
                            {
                                var bodyBytes = dataItem.Memory.Memory[..dataItem.Length].ToArray();
                                dataItem.Memory.Dispose();
                                _bodyChunks ??= [];
                                _bodyChunks.Add(bodyBytes);
                                Pull(stage._in);
                                return;
                            }

                            var data = dataItem.Memory.Memory[..dataItem.Length];

                            if (_decoder.TryDecode(data, out var responses))
                            {
                                dataItem.Memory.Dispose();

                                // Check if the last response is connection-close-delimited
                                var last = responses[responses.Count - 1];
                                if (IsCloseDelimited(last))
                                {
                                    // Emit all responses except the last one
                                    if (responses.Count > 1)
                                    {
                                        EmitMultiple(stage._out, responses.RemoveAt(responses.Count - 1));
                                    }

                                    // Hold the last response — body is delimited by connection close
                                    _pendingResponse = last;
                                    _bodyChunks = [];

                                    // Flush any body data the decoder stored in its remainder
                                    // (happens when body data is in the same chunk as headers)
                                    var remainder = _decoder.FlushRemainder();
                                    if (remainder.Length > 0)
                                    {
                                        _bodyChunks.Add(remainder);
                                    }

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
                                dataItem.Memory.Dispose();
                                Pull(stage._in);
                            }
                        }
                        catch (Exception ex)
                        {
                            dataItem.Memory.Dispose();
                            Log.Warning("Http11DecoderStage: Failed to decode response: {0}", ex.Message);
                            _decoder.Reset();
                            if (!HasBeenPulled(stage._in))
                            {
                                Pull(stage._in);
                            }
                        }
                    },
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: ex =>
                    {
                        Log.Warning("Http11DecoderStage: Upstream failure absorbed: {0}", ex.Message);
                        CompleteStage();
                    });

                SetHandler(stage._out,
                    onPull: () => Pull(stage._in),
                    onDownstreamFinish: _ => CompleteStage());
            }

            /// <summary>
            /// RFC 9112 §6.3: A response without Content-Length or Transfer-Encoding
            /// has its body delimited by connection close.
            /// </summary>
            private static bool IsCloseDelimited(HttpResponseMessage response)
            {
                var status = (int)response.StatusCode;

                // 1xx, 204, 304 never have a body
                if (status is >= 100 and < 200 or 204 or 304)
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
                        var body = AssembleBody();
                        _pendingResponse.Content = new ByteArrayContent(body);
                        var response = _pendingResponse;
                        _pendingResponse = null;
                        _bodyChunks = null;
                        Emit(_stage._out, response, CompleteStage);
                    }
                    else
                    {
                        Log.Warning("Http11DecoderStage: Abrupt connection close — discarding incomplete response");
                        _pendingResponse = null;
                        _bodyChunks = null;
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

            private byte[] AssembleBody()
            {
                if (_bodyChunks is null or { Count: 0 })
                {
                    return [];
                }

                if (_bodyChunks.Count == 1)
                {
                    return _bodyChunks[0];
                }

                var totalLength = 0;
                foreach (var chunk in _bodyChunks)
                {
                    totalLength += chunk.Length;
                }

                var result = new byte[totalLength];
                var offset = 0;
                foreach (var chunk in _bodyChunks)
                {
                    chunk.CopyTo(result, offset);
                    offset += chunk.Length;
                }

                return result;
            }
        }
    }
