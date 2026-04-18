using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Tcp;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Encapsulates all QUIC transport state and logic — multi-stream I/O (request, control, encoder),
/// tagged item routing, and connection lifecycle management.
/// Calls back into <see cref="ITransportOperations"/> for Akka-specific operations
/// (Push, Pull, Timer, Complete, Fail).
/// Async events arrive via <see cref="Dispatch"/> after being marshaled through the StageActorRef.
/// <para>
/// Connection acquisition is delegated to <see cref="QuicConnectionManagerActor"/> (via actor tell),
/// mirroring how <see cref="TcpTransportStateMachine"/> uses <see cref="TcpConnectionManagerActor"/>.
/// </para>
/// <para>
/// Per-stream routing is handled by <see cref="QuicStreamRouter"/>;
/// pump lifecycle by <see cref="QuicPumpManager"/>.
/// </para>
/// </summary>
internal sealed class QuicTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _self;
    private readonly IActorRef _quicManagerActor;
    private readonly TurboClientOptions _clientOptions;
    private readonly bool _allowConnectionMigration;

    private readonly QuicStreamRouter _router;
    private readonly QuicPumpManager _pumpManager;

    private int _connectionGen;

    private QuicConnectionLease? _currentConnectionLease;
    private ConnectionHandle? _controlHandle;
    private ConnectionHandle? _encoderHandle;

    private TlsCloseKind _lastCloseKind = TlsCloseKind.CleanClose;
    private bool _needsReconnectSignal;

    /// <summary>Pending control items buffered before control stream is ready.</summary>
    private readonly Queue<NetworkBuffer> _pendingControlItems = new();

    /// <summary>Pending QPACK encoder items buffered before encoder stream is ready.</summary>
    private readonly Queue<NetworkBuffer> _pendingEncoderItems = new();

    /// <summary>All active stream leases for this connection (disposed on Cleanup).</summary>
    private readonly List<ConnectionLease> _activeLeases = [];

    private RequestEndpoint _currentKey;
    private ConnectItem? _pendingConnect;
    private CancellationTokenSource? _acquireCts;

    /// <summary>Tracks the last observed local endpoint for connection migration detection.</summary>
    private System.Net.EndPoint? _lastLocalEndPoint;

    public QuicTransportStateMachine(ITransportOperations ops, IActorRef self, IActorRef quicManagerActor,
        TurboClientOptions clientOptions, bool allowConnectionMigration = true)
    {
        _ops = ops;
        _self = self;
        _quicManagerActor = quicManagerActor;
        _clientOptions = clientOptions;
        _allowConnectionMigration = allowConnectionMigration;
        _router = new QuicStreamRouter(ops, self);
        _pumpManager = new QuicPumpManager(self);
    }

    public void Dispatch(IQuicTransportEvent evt)
    {
        switch (evt)
        {
            case ConnectionLeaseAcquired e:
                OnConnectionLeaseAcquired(e.Lease);
                break;
            case RequestLeaseAcquired e:
                OnRequestLeaseAcquired(e.Lease, e.StreamId);
                break;
            case TypedLeaseAcquired e:
                OnTypedLeaseAcquired(e.Lease, e.StreamType);
                break;
            case AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case InboundData e:
                if (e.Gen == _connectionGen)
                {
                    CheckForConnectionMigration();
                    _ops.OnPushOutput(e.Item);
                }

                break;
            case InboundComplete e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundComplete(e.CloseKind, e.StreamId);
                }

                break;
            case InboundPumpFailed e:
                _ops.Log.Warning("QuicConnectionStage: Inbound pump failed — {0}", e.Error.Message);
                OnInboundComplete(TlsCloseKind.AbruptClose, e.StreamId);
                break;
            case InboundStreamReady e:
                OnInboundStreamReady(e.Stream);
                break;
            case OutboundWriteDone:
                _ops.OnSignalPullInput();
                break;
            case OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
                break;
            case EarlyDataRejected e:
                OnEarlyDataRejected(e.Buffer);
                break;
            case ConnectionMigrated e:
                OnConnectionMigrated(e.OldLocalEndPoint, e.NewLocalEndPoint);
                break;
        }
    }

    public void HandlePush(IOutputItem item)
    {
        var streamId = item switch
        {
            Http3NetworkBuffer t => t.StreamId,
            Http3EndOfRequestItem e => e.StreamId,
            _ => -1L
        };

        var result = _router.EnsureStreamContext(item, streamId,
            hasConnection: _currentConnectionLease is not null && _controlHandle is not null);

        switch (result)
        {
            case QuicStreamRouter.StreamContextResult.OpenNewStream:
                OpenNewRequestStream(streamId);
                break;
            case QuicStreamRouter.StreamContextResult.NeedsConnection:
                // Only start a new connection if one isn't already being acquired or established.
                // The stream context was already created — its items will be buffered in pending writes
                // and the stream will be opened once the connection is fully ready.
                if (_pendingConnect is null && _currentConnectionLease is null)
                {
                    AutoConnect(item.Key);
                }

                break;
        }

        switch (item)
        {
            case ConnectItem connect:
                HandleConnectItem(connect);
                break;

            case Http3NetworkBuffer tagged when tagged.StreamType != Http3StreamType.None:
                _router.RouteTaggedItem(tagged, _controlHandle, _pendingControlItems,
                    _encoderHandle, _pendingEncoderItems);
                break;

            case NetworkBuffer dataItem:
                _router.RouteUntaggedData(dataItem);
                break;

            case Http3EndOfRequestItem endItem:
                _router.HandleEndOfRequest(endItem);
                break;

            case ConnectionReuseItem:
            case StreamAcquireItem:
            case MaxConcurrentStreamsItem:
                // QUIC manages these internally — no-op
                _ops.OnSignalPullInput();
                break;
        }
    }

    public void HandleUpstreamFinish()
    {
        _pumpManager.StopAll();
        _ops.OnCompleteStage();
    }

    public void HandleDownstreamFinish()
    {
        CleanupTransport();
    }

    public void OnTimer(string? timerKey)
    {
        if (timerKey != ConnectTimerKey)
        {
            return;
        }

        if (_pendingConnect is null)
        {
            return;
        }

        _ops.Log.Warning("QuicConnectionStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Value.Key.Host, _pendingConnect.Value.Key.Port);

        var signal = new QuicCloseItem(QuicCloseKind.AcquisitionFailed) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;
        _needsReconnectSignal = true;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _router.DisposePendingWrites();
        CleanupTransport();
    }

    private void HandleConnectItem(ConnectItem connect)
    {
        _ops.Log.Debug("QuicConnectionStage: ConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);

        CleanupTransport();
        _pendingConnect = connect;

        if (connect.Options is not QuicOptions quicOptions)
        {
            _self.Tell(new AcquisitionFailed(new InvalidOperationException(
                "QuicConnectionStage received a non-QuicOptions ConnectItem.")));
            return;
        }

        AcquireQuicConnection(quicOptions, connect);
    }

    private void AutoConnect(RequestEndpoint endpoint)
    {
        _ops.Log.Debug("QuicConnectionStage: AutoConnect for {0}:{1}", endpoint.Host, endpoint.Port);

        var options = OptionsFactory.Build(endpoint, _clientOptions);
        _pendingConnect = new ConnectItem(options) { Key = endpoint };

        if (options is not QuicOptions quicOptions)
        {
            _self.Tell(new AcquisitionFailed(new InvalidOperationException(
                "QuicConnectionStage: AutoConnect produced non-QuicOptions for endpoint.")));
            return;
        }

        AcquireQuicConnection(quicOptions, _pendingConnect.Value);
    }

    private void OnConnectionLeaseAcquired(QuicConnectionLease lease)
    {
        _currentConnectionLease = lease;

        var streamId = _router.DequeueNextPendingStreamId();
        if (streamId < 0)
        {
            return;
        }

        _ = lease.Handle.OpenStreamAsLeaseAsync(Http3StreamType.Request)
            .PipeTo(_self,
                success: streamLease => new RequestLeaseAcquired(streamLease, streamId),
                failure: ex => new AcquisitionFailed(ex.GetBaseException()));
    }

    private void OnRequestLeaseAcquired(ConnectionLease lease, long streamId)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _pendingConnect = null;

        _activeLeases.Add(lease);
        _currentKey = lease.Key;
        _lastLocalEndPoint = _currentConnectionLease?.Handle.LocalEndPoint;

        var ctx = _router.GetOrCreateContext(streamId);
        ctx.Handle = lease.Handle;
        _pumpManager.StartInboundPump(lease.Handle, Http3StreamType.Request, _currentKey, _connectionGen, streamId);

        if (_controlHandle is not null)
        {
            _router.FlushPendingWrites(ctx);
            _ops.OnSignalPullInput();
        }
        else
        {
            OpenTypedStream(Http3StreamType.Control);
            OpenTypedStream(Http3StreamType.QpackEncoder);
            _pumpManager.StartInboundAcceptLoop(_currentConnectionLease!.Handle);
        }
    }

    private void OnTypedLeaseAcquired(ConnectionLease lease, Http3StreamType streamType)
    {
        _activeLeases.Add(lease);

        switch (streamType)
        {
            case Http3StreamType.Control:
                _controlHandle = lease.Handle;
                FlushPendingQuicItems(_pendingControlItems, lease.Handle);
                _router.FlushAllReadyStreams();
                OpenPendingStreams();
                if (_needsReconnectSignal)
                {
                    _needsReconnectSignal = false;
                    _ops.OnPushOutput(new ConnectedSignalItem { Key = _currentKey });
                }

                _ops.OnSignalPullInput();
                break;

            case Http3StreamType.QpackEncoder:
                _encoderHandle = lease.Handle;
                FlushPendingQuicItems(_pendingEncoderItems, lease.Handle);
                break;
        }
    }

    private void OnConnectionMigrated(System.Net.EndPoint? oldEndPoint, System.Net.EndPoint? newEndPoint)
    {
        if (_allowConnectionMigration)
        {
            _ops.Log.Info(
                "QuicConnectionStage: Connection migration detected ({0} → {1}) — migration allowed, continuing transparently.",
                oldEndPoint, newEndPoint);
            _lastLocalEndPoint = newEndPoint;
            return;
        }

        _ops.Log.Warning(
            "QuicConnectionStage: Connection migration detected ({0} → {1}) — migration disallowed, closing connection for reconnect.",
            oldEndPoint, newEndPoint);

        var signal = new QuicCloseItem(QuicCloseKind.MigrationDisallowed) { Key = _currentKey };
        _needsReconnectSignal = true;
        _ops.OnPushOutput(signal);

        _router.Clear();
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void CheckForConnectionMigration()
    {
        var currentLocal = _currentConnectionLease?.Handle.LocalEndPoint;
        if (currentLocal is null || _lastLocalEndPoint is null)
        {
            return;
        }

        if (!currentLocal.Equals(_lastLocalEndPoint))
        {
            var old = _lastLocalEndPoint;
            _lastLocalEndPoint = currentLocal;
            _self.Tell(new ConnectionMigrated(old, currentLocal));
        }
    }

    private void OnEarlyDataRejected(NetworkBuffer buffer)
    {
        _ops.Log.Warning(
            "QuicConnectionStage: 0-RTT early data rejected — re-queuing buffer for retry after full handshake.");
        _router.RequeueEarlyData(buffer);
    }

    private void OnOutboundWriteFailed(Exception ex)
    {
        _ops.Log.Warning("QuicConnectionStage: Outbound write failed — {0}", ex.Message);

        var signal = new QuicCloseItem(QuicCloseKind.WriteFailed) { Key = _currentKey };
        _needsReconnectSignal = true;
        _ops.OnPushOutput(signal);

        _router.Clear();
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _ops.Log.Warning("QuicConnectionStage: Connection acquisition failed — {0}", ex.Message);

        if (_pendingConnect is null)
        {
            return;
        }

        var signal = new QuicCloseItem(QuicCloseKind.AcquisitionFailed) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;
        _needsReconnectSignal = true;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    private void OnInboundComplete(TlsCloseKind closeKind, long streamId = -1)
    {
        _lastCloseKind = closeKind;

        if (closeKind == TlsCloseKind.CleanClose)
        {
            _ops.OnPushOutput(new QuicCloseItem(QuicCloseKind.RequestStreamComplete, streamId) { Key = _currentKey });
            _router.RemoveStream(streamId);
        }
        else
        {
            _needsReconnectSignal = true;
            _ops.OnPushOutput(new QuicCloseItem(QuicCloseKind.ConnectionFailure) { Key = _currentKey });
            _router.Clear();
            _controlHandle = null;
            _encoderHandle = null;
        }
    }

    private void OnInboundStreamReady(QuicConnectionHandle.InboundStream inbound)
    {
        _activeLeases.Add(inbound.Lease);
        _pumpManager.StartInboundPump(inbound.Lease.Handle, inbound.StreamType, _currentKey, _connectionGen);
    }

    private void AcquireQuicConnection(QuicOptions options, ConnectItem connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        var acquireTask = QuicConnectionManagerActor.AcquireAsync(
            _quicManagerActor, options, connect.Key, _acquireCts.Token);

        acquireTask.PipeTo(_self,
            success: connLease => new ConnectionLeaseAcquired(connLease),
            failure: ex => new AcquisitionFailed(ex.GetBaseException()));

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    private void OpenPendingStreams()
    {
        var pending = _router.DrainPendingStreamIds();
        foreach (var id in pending)
        {
            OpenNewRequestStream(id);
        }
    }

    private void OpenNewRequestStream(long streamId)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _ = _currentConnectionLease.Handle.OpenStreamAsLeaseAsync(Http3StreamType.Request)
            .PipeTo(_self,
                success: streamLease => new RequestLeaseAcquired(streamLease, streamId),
                failure: ex => new AcquisitionFailed(ex.GetBaseException()));
    }

    private void OpenTypedStream(Http3StreamType streamType)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        _ = _currentConnectionLease.Handle.OpenStreamAsLeaseAsync(streamType)
            .PipeTo(_self,
                success: lease => new TypedLeaseAcquired(lease, streamType),
                failure: ex =>
                {
                    _ops.Log.Warning("QuicConnectionStage: Failed to open {0} stream — {1}",
                        streamType, ex.GetBaseException().Message);
                    return new AcquisitionFailed(ex.GetBaseException());
                });
    }

    private void ReturnConnectionToPool(bool canReuse)
    {
        if (_currentConnectionLease is null)
        {
            return;
        }

        var lease = _currentConnectionLease;
        _currentConnectionLease = null;
        _quicManagerActor.Tell(new QuicConnectionManagerActor.Release(lease, canReuse));
    }

    private void CleanupTransport()
    {
        _connectionGen++;

        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        _pumpManager.StopAll();

        foreach (var lease in _activeLeases)
        {
            lease.Dispose();
        }

        _activeLeases.Clear();

        ReturnConnectionToPool(_lastCloseKind == TlsCloseKind.CleanClose);
        _lastCloseKind = TlsCloseKind.CleanClose;

        _router.Clear();
        _controlHandle = null;
        _encoderHandle = null;
    }

    private void FlushPendingQuicItems(
        Queue<NetworkBuffer> pending,
        ConnectionHandle handle)
    {
        while (pending.TryDequeue(out var item))
        {
            _ = handle.OutboundWriter.WriteAsync(item)
                .PipeTo(_self,
                    success: () => new OutboundWriteDone(),
                    failure: ex => new OutboundWriteFailed(ex.GetBaseException()));
        }

        _ops.OnSignalPullInput();
    }
}