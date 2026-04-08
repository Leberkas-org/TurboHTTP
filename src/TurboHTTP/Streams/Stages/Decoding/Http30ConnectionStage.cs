using System.Collections.Immutable;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Streams.Stages.Decoding;

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
/// - Process SETTINGS frames on the control stream
/// - Process GOAWAY frames for graceful shutdown
/// - Drop new outbound requests after GOAWAY received
/// - Forward DATA and HEADERS frames between app and server
/// - Reject invalid control-stream frames (DATA/HEADERS on control stream)
/// - Track idle timeout and close connection when expired with no active streams
///
/// All connection state is encapsulated in <see cref="ConnectionState"/>,
/// analogous to HTTP/2's StreamState pattern.
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
    /// <see cref="ConnectionState.ComputeEffectiveTimeout"/>.
    /// </param>
    public Http30ConnectionStage(TimeSpan idleTimeout)
    {
        if (idleTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), idleTimeout, "Idle timeout must be non-negative.");
        }

        _idleTimeout = idleTimeout;
    }

    /// <summary>
    /// Computes the effective idle timeout as the minimum of the local and remote values.
    /// A value of zero means no preference (RFC 9114 §5.1).
    /// </summary>
    internal static TimeSpan ComputeEffectiveTimeout(TimeSpan localTimeout, TimeSpan remoteTimeout)
        => ConnectionState.ComputeEffectiveTimeout(localTimeout, remoteTimeout);

    public override Http30ConnectionShape Shape =>
        new(_inServer, _outApp, _inApp, _outServer);

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    /// <summary>
    /// Encapsulates all HTTP/3 connection-level state in a single class,
    /// replacing the 7 external handler objects (Http3GoAwayHandler,
    /// Http3ControlStream, Http3IdleTimeoutHandler, Http3MaxPushIdHandler,
    /// Http3PushLimiter, Http3CancelPushHandler, Http3PushPromiseValidator).
    /// Analogous to HTTP/2's StreamState pattern.
    /// </summary>
    private sealed class ConnectionState
    {
        private readonly TimeSpan _idleTimeout;

        // --- GoAway state (replaces Http3GoAwayHandler) ---

        /// <summary>Whether a GOAWAY frame has been received from the server.</summary>
        public bool GoAwayReceived { get; set; }

        /// <summary>
        /// The last stream ID from the most recent server GOAWAY, or -1 if none received.
        /// Streams with ID &gt;= this value were NOT processed by the server.
        /// </summary>
        public long LastGoAwayStreamId { get; private set; } = -1;

        // --- ControlStream state (replaces Http3ControlStream) ---

        /// <summary>Whether remote SETTINGS have been received on the control stream.</summary>
        public bool RemoteSettingsReceived { get; private set; }

        /// <summary>The SETTINGS received from the server, or null if not yet received.</summary>
        public Http3Settings? RemoteSettings { get; private set; }

        /// <summary>The effective MAX_FIELD_SECTION_SIZE from the remote peer, or null if unlimited.</summary>
        public long? RemoteMaxFieldSectionSize => RemoteSettings?.MaxFieldSectionSize;

        // --- IdleTimeout state (replaces Http3IdleTimeoutHandler) ---

        private DateTime _lastActivity;
        private int _activeStreamCount;

        /// <summary>The number of currently active streams on this connection.</summary>
        public int ActiveStreamCount => _activeStreamCount;

        /// <summary>Whether idle timeout is disabled (timeout is <see cref="TimeSpan.Zero"/>).</summary>
        public bool IsTimeoutDisabled => _idleTimeout == TimeSpan.Zero;

        // --- Push state (replaces Http3MaxPushIdHandler, Http3PushLimiter, Http3CancelPushHandler, Http3PushPromiseValidator) ---

        /// <summary>The current MAX_PUSH_ID value, or 0 (no pushes accepted by default).</summary>
        public long MaxPushId { get; set; }

        private readonly HashSet<long> _cancelledPushIds = new();
        private int _pushCount;
        private readonly int _maxPushCount;

        public ConnectionState(TimeSpan idleTimeout, int maxPushCount = 0)
        {
            _idleTimeout = idleTimeout;
            _maxPushCount = maxPushCount;
            _lastActivity = DateTime.UtcNow;
        }

        // --- GoAway methods ---

        /// <summary>
        /// Processes a GOAWAY frame received from the server on the control stream.
        /// RFC 9114 §5.2: The stream ID MUST NOT increase on subsequent GOAWAYs,
        /// and MUST be a valid client-initiated bidirectional stream ID (divisible by 4).
        /// </summary>
        public void OnServerGoAway(Http3GoAwayFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            var streamId = frame.StreamId;

            if (streamId % 4 != 0)
            {
                throw new Http3Exception(
                    Http3ErrorCode.IdError,
                    $"Server GOAWAY stream ID {streamId} is not a valid client-initiated bidirectional stream ID (must be divisible by 4, RFC 9114 §5.2).");
            }

            if (LastGoAwayStreamId >= 0 && streamId > LastGoAwayStreamId)
            {
                throw new Http3Exception(
                    Http3ErrorCode.IdError,
                    $"Server GOAWAY stream ID {streamId} must not increase beyond previous value {LastGoAwayStreamId} (RFC 9114 §5.2).");
            }

            LastGoAwayStreamId = streamId;
            GoAwayReceived = true;
        }

        // --- ControlStream methods ---

        /// <summary>
        /// Processes a SETTINGS frame received from the server.
        /// The first SETTINGS marks the control stream as active.
        /// Duplicate SETTINGS is a connection error (RFC 9114 §7.2.4).
        /// DATA/HEADERS on the control stream are connection errors.
        /// </summary>
        public void OnRemoteSettings(Http3SettingsFrame settingsFrame)
        {
            if (RemoteSettingsReceived)
            {
                throw new Http3Exception(
                    Http3ErrorCode.FrameUnexpected,
                    "A second SETTINGS frame on the control stream is a connection error (RFC 9114 §7.2.4).");
            }

            var settings = new Http3Settings();
            foreach (var (id, val) in settingsFrame.Parameters)
            {
                settings.Set(id, val);
            }

            RemoteSettings = settings;
            RemoteSettingsReceived = true;
        }

        // --- IdleTimeout methods ---

        /// <summary>Records activity on this connection, resetting the idle timer.</summary>
        public void RecordActivity()
        {
            _lastActivity = DateTime.UtcNow;
        }

        /// <summary>Records that a new stream has been opened. Resets the idle timer.</summary>
        public void OnStreamOpened()
        {
            _activeStreamCount++;
            RecordActivity();
        }

        /// <summary>Records that a stream has been closed. Resets the idle timer.</summary>
        public void OnStreamClosed()
        {
            if (_activeStreamCount > 0)
            {
                _activeStreamCount--;
            }

            RecordActivity();
        }

        /// <summary>
        /// Determines whether the connection has been idle for longer than the configured timeout.
        /// </summary>
        public bool IsIdleTimeoutExpired()
        {
            if (IsTimeoutDisabled)
            {
                return false;
            }

            return (DateTime.UtcNow - _lastActivity) >= _idleTimeout;
        }

        /// <summary>Returns the time remaining before the idle timeout expires.</summary>
        public TimeSpan TimeUntilExpiry()
        {
            if (IsTimeoutDisabled)
            {
                return TimeSpan.MaxValue;
            }

            var remaining = _idleTimeout - (DateTime.UtcNow - _lastActivity);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        /// <summary>
        /// Computes the effective idle timeout as the minimum of the local and remote values.
        /// A value of zero means no preference (RFC 9114 §5.1).
        /// </summary>
        public static TimeSpan ComputeEffectiveTimeout(TimeSpan localTimeout, TimeSpan remoteTimeout)
        {
            if (localTimeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(localTimeout), localTimeout, "Timeout must be non-negative.");
            }

            if (remoteTimeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(remoteTimeout), remoteTimeout, "Timeout must be non-negative.");
            }

            if (localTimeout == TimeSpan.Zero)
            {
                return remoteTimeout;
            }

            if (remoteTimeout == TimeSpan.Zero)
            {
                return localTimeout;
            }

            return localTimeout < remoteTimeout ? localTimeout : remoteTimeout;
        }

        // --- Push methods ---

        /// <summary>
        /// Records an incoming push promise and enforces the DoS limit (RFC 9114 §10.5).
        /// </summary>
        public void RecordPush()
        {
            if (_pushCount >= _maxPushCount)
            {
                throw new Http3Exception(
                    Http3ErrorCode.ExcessiveLoad,
                    $"Server exceeded push limit of {_maxPushCount} push promises (RFC 9114 §10.5).");
            }

            _pushCount++;
        }

        /// <summary>
        /// Records a CANCEL_PUSH frame received from the server (RFC 9114 §7.2.3).
        /// A CANCEL_PUSH for an unknown push ID is not an error.
        /// </summary>
        public void OnReceivedCancelPush(Http3CancelPushFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            _cancelledPushIds.Add(frame.PushId);
        }

        /// <summary>Returns whether the given push ID has been cancelled.</summary>
        public bool IsPushCancelled(long pushId) => _cancelledPushIds.Contains(pushId);
    }

    private sealed class Logic : TimerGraphStageLogic
    {
        private const string IdleCheckTimerKey = "idle-timeout-check";

        private readonly Http30ConnectionStage _stage;
        private readonly ConnectionState _state;
        private readonly Queue<Http3Frame> _outboundQueue = new();

        public Logic(Http30ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _state = new ConnectionState(stage._idleTimeout);

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

                if (_state.GoAwayReceived)
                {
                    Log.Warning("Http30ConnectionStage: RFC 9114 §5.2 — GOAWAY received; dropping outbound frame.");
                    TryPullApp();
                    return;
                }

                // Track stream lifecycle — HEADERS frame indicates a new request stream.
                if (frame is Http3HeadersFrame)
                {
                    _state.OnStreamOpened();
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

            if (_state.IsIdleTimeoutExpired() && _state.ActiveStreamCount == 0)
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
            _state.RecordActivity();

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
                        _state.RecordPush();
                    }
                    catch (Http3Exception ex)
                    {
                        Log.Warning("Http30ConnectionStage: RFC 9114 §10.5 — server push rejected; push limit is zero. {0}", ex.Message);
                    }

                    Pull(_stage._inServer);
                    return;

                case Http3CancelPushFrame cancelPush:
                    // RFC 9114 §7.2.3 — Record the cancellation.
                    _state.OnReceivedCancelPush(cancelPush);
                    Pull(_stage._inServer);
                    return;

                case Http3MaxPushIdFrame:
                    // Control-stream frame — absorb (client doesn't need to act on MAX_PUSH_ID from server).
                    Pull(_stage._inServer);
                    return;

                case Http3HeadersFrame:
                    // Response HEADERS indicates a stream response is assembled — close the stream.
                    if (_state.ActiveStreamCount > 0)
                    {
                        _state.OnStreamClosed();
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
                _state.OnRemoteSettings(settings);

                // Reconcile local and remote idle timeout (RFC 9114 §5.1).
                // HTTP/3 idle timeout is a QUIC transport parameter; if the server
                // advertises one via SETTINGS, compute the effective timeout.
                var remoteTimeout = ExtractRemoteIdleTimeout(settings);
                if (remoteTimeout.HasValue)
                {
                    var effective = ConnectionState.ComputeEffectiveTimeout(
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
                _state.OnServerGoAway(goAway);

                Log.Warning("Http30ConnectionStage: RFC 9114 §5.2 — GOAWAY received (streamId={0}).",
                    goAway.StreamId);
            }
            catch (Http3Exception ex)
            {
                Log.Warning("Http30ConnectionStage: GOAWAY error absorbed — {0}", ex.Message);
                _state.GoAwayReceived = true;
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
            if (_state.IsTimeoutDisabled)
            {
                return;
            }

            var remaining = _state.TimeUntilExpiry();
            var checkInterval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
            ScheduleOnce(IdleCheckTimerKey, checkInterval);
        }

        private static TimeSpan? ExtractRemoteIdleTimeout(Http3SettingsFrame settings)
        {
            // RFC 9114: idle timeout is primarily a QUIC transport parameter,
            // not an HTTP/3 SETTINGS parameter. Currently HTTP/3 defines:
            // SETTINGS_MAX_FIELD_SECTION_SIZE, SETTINGS_QPACK_MAX_TABLE_CAPACITY,
            // SETTINGS_QPACK_BLOCKED_STREAMS. No standard idle timeout setting
            // exists in HTTP/3 SETTINGS, so this returns null. The method exists
            // as a reconciliation point for future extensions or custom settings.
            return null;
        }
    }
}
