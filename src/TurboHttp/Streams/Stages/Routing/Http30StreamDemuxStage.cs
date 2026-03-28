using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;

namespace TurboHttp.Streams.Stages.Routing;

/// <summary>
/// Custom shape for <see cref="Http30StreamDemuxStage"/>: one inlet for tagged output items,
/// three outlets — one per QUIC stream type (request, control, QPACK encoder).
/// </summary>
public sealed class Http30StreamDemuxShape : Shape
{
    public Inlet<IOutputItem> In { get; }
    public Outlet<IOutputItem> OutRequest { get; }
    public Outlet<IOutputItem> OutControl { get; }
    public Outlet<IOutputItem> OutEncoder { get; }

    public Http30StreamDemuxShape(
        Inlet<IOutputItem> @in,
        Outlet<IOutputItem> outRequest,
        Outlet<IOutputItem> outControl,
        Outlet<IOutputItem> outEncoder)
    {
        In = @in;
        OutRequest = outRequest;
        OutControl = outControl;
        OutEncoder = outEncoder;
    }

    public override ImmutableArray<Inlet> Inlets => [In];

    public override ImmutableArray<Outlet> Outlets => [OutRequest, OutControl, OutEncoder];

    public override Shape DeepCopy()
    {
        return new Http30StreamDemuxShape(
            (Inlet<IOutputItem>)In.CarbonCopy(),
            (Outlet<IOutputItem>)OutRequest.CarbonCopy(),
            (Outlet<IOutputItem>)OutControl.CarbonCopy(),
            (Outlet<IOutputItem>)OutEncoder.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http30StreamDemuxShape(
            (Inlet<IOutputItem>)inlets[0],
            (Outlet<IOutputItem>)outlets[0],
            (Outlet<IOutputItem>)outlets[1],
            (Outlet<IOutputItem>)outlets[2]);
    }
}

/// <summary>
/// RFC 9114 §6.2 — Demultiplexes tagged <see cref="IOutputItem"/> instances to the correct
/// QUIC stream outlet based on their <see cref="OutputStreamType"/> tag.
///
/// Items wrapped in <see cref="Http3TaggedItem"/> are routed by stream type:
/// <list type="bullet">
///   <item><see cref="OutputStreamType.Request"/> → <c>OutRequest</c> (bidirectional request stream)</item>
///   <item><see cref="OutputStreamType.Control"/> → <c>OutControl</c> (unidirectional control stream)</item>
///   <item><see cref="OutputStreamType.QpackEncoder"/> → <c>OutEncoder</c> (unidirectional QPACK encoder stream)</item>
/// </list>
/// Untagged items default to the request outlet.
/// </summary>
/// <remarks>
/// Each outlet has independent backpressure. When a target outlet is not available,
/// the item is buffered until the outlet signals demand.
/// </remarks>
public sealed class Http30StreamDemuxStage : GraphStage<Http30StreamDemuxShape>
{
    private readonly Inlet<IOutputItem> _in = new("Http30StreamDemux.In");
    private readonly Outlet<IOutputItem> _outRequest = new("Http30StreamDemux.Out.Request");
    private readonly Outlet<IOutputItem> _outControl = new("Http30StreamDemux.Out.Control");
    private readonly Outlet<IOutputItem> _outEncoder = new("Http30StreamDemux.Out.Encoder");

    public Http30StreamDemuxStage()
    {
        Shape = new Http30StreamDemuxShape(_in, _outRequest, _outControl, _outEncoder);
    }

    public override Http30StreamDemuxShape Shape { get; }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Queue<IOutputItem> _pendingRequest = new();
        private readonly Queue<IOutputItem> _pendingControl = new();
        private readonly Queue<IOutputItem> _pendingEncoder = new();
        private bool _upstreamFinished;

        public Logic(Http30StreamDemuxStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in, onPush: () =>
            {
                var item = Grab(stage._in);
                Enqueue(item);
                DrainAll(stage);
            }, onUpstreamFinish: () =>
            {
                _upstreamFinished = true;

                if (_pendingRequest.Count == 0 && _pendingControl.Count == 0 && _pendingEncoder.Count == 0)
                {
                    CompleteStage();
                }
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http30StreamDemuxStage: Upstream failure: {0}", ex.Message);
                Log.Debug("Http30StreamDemuxStage: Failing stage due to upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outRequest, onPull: () => DrainAll(stage));
            SetHandler(stage._outControl, onPull: () => DrainAll(stage));
            SetHandler(stage._outEncoder, onPull: () => DrainAll(stage));
        }

        private void Enqueue(IOutputItem item)
        {
            if (item is Http3TaggedItem tagged)
            {
                switch (tagged.StreamType)
                {
                    case OutputStreamType.Control:
                        _pendingControl.Enqueue(tagged);
                        return;
                    case OutputStreamType.QpackEncoder:
                        _pendingEncoder.Enqueue(tagged);
                        return;
                    default:
                        _pendingRequest.Enqueue(tagged);
                        return;
                }
            }

            // Untagged items default to request outlet.
            _pendingRequest.Enqueue(item);
        }

        private void DrainAll(Http30StreamDemuxStage stage)
        {
            DrainQueue(_pendingRequest, stage._outRequest);
            DrainQueue(_pendingControl, stage._outControl);
            DrainQueue(_pendingEncoder, stage._outEncoder);

            if (_pendingRequest.Count == 0 && _pendingControl.Count == 0 && _pendingEncoder.Count == 0)
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

        private void DrainQueue(Queue<IOutputItem> queue, Outlet<IOutputItem> outlet)
        {
            while (queue.Count > 0 && IsAvailable(outlet))
            {
                Push(outlet, queue.Dequeue());
            }
        }

        private void TryPullUpstream(Http30StreamDemuxStage stage)
        {
            if (!HasBeenPulled(stage._in)
                && _pendingRequest.Count == 0
                && _pendingControl.Count == 0
                && _pendingEncoder.Count == 0)
            {
                Pull(stage._in);
            }
        }
    }
}
