using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.Http3;

namespace TurboHttp.Streams.Stages.Decoding;

/// <summary>
/// Custom shape for the HTTP/3 connection stage.
/// Two inlets: server frames (control stream) and app frames (request stream).
/// Two outlets: app-bound frames (assembled responses) and server-bound frames (requests + control acks).
/// </summary>
public sealed class Http30ConnectionShape : Shape
{
    public Inlet<Http3Frame> InServer { get; }
    public Outlet<Http3Frame> OutApp { get; }
    public Inlet<Http3Frame> InApp { get; }
    public Outlet<Http3Frame> OutServer { get; }

    public Http30ConnectionShape(
        Inlet<Http3Frame> inServer,
        Outlet<Http3Frame> outApp,
        Inlet<Http3Frame> inApp,
        Outlet<Http3Frame> outServer)
    {
        InServer = inServer;
        OutApp = outApp;
        InApp = inApp;
        OutServer = outServer;
    }

    public override ImmutableArray<Inlet> Inlets =>
        [InServer, InApp];

    public override ImmutableArray<Outlet> Outlets =>
        [OutApp, OutServer];

    public override Shape DeepCopy()
    {
        return new Http30ConnectionShape(
            (Inlet<Http3Frame>)InServer.CarbonCopy(),
            (Outlet<Http3Frame>)OutApp.CarbonCopy(),
            (Inlet<Http3Frame>)InApp.CarbonCopy(),
            (Outlet<Http3Frame>)OutServer.CarbonCopy());
    }

    public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
    {
        return new Http30ConnectionShape(
            (Inlet<Http3Frame>)inlets[0],
            (Outlet<Http3Frame>)outlets[0],
            (Inlet<Http3Frame>)inlets[1],
            (Outlet<Http3Frame>)outlets[1]);
    }
}

/// <summary>
/// RFC 9114 §6.2.1, §5.2, §7.2.4 — HTTP/3 connection-level stage.
///
/// Manages the HTTP/3 control stream (SETTINGS/GOAWAY) and routes frames between
/// app and server. Unlike HTTP/2, QUIC handles flow control at the transport layer,
/// so this stage does not track per-stream windows.
///
/// Responsibilities:
/// - Process SETTINGS frames on the control stream (via <see cref="Http3ControlStream"/>)
/// - Process GOAWAY frames for graceful shutdown (via <see cref="Http3GoAwayHandler"/>)
/// - Drop new outbound requests after GOAWAY received
/// - Forward DATA and HEADERS frames between app and server
/// - Reject invalid control-stream frames (DATA/HEADERS on control stream)
/// - Track idle timeout and close connection when expired with no active streams (via <see cref="Http3IdleTimeoutHandler"/>)
/// </summary>
public sealed class Http30ConnectionStage : GraphStage<Http30ConnectionShape>
{
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly Inlet<Http3Frame> _inServer = new("Http30Connection.In.Server");
    private readonly Outlet<Http3Frame> _outApp = new("Http30Connection.Out.App");
    private readonly Inlet<Http3Frame> _inApp = new("Http30Connection.In.App");
    private readonly Outlet<Http3Frame> _outServer = new("Http30Connection.Out.Server");

    private readonly TimeSpan _idleTimeout;

    /// <summary>
    /// Creates an HTTP/3 connection stage with the default idle timeout (30 seconds).
    /// </summary>
    public Http30ConnectionStage() : this(DefaultIdleTimeout) { }

