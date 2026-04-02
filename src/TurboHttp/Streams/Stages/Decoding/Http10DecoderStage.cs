using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.Http10;

namespace TurboHttp.Streams.Stages.Decoding;

public sealed class Http10DecoderStage : GraphStage<FlowShape<IInputItem, HttpResponseMessage>>
{
    private readonly Inlet<IInputItem> _in = new("Http10Decoder.In");
    private readonly Outlet<HttpResponseMessage> _out = new("Http10Decoder.Out");

    public override FlowShape<IInputItem, HttpResponseMessage> Shape { get; }


    public Http10DecoderStage()
    {
        Shape = new FlowShape<IInputItem, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http10Decoder _decoder = new();

        public Logic(Http10DecoderStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var item = Grab(stage._in);

                    // Connection closed: flush any partially buffered response
                    // whose body is delimited by connection close (RFC 1945 §7.2.2).
                    if (item is CloseSignalItem closeSignal)
                    {
                        if (closeSignal.CloseKind == TlsCloseKind.AbruptClose)
                        {
                            // Abrupt close (connection reset).
                            // If the decoder was waiting for body data due to Content-Length,
                            // it's a Content-Length mismatch — treat as an error.
                            var message = _decoder.IsWaitingForContentLength
                                ? "Content-Length mismatch: connection closed before all body data was received."
                                : "Connection was aborted while receiving HTTP/1.0 response.";

                            _decoder.Reset();
                            FailStage(new HttpRequestException(message));
                            return;
                        }

                        // Clean close: body is delimited by connection close.
                        if (_decoder.TryDecodeEof(out var eofResponse) && eofResponse is not null)
                        {
                            Push(stage._out, eofResponse);
                        }
                        else
                        {
                            _decoder.Reset();
                            Pull(stage._in);
                        }

                        return;
                    }

                    if (item is not NetworkBuffer dataItem)
                    {
                        Pull(stage._in);
                        return;
                    }

                    try
                    {
                        var data = dataItem.Memory;

                        if (_decoder.TryDecode(data, out var response) && response is not null)
                        {
                            dataItem.Dispose();
                            Push(stage._out, response);
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
                        Log.Warning("Http10DecoderStage: Failed to decode response: {0}", ex.Message);
                        _decoder.Reset();
                        if (!HasBeenPulled(stage._in))
                        {
                            Pull(stage._in);
                        }
                    }
                },
                onUpstreamFinish: () =>
                {
                    try
                    {
                        // Flush any partial response buffered in the decoder
                        if (_decoder.TryDecodeEof(out var response) && response is not null)
                        {
                            Emit(stage._out, response, CompleteStage);
                        }
                        else
                        {
                            _decoder.Reset();
                            CompleteStage();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Http10DecoderStage: Failed to decode EOF: {0}", ex.Message);
                        _decoder.Reset();
                        FailStage(ex);
                    }
                },
                onUpstreamFailure: ex =>
                {
                    Log.Warning("Http10DecoderStage: Upstream failure absorbed: {0}", ex.Message);
                    CompleteStage();
                });

            SetHandler(stage._out, onPull: () => Pull(stage._in), onDownstreamFinish: _ => CompleteStage());
        }
    }
}
