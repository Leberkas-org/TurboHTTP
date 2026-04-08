using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages.Encoding;

/// <summary>
/// Custom shape for <see cref="Http30Request2FrameStage"/>: one inlet for requests,
/// two outlets — one for HTTP/3 frames, one for QPACK encoder instructions.
/// </summary>
public sealed class Http30Request2FrameShape : Shape
{
    public Inlet<HttpRequestMessage> In { get; }
    public Outlet<Http3Frame> OutFrame { get; }
    public Outlet<ReadOnlyMemory<byte>> OutEncoder { get; }

    public Http30Request2FrameShape(
        Inlet<HttpRequestMessage> @in,
        Outlet<Http3Frame> outFrame,
        Outlet<ReadOnlyMemory<byte>> outEncoder)
    {
        In = @in;
        OutFrame = outFrame;
        OutEncoder = outEncoder;
    }

    public override ImmutableArray<Inlet> Inlets => [In];

    public override ImmutableArray<Outlet> Outlets => [OutFrame, OutEncoder];

    public override Shape DeepCopy()
    {
        return new Http30Request2FrameShape(
            (Inlet<HttpRequestMessage>)In.CarbonCopy(),
            (Outlet<Http3Frame>)OutFrame.CarbonCopy(),
            (Outlet<ReadOnlyMemory<byte>>)OutEncoder.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http30Request2FrameShape(
            (Inlet<HttpRequestMessage>)inlets[0],
            (Outlet<Http3Frame>)outlets[0],
            (Outlet<ReadOnlyMemory<byte>>)outlets[1]);
    }
}

/// <summary>
/// RFC 9114 §4.1 — Converts an <see cref="HttpRequestMessage"/> into a sequence of
/// <see cref="Http3Frame"/> objects (HEADERS + DATA) using QPACK header compression.
///
/// Unlike the HTTP/2 <see cref="Http20Request2FrameStage"/>, no stream identifier is needed
/// because QUIC provides stream multiplexing at the transport layer.
///
/// Emits QPACK encoder instructions (pre-serialised bytes) on a second outlet
/// whenever the dynamic table produces insert/capacity instructions.
/// </summary>
public sealed class Http30Request2FrameStage : GraphStage<Http30Request2FrameShape>
{
    private readonly Inlet<HttpRequestMessage> _in = new("Http30Request2Frame.In");
    private readonly Outlet<Http3Frame> _outFrame = new("Http30Request2Frame.Out.Frame");
    private readonly Outlet<ReadOnlyMemory<byte>> _outEncoder = new("Http30Request2Frame.Out.Encoder");
    private readonly Http3RequestEncoder _encoder;

    public Http30Request2FrameStage(Http3RequestEncoder encoder)
    {
        _encoder = encoder;
        Shape = new Http30Request2FrameShape(_in, _outFrame, _outEncoder);
    }

    public override Http30Request2FrameShape Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<Http3Frame> _pendingFrames = new();
        private readonly Queue<ReadOnlyMemory<byte>> _pendingInstructions = new();
        private bool _upstreamFinished;

        public Logic(Http30Request2FrameStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, onPush: () =>
            {
                var request = Grab(stage._in);
                var isConnect = request.Method == HttpMethod.Connect;
                Http3OriginValidator.Validate(request.RequestUri!, isConnect);
                var frames = stage._encoder.Encode(request);

                // Capture encoder instructions before next Encode() overwrites them.
                // Queue them locally so a slow encoder stream does not block request flow.
                var instructions = stage._encoder.EncoderInstructions;
                if (instructions.Length > 0)
                {
                    _pendingInstructions.Enqueue(instructions.ToArray());
                }

                foreach (var f in frames)
                {
                    _pendingFrames.Enqueue(f);
                }

                DrainFrames(stage);
                DrainInstructions(stage);
            }, onUpstreamFinish: () =>
            {
                _upstreamFinished = true;

                if (_pendingFrames.Count == 0 && _pendingInstructions.Count == 0)
                {
                    CompleteStage();
                }
                else if (_pendingFrames.Count == 0)
                {
                    // No more frames to emit — complete the frame outlet.
                    // Instructions drain independently.
                    Complete(stage._outFrame);
                }
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http30Request2FrameStage: Upstream failure absorbed: {0}", ex.Message);
                Log.Debug("Http30Request2FrameStage: Failing stage due to upstream error: {0}", ex.Message);
                FailStage(ex);
            });

            SetHandler(stage._outFrame, onPull: () =>
            {
                DrainFrames(stage);
                TryPullUpstream(stage);
            });

            SetHandler(stage._outEncoder, onPull: () =>
            {
                DrainInstructions(stage);
                CheckCompletion(stage);
            });
        }

        /// <summary>
        /// Pushes queued frames to the frame outlet when it is available.
        /// </summary>
        private void DrainFrames(Http30Request2FrameStage stage)
        {
            while (_pendingFrames.Count > 0 && IsAvailable(stage._outFrame))
            {
                Push(stage._outFrame, _pendingFrames.Dequeue());
            }
        }

        /// <summary>
        /// Pushes queued encoder instructions to the encoder outlet when it is available.
        /// Instructions are buffered locally so a slow encoder stream does not block
        /// request processing (TASK-030-017).
        /// </summary>
        private void DrainInstructions(Http30Request2FrameStage stage)
        {
            while (_pendingInstructions.Count > 0 && IsAvailable(stage._outEncoder))
            {
                Push(stage._outEncoder, _pendingInstructions.Dequeue());
            }
        }

        /// <summary>
        /// Pulls upstream when the frame outlet is available and no frames are pending.
        /// Does NOT require the encoder outlet to be available — instructions queue
        /// independently and drain when the encoder stream pulls.
        /// </summary>
        private void TryPullUpstream(Http30Request2FrameStage stage)
        {
            if (!HasBeenPulled(stage._in)
                && _pendingFrames.Count == 0
                && IsAvailable(stage._outFrame)
                && !_upstreamFinished)
            {
                Pull(stage._in);
            }
        }

        /// <summary>
        /// Checks whether the stage can complete after draining instructions.
        /// </summary>
        private void CheckCompletion(Http30Request2FrameStage stage)
        {
            if (_upstreamFinished && _pendingFrames.Count == 0 && _pendingInstructions.Count == 0)
            {
                CompleteStage();
            }
        }
    }
}
