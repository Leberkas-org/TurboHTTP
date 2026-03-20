using System.Collections.Generic;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages;

public sealed class Request2FrameStage : GraphStage<FlowShape<(HttpRequestMessage, int), Http2Frame>>
{
    private readonly Inlet<(HttpRequestMessage, int)> _in = new("Request2Frame.In");
    private readonly Outlet<Http2Frame> _out = new("Request2Frame.Out");
    private readonly Http2RequestEncoder _encoder;

    public Request2FrameStage(Http2RequestEncoder encoder)
    {
        _encoder = encoder;
    }

    public override FlowShape<(HttpRequestMessage, int), Http2Frame> Shape => new(_in, _out);


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<Http2Frame> _pending = new();
        private bool _upstreamFinished;

        public Logic(Request2FrameStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, onPush: () =>
            {
                var (request, streamId) = Grab(stage._in);
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
            }, onUpstreamFailure: ex => Log.Warning("Request2FrameStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out, onPull: () => Drain(stage));
        }

        private void Drain(Request2FrameStage stage)
        {
            while (_pending.Count > 0 && IsAvailable(stage._out))
            {
                Push(stage._out, _pending.Dequeue());
            }

            if (_pending.Count == 0)
            {
                if (_upstreamFinished)
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(stage._in))
                {
                    Pull(stage._in);
                }
            }
        }
    }
}