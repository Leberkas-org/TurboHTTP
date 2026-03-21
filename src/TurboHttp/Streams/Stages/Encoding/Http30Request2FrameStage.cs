using System.Collections.Generic;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Streams.Stages.Encoding;

/// <summary>
/// RFC 9114 §4.1 — Converts an <see cref="HttpRequestMessage"/> into a sequence of
/// <see cref="Http3Frame"/> objects (HEADERS + DATA) using QPACK header compression.
///
/// Unlike the HTTP/2 <see cref="Request2FrameStage"/>, no stream identifier is needed
/// because QUIC provides stream multiplexing at the transport layer.
/// </summary>
public sealed class Http30Request2FrameStage : GraphStage<FlowShape<HttpRequestMessage, Http3Frame>>
{
    private readonly Inlet<HttpRequestMessage> _in = new("Http30Request2Frame.In");
    private readonly Outlet<Http3Frame> _out = new("Http30Request2Frame.Out");
    private readonly Http3RequestEncoder _encoder;

    public Http30Request2FrameStage(Http3RequestEncoder encoder)
    {
        _encoder = encoder;
    }

    public override FlowShape<HttpRequestMessage, Http3Frame> Shape => new(_in, _out);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<Http3Frame> _pending = new();
        private bool _upstreamFinished;

        public Logic(Http30Request2FrameStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, onPush: () =>
            {
                var request = Grab(stage._in);
                var frames = stage._encoder.Encode(request);

                foreach (var f in frames)
                {
                    _pending.Enqueue(f);
                }

                Drain(stage);
            }, onUpstreamFinish: () =>
            {
                _upstreamFinished = true;

                if (_pending.Count == 0)
                {
                    CompleteStage();
                }
            }, onUpstreamFailure: ex => Log.Warning("Http30Request2FrameStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out, onPull: () => Drain(stage));
        }

        private void Drain(Http30Request2FrameStage stage)
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
