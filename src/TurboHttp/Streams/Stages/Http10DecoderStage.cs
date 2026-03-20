using System;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC1945;

namespace TurboHttp.Streams.Stages;

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

                    if (item is not DataItem dataItem)
                    {
                        Pull(stage._in);
                        return;
                    }

                    try
                    {
                        var data = dataItem.Memory.Memory[..dataItem.Length];

                        if (_decoder.TryDecode(data, out var response) && response is not null)
                        {
                            dataItem.Memory.Dispose();
                            Push(stage._out, response);
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
                },
                onUpstreamFailure: ex => Log.Warning("Http10DecoderStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out, onPull: () => Pull(stage._in), onDownstreamFinish: _ => CompleteStage());
        }
    }
}
