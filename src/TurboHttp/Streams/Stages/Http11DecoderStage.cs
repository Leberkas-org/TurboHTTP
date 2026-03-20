using System;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Streams.Stages;

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
            private readonly Http11Decoder _decoder = new();

            public Logic(Http11DecoderStage stage) : base(stage.Shape)
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

                            if (_decoder.TryDecode(data, out var response))
                            {
                                dataItem.Memory.Dispose();
                                EmitMultiple(stage._out, response);
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
                    onUpstreamFailure: ex => Log.Warning("Http11DecoderStage: Upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._out,
                    onPull: () => Pull(stage._in),
                    onDownstreamFinish: _ => CompleteStage());
            }
        }
    }
