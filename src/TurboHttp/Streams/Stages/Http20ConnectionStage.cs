using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages;

public sealed class Http20ConnectionShape : Shape
{
    public Inlet<Http2Frame> ServerIn { get; }
    public Outlet<Http2Frame> AppOut { get; }
    public Inlet<Http2Frame> AppIn { get; }
    public Outlet<Http2Frame> ServerOut { get; }
    public Outlet<IControlItem> OutletSignal { get; }

    public Http20ConnectionShape(
        Inlet<Http2Frame> serverIn,
        Outlet<Http2Frame> appOut,
        Inlet<Http2Frame> appIn,
        Outlet<Http2Frame> serverOut,
        Outlet<IControlItem> outletSignal)
    {
        ServerIn = serverIn;
        AppOut = appOut;
        AppIn = appIn;
        ServerOut = serverOut;
        OutletSignal = outletSignal;
    }

    public override ImmutableArray<Inlet> Inlets =>
        ImmutableArray.Create<Inlet>(ServerIn, AppIn);

    public override ImmutableArray<Outlet> Outlets =>
        ImmutableArray.Create<Outlet>(AppOut, ServerOut, OutletSignal);

    public override Shape DeepCopy()
    {
        return new Http20ConnectionShape(
            (Inlet<Http2Frame>)ServerIn.CarbonCopy(),
            (Outlet<Http2Frame>)AppOut.CarbonCopy(),
            (Inlet<Http2Frame>)AppIn.CarbonCopy(),
            (Outlet<Http2Frame>)ServerOut.CarbonCopy(),
            (Outlet<IControlItem>)OutletSignal.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http20ConnectionShape(
            (Inlet<Http2Frame>)inlets[0],
            (Outlet<Http2Frame>)outlets[0],
            (Inlet<Http2Frame>)inlets[1],
            (Outlet<Http2Frame>)outlets[1],
            (Outlet<IControlItem>)outlets[2]);
    }
}

public sealed class Http20ConnectionStage : GraphStage<Http20ConnectionShape>
{
    private readonly Inlet<Http2Frame> _inletRaw = new("h2.server.in");
    private readonly Outlet<Http2Frame> _outletStream = new("h2.app.out");
    private readonly Inlet<Http2Frame> _inletRequest = new("h2.app.in");
    private readonly Outlet<Http2Frame> _outletRaw = new("h2.server.out");
    private readonly Outlet<IControlItem> _outletSignal = new("h2.signal.out");

    private readonly int _initialRecvWindowSize;
    private readonly int _maxConcurrentStreams;

    public Http20ConnectionStage(int initialRecvWindowSize = 65535, int maxConcurrentStreams = 100)
    {
        _initialRecvWindowSize = initialRecvWindowSize;
        _maxConcurrentStreams = maxConcurrentStreams;
    }

    public override Http20ConnectionShape Shape =>
        new(_inletRaw, _outletStream, _inletRequest, _outletRaw, _outletSignal);


    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly Http20ConnectionStage _stage;
        private int _connectionWindow;
        private int _initialRecvStreamWindow;
        private int _initialSendStreamWindow = 65535;
        private int _maxConcurrentStreams;
        private int _activeStreams;
        private bool _goAwayReceived;

        private RequestEndpoint _endpoint;
        private readonly Dictionary<int, int> _streamWindows = new();
        private readonly HashSet<int> _activeStreamIds = new();
        private readonly Queue<Http2Frame> _outboundQueue = new();

        public Logic(Http20ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _connectionWindow = stage._initialRecvWindowSize;
            _initialRecvStreamWindow = stage._initialRecvWindowSize;
            _maxConcurrentStreams = stage._maxConcurrentStreams;

            SetHandler(stage._inletRaw, onPush: () =>
            {
                var frame = Grab(stage._inletRaw);

                switch (frame)
                {
                    case SettingsFrame settings:
                        HandleSettings(settings);
                        break;

                    case DataFrame data:
                        if (!HandleInboundData(data))
                        {
                            return;
                        }
                        if (data.EndStream)
                        {
                            CloseStream(data.StreamId);
                        }
                        break;

                    case HeadersFrame headers:
                        if (headers.EndStream)
                        {
                            CloseStream(headers.StreamId);
                        }
                        break;

                    case RstStreamFrame rst:
                        CloseStream(rst.StreamId);
                        break;

                    case WindowUpdateFrame win:
                        HandleWindowUpdate(win);
                        break;

                    case PingFrame ping:
                        HandlePing(ping);
                        // PING is connection-level — not forwarded to app.
                        // Re-pull inbound to get the next app-visible frame.
                        Pull(stage._inletRaw);
                        return;

                    case GoAwayFrame goAway:
                        _goAwayReceived = true;
                        Log.Warning("Http20ConnectionStage: RFC 9113 §6.8 — GOAWAY received (lastStreamId={0}, errorCode={1}). Triggering reconnect.",
                            goAway.LastStreamId, goAway.ErrorCode);
                        Emit(_stage._outletSignal, new ConnectionReuseItem(_endpoint,
                            ConnectionReuseDecision.Close("RFC 9113 §6.8: GOAWAY received")));
                        break;
                }

                Push(stage._outletStream, frame);
            });

            SetHandler(stage._outletStream, onPull: () => Pull(stage._inletRaw));

            SetHandler(stage._inletRequest, onPush: () =>
            {
                var frame = Grab(stage._inletRequest);

                if (_goAwayReceived)
                {
                    Log.Warning("Http20ConnectionStage: RFC 9113 §6.8 — GOAWAY received; dropping new request frame (stream {0}).",
                        frame is HeadersFrame hf ? hf.StreamId : -1);
                    if (!HasBeenPulled(_stage._inletRequest) && !IsClosed(_stage._inletRequest))
                    {
                        Pull(_stage._inletRequest);
                    }
                    return;
                }

                switch (frame)
                {
                    case HeadersFrame headers:
                        _activeStreams++;
                        _activeStreamIds.Add(headers.StreamId);
                        if (_endpoint == default && headers.Endpoint.HasValue)
                        {
                            _endpoint = headers.Endpoint.Value;
                        }

                        Emit(stage._outletSignal, new StreamAcquireItem
                        {
                            Key = _endpoint
                        });
                        break;

                    case DataFrame data:
                        if (!HandleOutboundData(data))
                        {
                            return;
                        }
                        break;
                }

                EnqueueOutbound(frame);
            }, onUpstreamFinish: () =>
            {
                // Request stream finished — keep stage alive to receive server responses.
            });

            SetHandler(stage._outletRaw, onPull: () =>
            {
                TryDrainOutbound();
            });

            SetHandler(stage._outletSignal, onPull: () =>
            {
                // Demand-driven by downstream MergePreferred; no action needed.
            });
        }

