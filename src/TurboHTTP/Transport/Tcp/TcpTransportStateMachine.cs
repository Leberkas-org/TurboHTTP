using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Encapsulates all TCP/TLS transport state and logic — connection acquisition, inbound pumping,
/// outbound writing, reconnection, and lifecycle management.
/// Calls back into <see cref="ITransportOperations"/> for Akka-specific operations
/// (Push, Pull, Timer, Complete, Fail).
/// Async events arrive via <see cref="Dispatch"/> after being marshaled through the StageActorRef.
/// </summary>
internal sealed class TcpTransportStateMachine
{
    private const string ConnectTimerKey = "connect-timeout";

    private readonly ITransportOperations _ops;
    private readonly IActorRef _connectionManager;
    private readonly TurboClientOptions _clientOptions;
    private readonly IActorRef _self;

    private ConnectionHandle? _handle;
    private ConnectionLease? _currentLease;
    private bool _leaseReturned;
    private int _connectionGen;
    private RequestEndpoint _currentKey;
    private ConnectItem? _pendingConnect;
    private Activity? _waitActivity;
    private long _acquireTimestamp;

    /// <summary>
    /// Tracks the number of in-flight pipelined requests that have been acquired
    /// (via <see cref="StreamAcquireItem"/>) but whose corresponding
    /// <see cref="ConnectionReuseItem"/> response signal has not yet been received.
    /// </summary>
    private int _pendingResponseCount;

    /// <summary>NetworkBuffers buffered before the connection handle is available.</summary>
    private readonly Queue<NetworkBuffer> _pendingWrites = new();

    private bool _upstreamFinished;
    private bool _isReconnecting;
    private CancellationTokenSource? _pumpCts;
    private CancellationTokenSource? _acquireCts;

    public TcpTransportStateMachine(
        ITransportOperations ops,
        IActorRef connectionManager,
        TurboClientOptions clientOptions,
        IActorRef self)
    {
        _ops = ops;
        _connectionManager = connectionManager;
        _clientOptions = clientOptions;
        _self = self;
    }

    public void Dispatch(ITcpTransportEvent evt)
    {
        switch (evt)
        {
            case LeaseAcquired e:
                OnLeaseAcquired(e.Lease);
                break;
            case AcquisitionFailed e:
                OnAcquisitionFailed(e.Error);
                break;
            case InboundBatch e:
                if (e.Gen == _connectionGen)
                {
                    OnInboundBatch(e.Batch, e.Count);
                }
                else
                {
                    ArrayPool<IInputItem>.Shared.Return(e.Batch);
                }

                break;
            case InboundComplete e:
                OnInboundComplete(e.CloseKind, e.Gen);
                break;
            case InboundPumpFailed e:
                _ops.Log.Warning("TcpConnectionStage: Inbound pump failed — {0}", e.Error.Message);
                OnInboundComplete(TlsCloseKind.AbruptClose, _connectionGen);
                break;
            case OutboundWriteDone:
                _ops.OnSignalPullInput();
                break;
            case OutboundWriteFailed e:
                OnOutboundWriteFailed(e.Error);
                break;
            case FlushNextCompleted:
                FlushNext();
                break;
        }
    }

    public void HandlePush(IOutputItem item)
    {
        // Auto-connect: on the first data/control item, derive connection parameters
        // from the item's endpoint and acquire a connection.  Skip ConnectItem — it
        // handles its own acquisition in HandleConnectItem and running AutoConnect
        // first would start a duplicate acquire that races with the real one.
        if (_handle is null && _pendingConnect is null && item is not ConnectItem &&
            item.Key.Scheme is not null && item.Key != RequestEndpoint.Default)
        {
            AutoConnect(item.Key);
        }

        switch (item)
        {
            case ConnectItem connect:
                HandleConnectItem(connect);
                break;

            case NetworkBuffer buffer:
                HandleBuffer(buffer);
                break;

            case ConnectionReuseItem reuseItem:
                HandleConnectionReuseItem(reuseItem);
                break;

            case MaxConcurrentStreamsItem maxStreams:
                _currentLease?.UpdateMaxConcurrentStreams(maxStreams.MaxStreams);
                _ops.OnSignalPullInput();
                break;

            case StreamAcquireItem:
                _currentLease?.MarkBusy();
                _pendingResponseCount++;
                _ops.OnSignalPullInput();
                break;

            case Http3OutputTaggedItem:
                _ops.OnSignalPullInput();
                break;

            case ReconnectItem reconnectItem:
                HandleReconnectItem(reconnectItem);
                break;
        }
    }

