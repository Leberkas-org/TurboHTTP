using System;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Streams.Stages;

public sealed class Http11DecoderStage : GraphStage<FlowShape<IInputItem, HttpResponseMessage>>
    {
        private readonly Inlet<IInputItem> _inlet = new("http11.decoder.in");
        private readonly Outlet<HttpResponseMessage> _outlet = new("http11.decoder.out");

        public Http11DecoderStage()
        {
            Shape = new FlowShape<IInputItem, HttpResponseMessage>(_inlet, _outlet);
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
                SetHandler(stage._inlet,
                    onPush: () =>
                    {
                        var item = Grab(stage._inlet);

                        if (item is not DataItem dataItem)
                        {
                            Pull(stage._inlet);
                            return;
                        }

                        try
                        {
                            var data = dataItem.Memory.Memory[..dataItem.Length];

                            if (_decoder.TryDecode(data, out var response))
                            {
                                dataItem.Memory.Dispose();
                                EmitMultiple(stage._outlet, response);
                            }
                            else
                            {
                                // Not enough data yet – return the buffer and wait for more
                                dataItem.Memory.Dispose();
                                Pull(stage._inlet);
                            }
                        }
                        catch (Exception ex)
                        {
                            dataItem.Memory.Dispose();
                            FailStage(ex);
                        }
                    },
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: FailStage);

                SetHandler(stage._outlet,
                    onPull: () => Pull(stage._inlet),
                    onDownstreamFinish: _ => CompleteStage());
            }
        }
    }