        private void EnqueueOutbound(Http2Frame frame)
        {
            _outboundQueue.Enqueue(frame);
            TryDrainOutbound();
        }

        private void TryDrainOutbound()
        {
            if (_outboundQueue.Count > 0 && IsAvailable(_stage._outletRaw))
            {
                Push(_stage._outletRaw, _outboundQueue.Dequeue());
                return;
            }

            if (_outboundQueue.Count == 0)
            {
                TryPullRequest();
            }
        }

        private void HandleSettings(SettingsFrame frame)
        {
            if (frame.IsAck)
            {
                return;
            }

            foreach (var (key, value) in frame.Parameters)
            {
                if (key == SettingsParameter.InitialWindowSize)
                {
                    // Server's InitialWindowSize controls how much the CLIENT can SEND per stream
                    _initialSendStreamWindow = (int)value;
                }

                if (key == SettingsParameter.MaxConcurrentStreams)
                {
                    _maxConcurrentStreams = (int)value;
                    Emit(_stage._outletSignal, new MaxConcurrentStreamsItem(_maxConcurrentStreams)
                    {
                        Key = _endpoint
                    });
                }
            }

            EnqueueOutbound(new SettingsFrame([], isAck: true));
        }

        private bool HandleInboundData(DataFrame frame)
        {
            var dataLength = frame.Data.Length;

            _connectionWindow -= dataLength;

            _streamWindows.TryAdd(frame.StreamId, _initialRecvStreamWindow);

            _streamWindows[frame.StreamId] -= dataLength;

            if (_connectionWindow < 0)
            {
                Log.Warning("Http20ConnectionStage: RFC 9113 §6.9 — connection flow control window exceeded by {0} bytes. Triggering reconnect.",
                    -_connectionWindow);
                Emit(_stage._outletSignal, new ConnectionReuseItem(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: connection window exceeded")));
                Pull(_stage._inletRaw);
                return false;
            }

            if (_streamWindows[frame.StreamId] < 0)
            {
                Log.Warning("Http20ConnectionStage: RFC 9113 §6.9 — stream {0} flow control window exceeded by {1} bytes. Triggering reconnect.",
                    frame.StreamId, -_streamWindows[frame.StreamId]);
                Emit(_stage._outletSignal, new ConnectionReuseItem(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: stream window exceeded")));
                Pull(_stage._inletRaw);
                return false;
            }

            // RFC 9113 §6.9: WINDOW_UPDATE increment of 0 is a protocol error.
            // Skip window updates for empty DATA frames (e.g. END_STREAM-only frames).
            if (dataLength > 0)
            {
                EnqueueOutbound(new WindowUpdateFrame(0, dataLength));
                EnqueueOutbound(new WindowUpdateFrame(frame.StreamId, dataLength));
            }

            return true;
        }

        private void HandlePing(PingFrame ping)
        {
            if (!ping.IsAck)
            {
                EnqueueOutbound(new PingFrame(ping.Data, true));
            }
        }

        private void HandleWindowUpdate(WindowUpdateFrame frame)
        {
            if (frame.StreamId == 0)
            {
                _connectionWindow += frame.Increment;
            }
            else
            {
                _streamWindows.TryAdd(frame.StreamId, _initialRecvStreamWindow);

                _streamWindows[frame.StreamId] += frame.Increment;
            }
        }

        private void CloseStream(int streamId)
        {
            if (_activeStreamIds.Remove(streamId))
            {
                _activeStreams--;
                TryDrainOutbound();
            }

            _streamWindows.Remove(streamId);
        }

        private void TryPullRequest()
        {
            if (_activeStreams < _maxConcurrentStreams
                && !HasBeenPulled(_stage._inletRequest)
                && !IsClosed(_stage._inletRequest)
                && IsAvailable(_stage._outletRaw))
            {
                Pull(_stage._inletRequest);
            }
        }

        private bool HandleOutboundData(DataFrame frame)
        {
            _connectionWindow -= frame.Data.Length;

            if (_connectionWindow < 0)
            {
                Log.Warning("Http20ConnectionStage: RFC 9113 §6.9 — outbound flow control connection window exceeded by {0} bytes. Triggering reconnect.",
                    -_connectionWindow);
                Emit(_stage._outletSignal, new ConnectionReuseItem(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: outbound flow control exceeded")));
                return false;
            }

            return true;
        }
    }
}
