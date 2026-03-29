using System.Buffers;
using System.Threading.Channels;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Handles TCP single-stream transport (HTTP/1.x, HTTP/2) for <see cref="ConnectionStage"/>.
/// Encapsulates pool acquisition, lease lifecycle, inbound pump management, and outbound buffering.
/// </summary>
internal sealed class TcpTransportHandler : ITransportHandler
{
    private readonly ConnectionPool _pool;

    // ── TCP state ──

    private ConnectionHandle? _handle;
    private ConnectionLease? _currentLease;
    private bool _leaseReturned;
    private int _connectionGen;
    private RequestEndpoint _currentKey;
    private ConnectItem? _pendingConnect;

    /// <summary>Outbound items received before the ConnectionHandle was available (e.g. HTTP/2 preface).</summary>
    private readonly Queue<IOutputItem> _pendingWrites = new();

    private bool _upstreamFinished;
    private CancellationTokenSource? _pumpCts;

    // ── Async callbacks (registered in Initialize) ──

    private Action<ConnectionLease>? _onLeaseAcquired;
    private Action<IInputItem>? _onInboundData;
    private Action? _onOutboundWriteDone;
    private Action<Exception>? _onOutboundWriteFailed;
    private Action<Exception>? _onAcquisitionFailed;
    private Action<(TlsCloseKind CloseKind, int Gen)>? _onInboundComplete;
    private Action? _onFlushNext;

    private IStageCallbacks? _callbacks;

    public TcpTransportHandler(ConnectionPool pool)
    {
        _pool = pool;
    }

    /// <inheritdoc/>
    public void Initialize(IStageCallbacks callbacks)
    {
        _callbacks = callbacks;

        _onLeaseAcquired = callbacks.GetAsyncCallback<ConnectionLease>(lease =>
        {
            callbacks.CancelConnectTimeout();

            // Guard: if _pendingConnect is already null and handle is present, this is a duplicate — skip.
            if (_pendingConnect is null && _handle is not null)
            {
                callbacks.LogDebug("TcpTransport: _onLeaseAcquired duplicate — skipped");
                return;
            }

            _pendingConnect = null;

            // Increment generation BEFORE resetting _leaseReturned so that any stale
            // _onInboundComplete from the prior pump is ignored (it carries the old gen).
            _connectionGen++;
            _leaseReturned = false;
            callbacks.LogDebug("TcpTransport: _onLeaseAcquired gen={0}, key={1}:{2}", _connectionGen, lease.Key.Host, lease.Key.Port);

            // Discard any stale inbound items (DataItem / CloseSignalItem) that the
            // prior connection's pump pushed into the queue before it was cancelled.
            // Without this, a stale CloseSignalItem(AbruptClose) could reach the
            // decoder after the new connection is established.
            callbacks.ClearPendingOutput();

            _currentLease = lease;
            _handle = lease.Handle;
            _currentKey = lease.Key;

            StartInboundPump();
            FlushPendingWrites();
        });

        _onInboundData = callbacks.GetAsyncCallback<IInputItem>(item =>
        {
            callbacks.PushOutput(item);
        });

        _onOutboundWriteDone = callbacks.GetAsyncCallback(() =>
        {
            callbacks.SignalPullInput();
        });

        _onOutboundWriteFailed = callbacks.GetAsyncCallback<Exception>(ex =>
        {
            callbacks.LogDebug("TcpTransport: _onOutboundWriteFailed — {0}", ex.Message);
            callbacks.LogWarning("ConnectionStage: Outbound write failed — {0}", ex.Message);

            // Mark lease as non-reusable and release it back to the pool.
            if (_currentLease is { } lease)
            {
                lease.MarkNoReuse();
            }

            ReturnLeaseToPool(canReuse: false);

            // Emit close signal downstream so decoder stages know the connection is dead.
            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
            callbacks.PushOutput(signal);

            // Connection is dead — clear handle so next ConnectItem re-acquires.
            StopInboundPump();
            _handle = null;
            _currentLease = null;

            // Accept next element (e.g. a new ConnectItem for reconnection).
            callbacks.SignalPullInput();
        });

        _onAcquisitionFailed = callbacks.GetAsyncCallback<Exception>(ex =>
        {
            callbacks.CancelConnectTimeout();
            callbacks.LogWarning("ConnectionStage: Connection acquisition failed — {0}", ex.Message);

            if (_pendingConnect is null)
            {
                return;
            }

            // Emit close signal so the decoder/correlation stage fails the pending request.
            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            callbacks.PushOutput(signal);

            // Accept next element from upstream.
            callbacks.SignalPullInput();
        });

        _onInboundComplete = callbacks.GetAsyncCallback<(TlsCloseKind CloseKind, int Gen)>(tuple =>
        {
            var (closeKind, gen) = tuple;

            // Guard: ignore stale pump completions from a prior connection generation.
            // This prevents the old pump's completion from destroying a newly-acquired
            // connection when the events race (old pump completes after new lease acquired).
            if (gen != _connectionGen)
            {
                callbacks.LogDebug("TcpTransport: _onInboundComplete gen MISMATCH (stale={0}, current={1}) — ignored", gen, _connectionGen);
                return;
            }

            callbacks.LogDebug("TcpTransport: _onInboundComplete gen={0}, closeKind={1}", gen, closeKind);

            // Emit close signal to downstream decoder stages before clearing the handle.
            var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
            callbacks.PushOutput(signal);

            // Mark lease as non-reusable and release back to pool.
            if (_currentLease is { } lease)
            {
                lease.MarkNoReuse();
            }

            ReturnLeaseToPool(canReuse: false);

            // Connection closed — clear the handle so next ConnectItem re-acquires.
            _handle = null;
            _currentLease = null;

            if (_upstreamFinished)
            {
                callbacks.RequestCompleteStage();
            }
            else
            {
                // Maintain demand on the inlet so that upstream stages (e.g. Broadcast
                // feeding both ExtractOptionsStage and ConnectionStage) are not blocked.
                // Without this, the Broadcast requires ALL outputs to have demand before
                // pushing — if ConnectionStage has no demand, the reconnection signal
                // never reaches ExtractOptionsStage.InReuse, causing HTTP/1.0 requests
                // that need reconnection (redirect/retry) to deadlock.
                callbacks.SignalPullInput();
            }
        });

        _onFlushNext = callbacks.GetAsyncCallback(FlushNext);
    }