    public void HandleUpstreamFinish()
    {
        _upstreamFinished = true;
        if (_handle is null)
        {
            _ops.OnCompleteStage();
        }
        else if (_pendingResponseCount == 0 && _pendingWrites.Count == 0)
        {
            // Connection is idle — no pending responses or buffered writes.
            // Complete now; otherwise we'd wait forever since no more
            // ConnectionReuseItem signals will arrive.
            _connectionGen++;
            StopInboundPump();
            ReturnLeaseToPool(canReuse: true);
            _handle = null;
            _currentLease = null;
            _ops.OnCompleteStage();
        }
        // else: responses still in-flight — HandleConnectionReuseItem will complete when done
    }

    public void HandleDownstreamFinish()
    {
        CleanupTransport();
    }

    private void HandleReconnectItem(ReconnectItem reconnectItem)
    {
        _ops.Log.Debug("TcpConnectionStage: ReconnectItem — tearing down and reconnecting to {0}:{1}",
            reconnectItem.Key.Host, reconnectItem.Key.Port);

        _isReconnecting = true;
        CleanupTransport();

        var options = OptionsFactory.Build(reconnectItem.Key, _clientOptions);
        _pendingConnect = new ConnectItem(options) { Key = reconnectItem.Key };
        AcquireConnection(_pendingConnect.Value);
    }

    private void AutoConnect(RequestEndpoint endpoint)
    {
        _ops.Log.Debug("TcpConnectionStage: AutoConnect for {0}:{1}", endpoint.Host, endpoint.Port);

        var options = OptionsFactory.Build(endpoint, _clientOptions);
        _pendingConnect = new ConnectItem(options) { Key = endpoint };
        AcquireConnection(_pendingConnect.Value);
    }

    private void HandleConnectItem(ConnectItem connect)
    {
        _ops.Log.Debug("TcpConnectionStage: ConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);

        CleanupTransport();
        _pendingConnect = connect;
        AcquireConnection(connect);
    }

    private void HandleBuffer(NetworkBuffer buffer)
    {
        if (_handle is null)
        {
            _ops.Log.Debug("TcpConnectionStage: NetworkBuffer buffered (no handle), length={0}, pending={1}",
                buffer.Length, _pendingWrites.Count + 1);
            _pendingWrites.Enqueue(buffer);
            _ops.OnSignalPullInput();
            return;
        }

        _ops.Log.Debug("TcpConnectionStage: NetworkBuffer writing length={0}", buffer.Length);
        WriteToOutbound(buffer);
    }

    private void HandleConnectionReuseItem(ConnectionReuseItem reuseItem)
    {
        _ops.Log.Debug("TcpConnectionStage: ConnectionReuseItem canReuse={0}, pendingResponseCount={1}",
            reuseItem.Decision.CanReuse, _pendingResponseCount);

        if (!reuseItem.Decision.CanReuse)
        {
            _pendingResponseCount = 0;
            _currentLease?.MarkNoReuse();
            _leaseReturned = false;
            ReturnLeaseToPool(canReuse: false);
            _connectionGen++;
            StopInboundPump();
            _handle = null;
            _currentLease = null;
        }
        else
        {
            if (_pendingResponseCount > 0)
            {
                _pendingResponseCount--;
            }

            if (_pendingResponseCount > 0)
            {
                _ops.OnSignalPullInput();
                return;
            }

            _currentLease?.MarkIdle();
        }

        if (_upstreamFinished)
        {
            if (_handle is not null)
            {
                _connectionGen++;
                StopInboundPump();
                _handle = null;
                _currentLease = null;
            }

            _ops.OnCompleteStage();
            return;
        }

        _ops.OnSignalPullInput();
    }

    public void OnTimer(string? timerKey)
    {
        switch (timerKey)
        {
            case ConnectTimerKey:
            {
                if (_pendingConnect is null)
                {
                    return;
                }

                _ops.Log.Warning("TcpConnectionStage: Connection acquisition timed out for {0}:{1}",
                    _pendingConnect.Value.Key.Host, _pendingConnect.Value.Key.Port);

                _waitActivity?.Stop();
                _waitActivity = null;

                var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Value.Key };
                _pendingConnect = null;

                _ops.OnPushOutput(signal);
                _ops.OnSignalPullInput();
                break;
            }
        }
    }

