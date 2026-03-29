using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Streams.Stages.Encoding;

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
        private readonly Queue<Http3Frame> _pending = new();
        private ReadOnlyMemory<byte> _pendingInstructions;
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
                var instructions = stage._encoder.EncoderInstructions;
                _pendingInstructions = instructions.Length > 0
                    ? instructions.ToArray()
                    : ReadOnlyMemory<byte>.Empty;

                foreach (var f in frames)
                {
                    _pending.Enqueue(f);
                }

                DrainAll(stage);
            }, onUpstreamFinish: () =>
            {
                _upstreamFinished = true;

                if (_pending.Count == 0 && _pendingInstructions.Length == 0)
                {
                    CompleteStage();
                }
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http30Request2FrameStage: Upstream failure absorbed: {0}", ex.Message);
                Log.Debug("Http30Request2FrameStage: Failing stage due to upstream error: {0}", ex.Message);
                FailStage(ex);
            });

            SetHandler(stage._outFrame, onPull: () => DrainAll(stage));
            SetHandler(stage._outEncoder, onPull: () => DrainAll(stage));
        }

        private void DrainAll(Http30Request2FrameStage stage)
        {
            // Push pending encoder instructions if the outlet is available.
            if (_pendingInstructions.Length > 0 && IsAvailable(stage._outEncoder))
            {
                var instructions = _pendingInstructions;
                _pendingInstructions = ReadOnlyMemory<byte>.Empty;
                Push(stage._outEncoder, instructions);
            }

            // Push pending frames.
            while (_pending.Count > 0 && IsAvailable(stage._outFrame))
            {
                Push(stage._outFrame, _pending.Dequeue());
            }

            // Check completion.
            if (_pending.Count == 0 && _pendingInstructions.Length == 0)
            {
                if (_upstreamFinished)
                {
                    CompleteStage();
                }
                else
                {
                    TryPullUpstream(stage);
                }
            }
        }

        private void TryPullUpstream(Http30Request2FrameStage stage)
        {
            if (!HasBeenPulled(stage._in)
                && _pending.Count == 0
                && _pendingInstructions.Length == 0
                && IsAvailable(stage._outFrame)
                && IsAvailable(stage._outEncoder))
            {
                Pull(stage._in);
            }
        }
    }
}
