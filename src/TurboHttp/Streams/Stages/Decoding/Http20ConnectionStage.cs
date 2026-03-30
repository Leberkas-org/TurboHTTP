using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Streams.Stages.Decoding;

public sealed class Http20ConnectionShape : Shape
{
    public Inlet<Http2Frame> InServer { get; }
    public Outlet<Http2Frame> OutStream { get; }
    public Inlet<Http2Frame> InApp { get; }
    public Outlet<Http2Frame> OutServer { get; }
    public Outlet<IControlItem> OutSignal { get; }

    public Http20ConnectionShape(
        Inlet<Http2Frame> inServer,
        Outlet<Http2Frame> outStream,
        Inlet<Http2Frame> inApp,
        Outlet<Http2Frame> outServer,
        Outlet<IControlItem> outSignal)
    {
        InServer = inServer;
        OutStream = outStream;
        InApp = inApp;
        OutServer = outServer;
        OutSignal = outSignal;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InServer, InApp];

    public override ImmutableArray<Outlet> Outlets =>
        [OutStream, OutServer, OutSignal];

    public override Shape DeepCopy()
    {
        return new Http20ConnectionShape(
            (Inlet<Http2Frame>)InServer.CarbonCopy(),
            (Outlet<Http2Frame>)OutStream.CarbonCopy(),
            (Inlet<Http2Frame>)InApp.CarbonCopy(),
            (Outlet<Http2Frame>)OutServer.CarbonCopy(),
            (Outlet<IControlItem>)OutSignal.CarbonCopy());
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
    private readonly Inlet<Http2Frame> _inServer = new("Http20Connection.In.Server");
    private readonly Outlet<Http2Frame> _outStream = new("Http20Connection.Out.Stream");
    private readonly Inlet<Http2Frame> _inApp = new("Http20Connection.In.App");
    private readonly Outlet<Http2Frame> _outServer = new("Http20Connection.Out.Server");
    private readonly Outlet<IControlItem> _outSignal = new("Http20Connection.Out.Signal");

    private readonly int _initialRecvWindowSize;
    private readonly int _maxConcurrentStreams;

    public Http20ConnectionStage(int initialRecvWindowSize = 65535, int maxConcurrentStreams = 100)
    {
        _initialRecvWindowSize = initialRecvWindowSize;
        _maxConcurrentStreams = maxConcurrentStreams;
    }

    public override Http20ConnectionShape Shape => new(_inServer, _outStream, _inApp, _outServer, _outSignal);


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
        private readonly HashSet<int> _activeStreamIds = [];
        private readonly Queue<Http2Frame> _outboundQueue = new();

        public Logic(Http20ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _connectionWindow = stage._initialRecvWindowSize;
            _initialRecvStreamWindow = stage._initialRecvWindowSize;
            _maxConcurrentStreams = stage._maxConcurrentStreams;

            SetHandler(stage._inServer, onPush: () =>
            {
                var frame = Grab(stage._inServer);

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
                        Pull(stage._inServer);
                        return;

                    case GoAwayFrame goAway:
                        _goAwayReceived = true;
                        Log.Warning("Http20ConnectionStage: RFC 9113 §6.8 — GOAWAY received (lastStreamId={0}, errorCode={1}). Triggering reconnect.",
                            goAway.LastStreamId, goAway.ErrorCode);
                        Emit(_stage._outSignal, new ConnectionReuseItem(_endpoint,
                            ConnectionReuseDecision.Close("RFC 9113 §6.8: GOAWAY received")));
                        break;
                }

                Push(stage._outStream, frame);
            }, onUpstreamFinish: () =>
            {
                Log.Debug("Http20ConnectionStage: Completing stage due to server inlet upstream finish.");
                CompleteStage();
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http20ConnectionStage: Server inlet upstream failure: {0}", ex.Message);
                Log.Debug("Http20ConnectionStage: Failing stage due to server inlet upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outStream, onPull: () =>
            {
                Pull(stage._inServer);
            });

            SetHandler(stage._inApp, onPush: () =>
            {
                var frame = Grab(stage._inApp);

                if (_goAwayReceived)
                {
                    Log.Warning("Http20ConnectionStage: RFC 9113 §6.8 — GOAWAY received; dropping new request frame (stream {0}).",
                        frame is HeadersFrame hf ? hf.StreamId : -1);
                    if (!HasBeenPulled(_stage._inApp) && !IsClosed(_stage._inApp))
                    {
                        Pull(_stage._inApp);
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

                        Emit(stage._outSignal, new StreamAcquireItem
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
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http20ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                Log.Debug("Http20ConnectionStage: Failing stage due to app inlet upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outServer, onPull: () =>
            {
                TryDrainOutbound();
            });

            SetHandler(stage._outSignal, onPull: () =>
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
            if (_outboundQueue.Count > 0 && IsAvailable(_stage._outServer))
            {
                Push(_stage._outServer, _outboundQueue.Dequeue());
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
                    Emit(_stage._outSignal, new MaxConcurrentStreamsItem(_maxConcurrentStreams)
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
                Emit(_stage._outSignal, new ConnectionReuseItem(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: connection window exceeded")));
                Pull(_stage._inServer);
                return false;
            }

            if (_streamWindows[frame.StreamId] < 0)
            {
                Log.Warning("Http20ConnectionStage: RFC 9113 §6.9 — stream {0} flow control window exceeded by {1} bytes. Triggering reconnect.",
                    frame.StreamId, -_streamWindows[frame.StreamId]);
                Emit(_stage._outSignal, new ConnectionReuseItem(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: stream window exceeded")));
                Pull(_stage._inServer);
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
                && !HasBeenPulled(_stage._inApp)
                && !IsClosed(_stage._inApp)
                && IsAvailable(_stage._outServer))
            {
                Pull(_stage._inApp);
            }
        }

        private bool HandleOutboundData(DataFrame frame)
        {
            _connectionWindow -= frame.Data.Length;

            if (_connectionWindow < 0)
            {
                Log.Warning("Http20ConnectionStage: RFC 9113 §6.9 — outbound flow control connection window exceeded by {0} bytes. Triggering reconnect.",
                    -_connectionWindow);
                Emit(_stage._outSignal, new ConnectionReuseItem(_endpoint,
                    ConnectionReuseDecision.Close("RFC 9113 §6.9: outbound flow control exceeded")));
                return false;
            }

            return true;
        }
    }
}