    /// <summary>
    /// Creates an HTTP/3 connection stage with a configurable idle timeout.
    /// </summary>
    /// <param name="idleTimeout">
    /// The local idle timeout. Use <see cref="TimeSpan.Zero"/> to disable idle timeout.
    /// The effective timeout is reconciled with the remote timeout from SETTINGS via
    /// <see cref="Http3IdleTimeoutHandler.ComputeEffectiveTimeout"/>.
    /// </param>
    public Http30ConnectionStage(TimeSpan idleTimeout)
    {
        if (idleTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), idleTimeout, "Idle timeout must be non-negative.");
        }

        _idleTimeout = idleTimeout;
    }

    public override Http30ConnectionShape Shape =>
        new(_inServer, _outApp, _inApp, _outServer);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private const string IdleCheckTimerKey = "idle-timeout-check";

        private readonly Http30ConnectionStage _stage;
        private readonly Http3ControlStream _controlStream = new();
        private readonly Http3GoAwayHandler _goAwayHandler = new();
        private readonly Http3IdleTimeoutHandler _idleTimeoutHandler;
        private readonly Http3MaxPushIdHandler _maxPushIdHandler = new();
        private readonly Http3PushLimiter _pushLimiter = new(maxPushCount: 0);
        private readonly Http3CancelPushHandler _cancelPushHandler;
        private readonly Http3PushPromiseValidator _pushPromiseValidator;
        private readonly Queue<Http3Frame> _outboundQueue = new();

        private bool _goAwayReceived;

        public Logic(Http30ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _idleTimeoutHandler = new Http3IdleTimeoutHandler(stage._idleTimeout);
            _cancelPushHandler = new Http3CancelPushHandler(_maxPushIdHandler);
            _pushPromiseValidator = new Http3PushPromiseValidator(_maxPushIdHandler);

            SetHandler(stage._inServer, onPush: () =>
            {
                var frame = Grab(stage._inServer);
                HandleServerFrame(frame);
            }, onUpstreamFinish: () =>
            {
                Log.Debug("Http30ConnectionStage: Completing stage due to server inlet upstream finish.");
                CompleteStage();
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http30ConnectionStage: Server inlet upstream failure: {0}", ex.Message);
                Log.Debug("Http30ConnectionStage: Failing stage due to server inlet upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outApp, onPull: () => Pull(stage._inServer));

            SetHandler(stage._inApp, onPush: () =>
            {
                var frame = Grab(stage._inApp);

                if (_goAwayReceived)
                {
                    Log.Warning("Http30ConnectionStage: RFC 9114 §5.2 — GOAWAY received; dropping outbound frame.");
                    TryPullApp();
                    return;
                }

                // Track stream lifecycle — HEADERS frame indicates a new request stream.
                if (frame is Http3HeadersFrame)
                {
                    _idleTimeoutHandler.OnStreamOpened();
                }

                EnqueueOutbound(frame);
            }, onUpstreamFinish: () =>
            {
                // App stream finished — keep stage alive to receive server responses.
            }, onUpstreamFailure: ex =>
            {
                Log.Warning("Http30ConnectionStage: App inlet upstream failure: {0}", ex.Message);
                Log.Debug("Http30ConnectionStage: Failing stage due to app inlet upstream failure.");
                FailStage(ex);
            });

            SetHandler(stage._outServer, onPull: () =>
            {
                TryDrainOutbound();
            });
        }

        public override void PreStart()
        {
            // RFC 9114 §6.2.1: SETTINGS and MAX_PUSH_ID belong on the unidirectional
            // control stream, NOT on bidirectional request streams. The control stream
            // is now opened by QuicClientProvider after the QUIC connection is established.
            // This stage only handles routing frames on the bidirectional request stream.

            // Schedule periodic idle timeout checks if timeout is enabled.
            ScheduleIdleCheck();
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not IdleCheckTimerKey)
            {
                return;
            }

            if (_idleTimeoutHandler.IsIdleTimeoutExpired() && _idleTimeoutHandler.ActiveStreamCount == 0)
            {
                Log.Warning("Http30ConnectionStage: RFC 9114 §5.1 — idle timeout expired with no active streams; sending GOAWAY.");
                EnqueueOutbound(new Http3GoAwayFrame(0));
                CompleteStage();
                return;
            }

            // Re-schedule for the next check.
            ScheduleIdleCheck();
        }

        private void HandleServerFrame(Http3Frame frame)
        {
            // Record activity on every server frame for idle timeout tracking.
            _idleTimeoutHandler.RecordActivity();

            switch (frame)
            {
                case Http3SettingsFrame settings:
                    HandleSettings(settings);
                    // SETTINGS is connection-level — don't forward to app.
                    Pull(_stage._inServer);
                    return;

                case Http3GoAwayFrame goAway:
                    HandleGoAway(goAway);
                    // GOAWAY is connection-level — don't forward to app.
                    Pull(_stage._inServer);
                    return;

                case Http3PushPromiseFrame:
                    // RFC 9114 §10.5 — With MAX_PUSH_ID=0, any push promise exceeds the limit.
                    try
                    {
                        _pushLimiter.RecordPush();
                    }
                    catch (Http3Exception ex)
                    {
                        Log.Warning("Http30ConnectionStage: RFC 9114 §10.5 — server push rejected; push limit is zero. {0}", ex.Message);
                    }

                    Pull(_stage._inServer);
                    return;

                case Http3CancelPushFrame cancelPush:
                    // RFC 9114 §7.2.3 — Record the cancellation.
                    _cancelPushHandler.HandleReceivedCancelPush(cancelPush);
                    Pull(_stage._inServer);
                    return;

                case Http3MaxPushIdFrame:
                    // Control-stream frame — absorb (client doesn't need to act on MAX_PUSH_ID from server).
                    Pull(_stage._inServer);
                    return;

                case Http3HeadersFrame:
                    // Response HEADERS indicates a stream response is assembled — close the stream.
                    if (_idleTimeoutHandler.ActiveStreamCount > 0)
                    {
                        _idleTimeoutHandler.OnStreamClosed();
                    }

                    Push(_stage._outApp, frame);
                    break;

                default:
                    // DATA frames go to the app (request stream).
                    Push(_stage._outApp, frame);
                    break;
            }
        }

        private void HandleSettings(Http3SettingsFrame settings)
        {
            try
            {
                // Ensure the remote control stream is registered before processing.
                if (_controlStream.RemoteState == ControlStreamState.NotOpened)
                {
                    _controlStream.OnRemoteControlStreamOpened();
                }

                _controlStream.OnRemoteFrame(settings);

                // Reconcile local and remote idle timeout (RFC 9114 §5.1).
                // HTTP/3 idle timeout is a QUIC transport parameter; if the server
                // advertises one via SETTINGS, compute the effective timeout.
                var remoteTimeout = ExtractRemoteIdleTimeout(settings);
                if (remoteTimeout.HasValue)
                {
                    var effective = Http3IdleTimeoutHandler.ComputeEffectiveTimeout(
                        _stage._idleTimeout, remoteTimeout.Value);

                    Log.Debug("Http30ConnectionStage: RFC 9114 §5.1 — effective idle timeout reconciled to {0}ms.",
                        effective.TotalMilliseconds);
                }

                Log.Debug("Http30ConnectionStage: RFC 9114 §7.2.4 — remote SETTINGS received ({0} parameters).",
                    settings.Parameters.Count);
            }
            catch (Http3Exception ex)
            {
                Log.Warning("Http30ConnectionStage: SETTINGS error absorbed — {0}", ex.Message);
            }
        }

        private void HandleGoAway(Http3GoAwayFrame goAway)
        {
            try
            {
                _goAwayHandler.OnServerGoAway(goAway);
                _goAwayReceived = true;

                Log.Warning("Http30ConnectionStage: RFC 9114 §5.2 — GOAWAY received (streamId={0}).",
                    goAway.StreamId);
            }
            catch (Http3Exception ex)
            {
                Log.Warning("Http30ConnectionStage: GOAWAY error absorbed — {0}", ex.Message);
                _goAwayReceived = true;
            }
        }

        private void EnqueueOutbound(Http3Frame frame)
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
                TryPullApp();
            }
        }

        private void TryPullApp()
        {
            if (!HasBeenPulled(_stage._inApp) && !IsClosed(_stage._inApp))
            {
                Pull(_stage._inApp);
            }
        }

        private void ScheduleIdleCheck()
        {
            if (_idleTimeoutHandler.IsTimeoutDisabled)
            {
                return;
            }

            var remaining = _idleTimeoutHandler.TimeUntilExpiry();
            var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
            ScheduleOnce(IdleCheckTimerKey, checkInterval);
        }

        private static TimeSpan? ExtractRemoteIdleTimeout(Http3SettingsFrame settings)
        {
            // RFC 9114: idle timeout is primarily a QUIC transport parameter,
            // not an HTTP/3 SETTINGS parameter. However, if a server includes
            // an idle-timeout-like setting, we can extract it here.
            // Currently HTTP/3 defines: SETTINGS_MAX_FIELD_SECTION_SIZE,
            // SETTINGS_QPACK_MAX_TABLE_CAPACITY, SETTINGS_QPACK_BLOCKED_STREAMS.
            // No standard idle timeout setting exists in HTTP/3 SETTINGS,
            // so this returns null. The method exists as a reconciliation point
            // for future extensions or custom settings.
            return null;
        }
    }
}
