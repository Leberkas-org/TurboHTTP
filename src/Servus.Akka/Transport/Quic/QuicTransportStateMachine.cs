using System.Net;
using Akka.Actor;

namespace Servus.Akka.Transport.Quic;

public sealed class QuicTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _connectionManager;
    private readonly IActorRef _self;

    private QuicConnectionHandle? _connectionHandle;
    private QuicConnectionLease? _connectionLease;
    private int _connectionGen;
    private ConnectTransport? _pendingConnect;
    private bool _autoReconnect;
    private bool _upstreamFinished;
    private bool _isReconnecting;
    private CancellationTokenSource? _acquireCts;
    private EndPoint? _lastLocalEndPoint;

    private readonly Dictionary<long, StreamContext> _streams = new();
    private readonly Queue<(long StreamId, StreamDirection Direction)> _pendingStreamOpens = new();
    private QuicPumpManager? _pumpManager;

    public QuicTransportStateMachine(
        ITransportOperations ops,
        IActorRef connectionManager,
        IActorRef self)
    {
        _ops = ops;
        _connectionManager = connectionManager;
        _self = self;
    }

    internal void Dispatch(IQuicTransportEvent evt)
    {
        switch (evt)
        {
            case ConnectionLeaseAcquired e:
                OnConnectionLeaseAcquired(e.Lease);
                break;
            case StreamLeaseAcquired e:
                OnStreamLeaseAcquired(e.Handle, e.StreamId);
                break;
            case AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case InboundData e:
                if (e.Gen == _connectionGen)
                {
                    CheckForConnectionMigration();
                    _ops.OnPushInbound(new MultiplexedData(e.Buffer, e.StreamId));
                }
                else
                {
                    e.Buffer.Dispose();
                }

                break;
            case InboundStreamAccepted e:
                OnInboundStreamAccepted(e.Stream, e.StreamId);
                break;
            case InboundComplete e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundComplete(e.Reason, e.StreamId);
                }

                break;
            case InboundPumpFailed e:
                OnInboundComplete(DisconnectReason.Error, e.StreamId);
                break;
            case OutboundWriteDone:
                _ops.OnSignalPullOutbound();
                break;
            case OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
                break;
            case MigrationDetected e:
                _ops.OnPushInbound(new ConnectionMigrationDetected(e.OldEndPoint, e.NewEndPoint));
                break;
            case EarlyDataRejected e:
                _ops.OnPushInbound(new DataRejected(e.Buffer));
                break;
        }
    }

    public void HandlePush(ITransportOutbound item)
    {
        switch (item)
        {
            case ConnectTransport connect:
                HandleConnectTransport(connect);
                break;
            case OpenStream open:
                HandleOpenStream(open.StreamId, open.Direction);
                break;
            case MultiplexedData data:
                HandleMultiplexedData(data);
                break;
            case CloseStream close:
                HandleCloseStream(close.StreamId);
                break;
            case DisconnectTransport:
                CleanupTransport();
                _ops.OnSignalPullOutbound();
                break;
        }
    }

    public void HandleUpstreamFinish()
    {
        _upstreamFinished = true;
        if (_connectionHandle is null)
        {
            _ops.OnCompleteStage();
            return;
        }

        _pumpManager?.StopAll();
        _ops.OnCompleteStage();
    }

    public void HandleDownstreamFinish()
    {
        CleanupTransport();
    }

    public void OnTimer(string? timerKey)
    {
        if (timerKey != ConnectTimerKey || _pendingConnect is null)
        {
            return;
        }

        _pendingConnect = null;

        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Timeout));
        _ops.OnSignalPullOutbound();
    }

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        CleanupTransport();
    }

    private void HandleConnectTransport(ConnectTransport connect)
    {
        if (connect.Options is QuicTransportOptions quicOpts)
        {
            _autoReconnect = quicOpts.AutoReconnect;
        }

        if (_connectionLease is not null)
        {
            _isReconnecting = true;
        }

        CleanupTransport();
        _pendingConnect = connect;
        AcquireConnection(connect);
        _ops.OnSignalPullOutbound();
    }

    private void HandleOpenStream(long streamId, StreamDirection direction)
    {
        if (_connectionHandle is null)
        {
            _pendingStreamOpens.Enqueue((streamId, direction));
            _ops.OnSignalPullOutbound();
            return;
        }

        var ctx = new StreamContext(direction);
        ctx.SetSelf(_self);
        _streams[streamId] = ctx;

        var sid = streamId;
        _connectionHandle.OpenStreamAsync(direction)
            .PipeTo(_self,
                success: result => new StreamLeaseAcquired(new StreamHandle(result.Stream, null), sid),
                failure: ex => new AcquisitionFailed(ex));

        _ops.OnSignalPullOutbound();
    }

    private void HandleMultiplexedData(MultiplexedData data)
    {
        if (_streams.TryGetValue(data.StreamId, out var ctx))
        {
            ctx.Write(data.Buffer);
        }
        else
        {
            data.Buffer.Dispose();
        }

        _ops.OnSignalPullOutbound();
    }

    private void HandleCloseStream(long streamId)
    {
        if (_streams.Remove(streamId, out var ctx))
        {
            ctx.CompleteWrites();
            _ = ctx.DisposeAsync();
        }

        _ops.OnSignalPullOutbound();
    }

    private void OnConnectionLeaseAcquired(QuicConnectionLease lease)
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        _pendingConnect = null;
        _connectionGen++;
        _connectionLease = lease;
        _connectionHandle = lease.Handle;
        _lastLocalEndPoint = _connectionHandle.LocalEndPoint();

        _pumpManager = new QuicPumpManager(_self);
        _pumpManager.StartAcceptLoop(_connectionHandle);

        if (_isReconnecting)
        {
            _isReconnecting = false;
        }

        _ops.OnPushInbound(new TransportConnected(default!));

        while (_pendingStreamOpens.TryDequeue(out var pending))
        {
            HandleOpenStream(pending.StreamId, pending.Direction);
        }
    }

    private void OnStreamLeaseAcquired(StreamHandle handle, long streamId)
    {
        if (!_streams.TryGetValue(streamId, out var ctx))
        {
            _ = handle.DisposeAsync();
            return;
        }

        ctx.AttachHandle(handle);
        _pumpManager?.StartInboundPump(handle, streamId, _connectionGen);

        while (ctx.TryDequeuePendingWrite(out var buffer))
        {
            ctx.Write(buffer!);
        }

        _ops.OnPushInbound(new StreamOpened(streamId, ctx.Direction()));
    }

    private void OnInboundStreamAccepted(Stream stream, long streamId)
    {
        var handle = new StreamHandle(stream, null);
        var ctx = new StreamContext(StreamDirection.Bidirectional);
        ctx.SetSelf(_self);
        ctx.AttachHandle(handle);
        _streams[streamId] = ctx;

        _pumpManager?.StartInboundPump(handle, streamId, _connectionGen);
        _ops.OnPushInbound(new StreamOpened(streamId, StreamDirection.Bidirectional));
    }

    private void OnInboundComplete(DisconnectReason reason, long streamId)
    {
        if (_streams.Remove(streamId, out var ctx))
        {
            _ = ctx.DisposeAsync();
        }

        _ops.OnPushInbound(new StreamClosed(streamId, reason));
    }

    private void OnOutboundWriteFailed(Exception ex)
    {
        HandleConnectionFailure(DisconnectReason.Error);
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return;
        }

        _ops.OnCancelTimer(ConnectTimerKey);

        if (_pendingConnect is null)
        {
            return;
        }

        _pendingConnect = null;
        _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Error));
        _ops.OnSignalPullOutbound();
    }

    private void HandleConnectionFailure(DisconnectReason reason)
    {
        foreach (var (streamId, ctx) in _streams)
        {
            _ops.OnPushInbound(new StreamClosed(streamId, reason));
            _ = ctx.DisposeAsync();
        }

        _streams.Clear();

        if (_autoReconnect && !_upstreamFinished)
        {
            _ops.OnPushInbound(new TransportDisconnected(DisconnectReason.Transient));
            _isReconnecting = true;
            _pumpManager?.StopAll();
            ReturnConnectionToPool(false);
            _connectionHandle = null;
            _connectionLease = null;
            _ops.OnSignalPullOutbound();
            return;
        }

        _ops.OnPushInbound(new TransportDisconnected(reason));
        _pumpManager?.StopAll();
        ReturnConnectionToPool(false);
        _connectionHandle = null;
        _connectionLease = null;

        if (_upstreamFinished)
        {
            _ops.OnCompleteStage();
        }
        else
        {
            _ops.OnSignalPullOutbound();
        }
    }

    private void CheckForConnectionMigration()
    {
        var currentLocal = _connectionHandle?.LocalEndPoint();
        if (currentLocal is null || _lastLocalEndPoint is null)
        {
            return;
        }

        if (!currentLocal.Equals(_lastLocalEndPoint))
        {
            var old = _lastLocalEndPoint;
            _lastLocalEndPoint = currentLocal;
            _self.Tell(new MigrationDetected(old, currentLocal));
        }
    }

    private void AcquireConnection(ConnectTransport connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        if (connect.Options is QuicTransportOptions quicOpts)
        {
            QuicConnectionManagerActor.AcquireAsync(_connectionManager, quicOpts, _acquireCts.Token)
                .PipeTo(_self,
                    success: lease => new ConnectionLeaseAcquired(lease),
                    failure: ex => new AcquisitionFailed(ex));
        }

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    private void ReturnConnectionToPool(bool canReuse)
    {
        if (_connectionLease is null)
        {
            return;
        }

        var lease = _connectionLease;
        _connectionLease = null;

        _connectionManager.Tell(new QuicConnectionManagerActor.Release(lease, canReuse));

        if (!canReuse)
        {
            _ = lease.DisposeAsync();
        }
    }

    private void CleanupTransport()
    {
        _connectionGen++;
        _pumpManager?.StopAll();

        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        foreach (var (_, ctx) in _streams)
        {
            _ = ctx.DisposeAsync();
        }

        _streams.Clear();
        _pendingStreamOpens.Clear();

        ReturnConnectionToPool(false);
        _connectionHandle = null;
        _connectionLease = null;
    }
}

#pragma warning restore CA1416