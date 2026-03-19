using System.Collections.Generic;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages;

public sealed class Request2FrameStage : GraphStage<FlowShape<(HttpRequestMessage, int), Http2Frame>>
{
    private readonly Inlet<(HttpRequestMessage, int)> _inlet = new("req.in");
    private readonly Outlet<Http2Frame> _outlet = new("req.out");
    private readonly Http2RequestEncoder _encoder;

    public Request2FrameStage(Http2RequestEncoder encoder)
    {
        _encoder = encoder;
    }

    public override FlowShape<(HttpRequestMessage, int), Http2Frame> Shape => new(_inlet, _outlet);


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<Http2Frame> _pending = new();
        private bool _upstreamFinished;

        public Logic(Request2FrameStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet, onPush: () =>
            {
                var (request, streamId) = Grab(stage._inlet);
                var (_, frames) = stage._encoder.Encode(request, streamId);

                var endpoint = request.RequestUri is not null && request.Version is not null
                    ? RequestEndpoint.FromRequest(request)
                    : RequestEndpoint.Default;
                var first = true;

                foreach (var f in frames)
                {
                    if (first)
                    {
                        f.Endpoint = endpoint;
                        first = false;
                    }

                    _pending.Enqueue(f);
                }

                Drain(stage);
            }, onUpstreamFinish: () =>
            {
                _upstreamFinished = true;

                // Don't complete yet — drain remaining frames first.
                if (_pending.Count == 0)
                {
                    CompleteStage();
                }
            });

            SetHandler(stage._outlet, onPull: () => Drain(stage));
        }

        private void Drain(Request2FrameStage stage)
        {
            while (_pending.Count > 0 && IsAvailable(stage._outlet))
            {
                Push(stage._outlet, _pending.Dequeue());
            }

            if (_pending.Count == 0)
            {
                if (_upstreamFinished)
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(stage._inlet))
                {
                    Pull(stage._inlet);
                }
            }
        }
    }
}