    private void OnLeaseAcquired(ConnectionLease lease)
    {
        _ops.OnCancelTimer(ConnectTimerKey);

        _waitActivity?.Stop();
        _waitActivity = null;
        var waitDurationS = Stopwatch.GetElapsedTime(_acquireTimestamp).TotalSeconds;
        TurboHttpMetrics.RequestTimeInQueue.Record(waitDurationS,
            new("server.address", lease.Key.Host),
            new("server.port", lease.Key.Port));

        if (_pendingConnect is null && _handle is not null)
        {
            _ops.Log.Debug("TcpConnectionStage: OnLeaseAcquired duplicate — skipped");
            return;
        }

        _pendingConnect = null;

        _connectionGen++;
        _leaseReturned = false;
        _pendingResponseCount = 0;
        _ops.Log.Debug("TcpConnectionStage: OnLeaseAcquired gen={0}, key={1}:{2}",
            _connectionGen, lease.Key.Host, lease.Key.Port);

        _currentLease = lease;
        _handle = lease.Handle;
        _currentKey = lease.Key;

        StartInboundPump();

        if (_isReconnecting)
        {
            _isReconnecting = false;
            _ops.OnPushOutput(new ConnectedSignalItem { Key = _currentKey });
        }

        FlushPendingWrites();
    }

    private void OnOutboundWriteFailed(Exception ex)
    {
        _ops.Log.Warning("TcpConnectionStage: Outbound write failed — {0}", ex.Message);

        if (_currentLease is { } lease)
        {
            lease.MarkNoReuse();
        }

        _leaseReturned = false;
        ReturnLeaseToPool(canReuse: false);

        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        StopInboundPump();
        _handle = null;
        _currentLease = null;

        _ops.OnSignalPullInput();
    }

    private void OnAcquisitionFailed(Exception ex)
    {
        // Ignore cancellations from a previous acquisition that we explicitly
        // cancelled (e.g. CleanupTransport cancelled the old CTS).  Processing
        // this would corrupt the _pendingConnect of a newer, valid acquisition.
        if (ex is OperationCanceledException)
        {
            _ops.Log.Debug("TcpConnectionStage: AcquisitionFailed (cancelled) — ignored");
            return;
        }

        _ops.OnCancelTimer(ConnectTimerKey);

        if (_waitActivity is not null)
        {
            TurboHttpInstrumentation.SetError(_waitActivity, ex);
            _waitActivity.Stop();
            _waitActivity = null;
        }

        _ops.Log.Warning("TcpConnectionStage: Connection acquisition failed — {0}", ex.Message);

        if (_pendingConnect is null)
        {
            return;
        }

        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Value.Key };
        _pendingConnect = null;