    /// <inheritdoc/>
    public void HandleConnectItem(ConnectItem connect)
    {
        _callbacks!.LogDebug("TcpTransport: HandleConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);
        _pendingConnect = connect;
        AcquireConnection(connect);
        // Do NOT pull — wait for ConnectionLease before accepting data.
    }

    /// <inheritdoc/>
    public void HandleDataItem(DataItem dataItem)
    {
        var handle = _handle;
        if (handle is null)
        {
            // Buffer items that arrive before the connection is established
            // (e.g. HTTP/2 preface from PrependPrefaceStage racing ahead of ConnectItem).
            _callbacks!.LogDebug("TcpTransport: HandleDataItem buffered (no handle), length={0}, pending={1}", dataItem.Length, _pendingWrites.Count + 1);
            _pendingWrites.Enqueue(dataItem);
            _callbacks.SignalPullInput();
            return;
        }

        _callbacks!.LogDebug("TcpTransport: HandleDataItem writing length={0}", dataItem.Length);

        // Write directly to the connection's outbound channel.
        var writeTask = handle.OutboundWriter
            .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(dataItem.Memory, dataItem.Length))
            .AsTask();

        writeTask.ContinueWith(
            _ => _onOutboundWriteDone!(),
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

        writeTask.ContinueWith(
            t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <inheritdoc/>
    /// <remarks>TCP does not use tagged items — this is a no-op.</remarks>
    public void HandleTaggedItem(Http3OutputTaggedItem outputTagged)
    {
        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    public void HandleConnectionReuseItem(ConnectionReuseItem reuseItem)
    {
        _callbacks!.LogDebug("TcpTransport: HandleConnectionReuseItem canReuse={0}, upstreamFinished={1}", reuseItem.Decision.CanReuse, _upstreamFinished);
        if (!reuseItem.Decision.CanReuse)
        {
            _currentLease?.MarkNoReuse();
        }

        ReturnLeaseToPool(reuseItem.Decision.CanReuse);

        if (!reuseItem.Decision.CanReuse)
        {
            _connectionGen++;
            StopInboundPump();
            _handle = null;
            _currentLease = null;
        }

        // If upstream has finished (source completed, i.e. client disposed),
        // no more requests will arrive.  Clean up the connection and complete
        // the stage so stream actors are released promptly.  Without this,
        // the inbound pump keeps reading from an idle TCP socket, the stage
        // never completes, and the actor tree becomes a zombie.
        if (_upstreamFinished)
        {
            if (_handle is not null)
            {
                _connectionGen++;
                StopInboundPump();
                _handle = null;
                _currentLease = null;
            }

            _callbacks!.RequestCompleteStage();
            return;
        }

        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    public void HandleMaxConcurrentStreamsItem(MaxConcurrentStreamsItem item)
    {
        _currentLease?.UpdateMaxConcurrentStreams(item.MaxStreams);
        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    public void HandleStreamAcquireItem(StreamAcquireItem item)
    {
        _currentLease?.MarkBusy();
        _callbacks!.SignalPullInput();
    }

    /// <inheritdoc/>
    public void OnUpstreamFinished()
    {
        _upstreamFinished = true;
        if (_handle is null)
        {
            _callbacks!.RequestCompleteStage();
        }
    }

    /// <inheritdoc/>
    public void OnConnectTimeout()
    {
        if (_pendingConnect is null)
        {
            return;
        }

        _callbacks!.LogWarning(
            "ConnectionStage: Connection acquisition timed out for {0}:{1}",
            _pendingConnect.Key.Host,
            _pendingConnect.Key.Port);

        // Emit close signal so the decoder/correlation stage fails the pending request.
        // The stream stays alive — future ConnectItems can still succeed.
        var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
        _pendingConnect = null;

        _callbacks.PushOutput(signal);

        // Accept next element from upstream.
        _callbacks.SignalPullInput();
    }

    /// <inheritdoc/>
    public void Cleanup()
    {
        _callbacks?.LogDebug("TcpTransport: Cleanup gen={0}", _connectionGen);
        _connectionGen++;          // Invalidate stale async callbacks from the prior pump.
        StopInboundPump();

        if (_currentLease is { } lease)
        {
            lease.Dispose();
            _currentLease = null;
            _handle = null;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acquires a TCP connection from the <see cref="ConnectionPool"/> and schedules a timeout.
    /// If the pool returns a <see cref="ConnectionLease"/> before the timer fires,
    /// the stage starts I/O. Otherwise, a <see cref="CloseSignalItem"/> is emitted.
    /// </summary>
    private void AcquireConnection(ConnectItem connect)
    {
        var acquireTask = _pool.AcquireAsync(connect.Options, connect.Key);

        acquireTask.ContinueWith(
            t => _onLeaseAcquired!(t.Result),
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

        acquireTask.ContinueWith(
            t => _onAcquisitionFailed!(t.Exception!.GetBaseException()),
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);

        var timeout = connect.Options.ConnectTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(10);
        }

        _callbacks!.ScheduleConnectTimeout(timeout);
    }

    /// <summary>
    /// Releases the current lease back to the <see cref="ConnectionPool"/> exactly once per
    /// connection lifecycle. Idempotent — safe to call from both HandleConnectionReuseItem
    /// and <see cref="_onInboundComplete"/>.
    /// </summary>
    private void ReturnLeaseToPool(bool canReuse)
    {
        if (_leaseReturned || _currentLease is null)
        {
            return;
        }

        _leaseReturned = true;
        _pool.Release(_currentLease, canReuse);
    }

    /// <summary>
    /// Starts an async loop that reads from <see cref="ConnectionHandle.InboundReader"/>
    /// and pushes each chunk into the stage via <see cref="_onInboundData"/>. TCP path only.
    /// </summary>
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
        var onData = _onInboundData!;
        var onComplete = _onInboundComplete!;

        _ = Task.Run(async () =>
        {
            var closeKind = TlsCloseKind.CleanClose;
            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var chunk))
                    {
                        if (gen != _connectionGen)
                        {
                            // Stale pump — discard this chunk and drain the rest to avoid memory leaks.
                            chunk.Buffer.Dispose();
                            while (reader.TryRead(out var stale))
                            {
                                stale.Buffer.Dispose();
                            }
                            return;
                        }

                        var dataItem = new DataItem(chunk.Buffer, chunk.ReadableBytes) { Key = key };
                        onData(dataItem);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on stage shutdown — do not emit close signal.
                return;
            }
            catch (ChannelClosedException ex) when (ex.InnerException is AbruptCloseException)
            {
                closeKind = TlsCloseKind.AbruptClose;
            }

            onComplete((closeKind, gen));
        }, ct);
    }

    private void StopInboundPump()
    {
        if (_pumpCts is null)
        {
            return;
        }

        _callbacks!.LogDebug("TcpTransport: StopInboundPump gen={0}", _connectionGen);
        _pumpCts.Cancel();
        _pumpCts.Dispose();
        _pumpCts = null;
    }

    /// <summary>
    /// Writes all buffered outbound items to the connection and then pulls upstream.
    /// Called after a <see cref="ConnectionLease"/> is acquired.
    /// </summary>
    private void FlushPendingWrites()
    {
        if (_pendingWrites.Count == 0)
        {
            _callbacks!.SignalPullInput();
            return;
        }

        FlushNext();
    }

    private void FlushNext()
    {
        if (!_pendingWrites.TryDequeue(out var item))
        {
            // All buffered items flushed — resume normal upstream pulls.
            _callbacks!.SignalPullInput();
            return;
        }

        if (item is DataItem dataItem && _handle is { } handle)
        {
            var writeTask = handle.OutboundWriter
                .WriteAsync(new ValueTuple<IMemoryOwner<byte>, int>(dataItem.Memory, dataItem.Length))
                .AsTask();

            writeTask.ContinueWith(
                _ => _onFlushNext!(),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            writeTask.ContinueWith(
                t => _onOutboundWriteFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            // Non-data items shouldn't be buffered, but handle gracefully.
            FlushNext();
        }
    }
}