        _ops.OnPushOutput(signal);
        _ops.OnSignalPullInput();
    }

    private void OnInboundComplete(TlsCloseKind closeKind, int gen)
    {
        if (gen != _connectionGen)
        {
            _ops.Log.Debug("TcpConnectionStage: OnInboundComplete gen MISMATCH (stale={0}, current={1}) — ignored",
                gen, _connectionGen);
            return;
        }

        _ops.Log.Debug("TcpConnectionStage: OnInboundComplete gen={0}, closeKind={1}", gen, closeKind);

        var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
        _ops.OnPushOutput(signal);

        if (_currentLease is { } lease)
        {
            lease.MarkNoReuse();
        }

        _leaseReturned = false;
        ReturnLeaseToPool(canReuse: false);

        _handle = null;
        _currentLease = null;

        if (_upstreamFinished)
        {
            _ops.OnCompleteStage();
        }
        else
        {
            _ops.OnSignalPullInput();
        }
    }

    private void OnInboundBatch(IInputItem[] batch, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _ops.OnPushOutput(batch[i]);
            batch[i] = null!;
        }

        ArrayPool<IInputItem>.Shared.Return(batch);
    }

    private void AcquireConnection(ConnectItem connect)
    {
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = new CancellationTokenSource();

        _waitActivity = TurboHttpInstrumentation.StartWaitForConnection(
            connect.Key.Host, connect.Key.Port);
        _acquireTimestamp = Stopwatch.GetTimestamp();

        var acquireTask = TcpConnectionManagerActor.AcquireAsync(
            _connectionManager, connect.Options, connect.Key, _acquireCts.Token);

        acquireTask.PipeTo(_self,
            success: lease => new LeaseAcquired(lease),
            failure: ex => new AcquisitionFailed(ex.GetBaseException()));

        const int DefaultConnectTimeoutSeconds = 10;
        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds);
        }

        _ops.OnScheduleTimer(ConnectTimerKey, timeout);
    }

    private void ReturnLeaseToPool(bool canReuse)
    {
        if (_leaseReturned || _currentLease is null)
        {
            return;
        }

        _leaseReturned = true;
        _connectionManager.Tell(new TcpConnectionManagerActor.Release(_currentLease, canReuse));
    }

    private void CleanupTransport()
    {
        _ops.Log.Debug("TcpConnectionStage: CleanupTransport gen={0}", _connectionGen);
        _connectionGen++;
        StopInboundPump();

        // Cancel any in-flight connection acquisition so the ConnectionManager
        // can immediately release the lease instead of sending it to dead letters.
        _acquireCts?.Cancel();
        _acquireCts?.Dispose();
        _acquireCts = null;

        // Stop any pending wait-for-connection activity before it gets
        // overwritten by a subsequent AcquireConnection call.
        _waitActivity?.Stop();
        _waitActivity = null;

        if (_currentLease is { } lease)
        {
            _leaseReturned = false;
            ReturnLeaseToPool(canReuse: false);
            lease.Dispose();
            _currentLease = null;
            _handle = null;
        }
    }

    private void StartInboundPump()
    {
        StopInboundPump();

        var handle = _handle;
        if (handle is null)
        {
            return;
        }

        _pumpCts = new CancellationTokenSource();
        var ct = _pumpCts.Token;
        var reader = handle.InboundReader;
        var key = _currentKey;
        var gen = _connectionGen;
        var self = _self;

        _ = PumpAsync(reader, key, gen, ct, self);
    }

    private static async Task PumpAsync(
        ChannelReader<NetworkBuffer> reader,
        RequestEndpoint key,
        int gen,
        CancellationToken ct,
        IActorRef self)
    {
        var closeKind = TlsCloseKind.CleanClose;
        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                IInputItem[]? batch = null;
                var count = 0;

                while (reader.TryRead(out var chunk))
                {
                    // Early exit when the connection generation changed — the actor thread
                    // always cancels the pump CTS after incrementing _connectionGen, so
                    // checking the token is sufficient. This avoids a cross-thread volatile
                    // read of _connectionGen from the pump's ThreadPool thread.
                    if (ct.IsCancellationRequested)
                    {
                        chunk.Dispose();
                        while (reader.TryRead(out var stale)) stale.Dispose();
                        if (batch is not null) ArrayPool<IInputItem>.Shared.Return(batch);
                        return;
                    }

                    chunk.Key = key;
                    batch ??= ArrayPool<IInputItem>.Shared.Rent(8);

                    if (count == batch.Length)
                    {
                        self.Tell(new InboundBatch(batch, count, gen));
                        batch = ArrayPool<IInputItem>.Shared.Rent(count * 2);
                        count = 0;
                    }

                    batch[count++] = chunk;
                }

                if (count > 0)
                {
                    self.Tell(new InboundBatch(batch!, count, gen));
                }
                else if (batch is not null)
                {
                    ArrayPool<IInputItem>.Shared.Return(batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
        {
            closeKind = TlsCloseKind.AbruptClose;
        }
        catch (Exception ex)
        {
            self.Tell(new InboundPumpFailed(ex));
            return;
        }

        self.Tell(new InboundComplete(closeKind, gen));
    }

    private void StopInboundPump()
    {
        if (_pumpCts is null)
        {
            return;
        }

        _ops.Log.Debug("TcpConnectionStage: StopInboundPump gen={0}", _connectionGen);
        _pumpCts.Cancel();
        _pumpCts.Dispose();
        _pumpCts = null;
    }

    private void WriteToOutbound(NetworkBuffer buffer)
    {
        _handle!.OutboundWriter.WriteAsync(buffer)
            .PipeTo(_self,
                success: () => new OutboundWriteDone(),
                failure: ex => new OutboundWriteFailed(ex));
    }

    private void FlushPendingWrites()
    {
        if (_pendingWrites.Count == 0)
        {
            _ops.OnSignalPullInput();
            return;
        }

        FlushNext();
    }

    private void FlushNext()
    {
        if (!_pendingWrites.TryDequeue(out var dataItem))
        {
            _ops.OnSignalPullInput();
            return;
        }

        if (_handle is { } handle)
        {
            _ = handle.OutboundWriter.WriteAsync(dataItem).PipeTo(_self,
                success: () => new FlushNextCompleted(),
                failure: ex => new OutboundWriteFailed(ex));
        }
        else
        {
            // No handle — connection was torn down while writes were pending.
            // Drain and dispose all remaining writes to avoid leaking buffers,
            // then pull to let upstream know we're ready for new items.
            dataItem.Dispose();
            while (_pendingWrites.TryDequeue(out var orphan))
            {
                orphan.Dispose();
            }

            _ops.OnSignalPullInput();
        }
    }

    public void PostStop()
    {
        _ops.OnCancelTimer(ConnectTimerKey);
        CleanupTransport();

        while (_pendingWrites.TryDequeue(out var orphan))
        {
            orphan.Dispose();
        }
    }
}