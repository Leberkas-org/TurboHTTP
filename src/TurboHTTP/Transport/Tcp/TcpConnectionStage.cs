using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Transport stage for HTTP/1.0, HTTP/1.1, and HTTP/2 (TCP/TLS).
/// Owns the I/O pumps (zero-copy, no thread hop) and delegates connection lifecycle
/// (acquire, release, idle reuse, eviction) to the <see cref="ConnectionManagerActor"/>.
/// <para>
/// Replaces the former <c>ConnectionStage</c> + <c>TcpTransportHandler</c> + <c>IStageCallbacks</c>
/// abstraction with a single, direct GraphStage implementation.
/// </para>
/// </summary>
internal sealed class TcpConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    internal IActorRef ConnectionManager { get; }
    internal TurboClientOptions ClientOptions { get; }

    private readonly Inlet<IOutputItem> _in = new("TcpConnection.In");
    private readonly Outlet<IInputItem> _out = new("TcpConnection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public TcpConnectionStage(IActorRef connectionManager, TurboClientOptions clientOptions)
    {
        ConnectionManager = connectionManager;
        ClientOptions = clientOptions;
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private const string ConnectTimerKey = "connect-timeout";

        private readonly TcpConnectionStage _stage;

        private readonly Queue<IInputItem> _pendingReads = new();

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
        /// The connection lease must NOT be returned to the pool while pipelined
        /// responses are still in-flight — returning early makes it available for
        /// reuse by another sub-stream slot, causing two concurrent readers on
        /// the same <see cref="ChannelReader{T}"/>.
        /// </summary>
        private int _pendingResponseCount;

        /// <summary>NetworkBuffers buffered before the connection handle is available.</summary>
        private readonly Queue<NetworkBuffer> _pendingWrites = new();

        private bool _upstreamFinished;
        private bool _isReconnecting;
        private CancellationTokenSource? _pumpCts;

        private Action<ConnectionLease>? _onLeaseAcquired;
        private Action<(IInputItem[] Batch, int Count)>? _onInboundBatch;
        private Action? _onOutboundWriteDone;
        private Action<Exception>? _onOutboundWriteFailed;
        private Action<Exception>? _onAcquisitionFailed;
        private Action<(TlsCloseKind CloseKind, int Gen)>? _onInboundComplete;
        private Action? _onFlushNext;
        private Action<Exception>? _onInboundPumpFailed;

        public Logic(TcpConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _upstreamFinished = true;
                    if (_handle is null)
                    {
                        CompleteStage();
                    }
                });

            SetHandler(stage._out,
                onPull: () =>
                {
                    if (_pendingReads.TryDequeue(out var item))
                    {
                        Push(_stage._out, item);
                    }
                },
                onDownstreamFinish: _ =>
                {
                    CleanupTransport();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            RegisterAsyncCallbacks();
            Pull(_stage._in);
        }


        private void RegisterAsyncCallbacks()
        {
            _onLeaseAcquired = GetAsyncCallback<ConnectionLease>(OnLeaseAcquired);
            _onInboundBatch = GetAsyncCallback<(IInputItem[] Batch, int Count)>(tuple =>
            {
                var (batch, count) = tuple;
                for (var i = 0; i < count; i++)
                {
                    PushOutput(batch[i]);
                    batch[i] = null!;
                }
                ArrayPool<IInputItem>.Shared.Return(batch);
            });
            _onOutboundWriteDone = GetAsyncCallback(SignalPullInput);
            _onOutboundWriteFailed = GetAsyncCallback<Exception>(OnOutboundWriteFailed);
            _onAcquisitionFailed = GetAsyncCallback<Exception>(OnAcquisitionFailed);
            _onInboundComplete = GetAsyncCallback<(TlsCloseKind, int)>(OnInboundComplete);
            _onFlushNext = GetAsyncCallback(FlushNext);
            _onInboundPumpFailed = GetAsyncCallback<Exception>(FailStage);
        }


        private void HandlePush()
        {
            var item = Grab(_stage._in);

            // Auto-connect: on the first item (any type), derive connection parameters
            // from the item's endpoint and acquire a connection. This eliminates the need
            // for a separate ExtractOptionsStage and its MergePreferred wiring.
            if (_handle is null && _pendingConnect is null && item.Key.Scheme is not null &&
                item.Key != RequestEndpoint.Default)
            {
                AutoConnect(item.Key);
            }

            switch (item)
            {
                case ConnectItem connect:
                    // Legacy path — still supported for backward compatibility with tests
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
                    SignalPullInput();
                    break;

                case StreamAcquireItem acquireItem:
                    _currentLease?.MarkBusy();
                    _pendingResponseCount++;
                    SignalPullInput();
                    break;

                case Http3OutputTaggedItem:
                    SignalPullInput();
                    break;

                case PipelineRetryItem retry:
                    HandlePipelineRetryItem(retry);
                    break;

                case ReconnectItem reconnectItem:
                    HandleReconnectItem(reconnectItem);
                    break;
            }
        }

        private void HandlePipelineRetryItem(PipelineRetryItem retry)
        {
            // The H1.1 correlation stage detected that the server closed the connection
            // while this request was still in-flight (pipelined). Just unblock and continue.
            Log.Warning("TcpConnectionStage: PipelineRetryItem — abandoning {0} (no retry)",
                retry.Request.RequestUri);
            SignalPullInput();
        }

        private void HandleReconnectItem(ReconnectItem reconnectItem)
        {
            Log.Debug("TcpConnectionStage: ReconnectItem — tearing down and reconnecting to {0}:{1}",
                reconnectItem.Key.Host, reconnectItem.Key.Port);

            _isReconnecting = true;
            CleanupTransport();

            var options = TcpOptionsFactory.Build(reconnectItem.Key, _stage.ClientOptions);
            _pendingConnect = new ConnectItem(options) { Key = reconnectItem.Key };
            AcquireConnection(_pendingConnect.Value);
            // Do NOT pull — wait for new ConnectionLease before accepting outbound data
        }

        private void AutoConnect(RequestEndpoint endpoint)
        {
            Log.Debug("TcpConnectionStage: AutoConnect for {0}:{1}", endpoint.Host, endpoint.Port);

            var options = TcpOptionsFactory.Build(endpoint, _stage.ClientOptions);
            _pendingConnect = new ConnectItem(options) { Key = endpoint };
            AcquireConnection(_pendingConnect.Value);
        }


        private void HandleConnectItem(ConnectItem connect)
        {
            Log.Debug("TcpConnectionStage: ConnectItem key={0}:{1}", connect.Key.Host, connect.Key.Port);

            // Clean up prior connection if a new ConnectItem arrives
            CleanupTransport();

            // Flush DataItems that arrived before this ConnectItem (e.g. HTTP/2 preface
            // from PrependPrefaceStage racing ahead of ConnectItem). They are re-queued
            // into _pendingWrites and will be flushed after the handle is available.
            _pendingConnect = connect;
            AcquireConnection(connect);
            // Do NOT pull — wait for ConnectionLease before accepting data.
        }


        private void HandleBuffer(NetworkBuffer buffer)
        {
            if (_handle is null)
            {
                Log.Debug("TcpConnectionStage: NetworkBuffer buffered (no handle), length={0}, pending={1}",
                    buffer.Length, _pendingWrites.Count + 1);
                _pendingWrites.Enqueue(buffer);
                SignalPullInput();
                return;
            }

            Log.Debug("TcpConnectionStage: NetworkBuffer writing length={0}", buffer.Length);
            WriteToOutbound(buffer);
        }


        private void HandleConnectionReuseItem(ConnectionReuseItem reuseItem)
        {
            Log.Debug("TcpConnectionStage: ConnectionReuseItem canReuse={0}, pendingResponseCount={1}",
                reuseItem.Decision.CanReuse, _pendingResponseCount);

            if (!reuseItem.Decision.CanReuse)
            {
                // Server requested connection close — tear down immediately
                _pendingResponseCount = 0;
                _currentLease?.MarkNoReuse();
                _leaseReturned = false; // Reset so release is not skipped after prior canReuse=true
                ReturnLeaseToPool(canReuse: false);
                _connectionGen++;
                StopInboundPump();
                _handle = null;
                _currentLease = null;
            }
            else
            {
                // Keep-alive. Track pipelined responses but do NOT return the lease.
                // The substream keeps exclusive ownership of its connection — returning
                // it to the pool would allow the actor to hand it to a different substream
                // while this one still holds the handle, causing data corruption.
                if (_pendingResponseCount > 0)
                {
                    _pendingResponseCount--;
                }

                if (_pendingResponseCount > 0)
                {
                    // More pipelined responses expected
                    SignalPullInput();
                    return;
                }

                // All in-flight responses accounted for — connection stays owned by this stage.
                // Mark idle on the lease so the actor knows it's not actively streaming,
                // but do NOT return to the pool.
                _currentLease?.MarkIdle();
            }

            // If upstream finished, complete the stage
            if (_upstreamFinished)
            {
                if (_handle is not null)
                {
                    _connectionGen++;
                    StopInboundPump();
                    _handle = null;
                    _currentLease = null;
                }

                CompleteStage();
                return;
            }

            SignalPullInput();
        }


        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key || key != ConnectTimerKey)
            {
                return;
            }

            if (_pendingConnect is null)
            {
                return;
            }

            Log.Warning("TcpConnectionStage: Connection acquisition timed out for {0}:{1}",
                _pendingConnect.Value.Key.Host, _pendingConnect.Value.Key.Port);

            // Stop WaitForConnection span on timeout
            _waitActivity?.Stop();
            _waitActivity = null;

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Value.Key };
            _pendingConnect = null;

            PushOutput(signal);
            SignalPullInput();
        }


        private void OnLeaseAcquired(ConnectionLease lease)
        {
            CancelTimer(ConnectTimerKey);

            // Stop WaitForConnection span and record RequestTimeInQueue metric
            _waitActivity?.Stop();
            _waitActivity = null;
            var waitDurationS = Stopwatch.GetElapsedTime(_acquireTimestamp).TotalSeconds;
            TurboHttpMetrics.RequestTimeInQueue.Record(waitDurationS,
                new("server.address", lease.Key.Host),
                new("server.port", lease.Key.Port));

            // Guard: duplicate lease arrival
            if (_pendingConnect is null && _handle is not null)
            {
                Log.Debug("TcpConnectionStage: OnLeaseAcquired duplicate — skipped");
                return;
            }

            _pendingConnect = null;

            // Increment generation BEFORE resetting _leaseReturned so that any stale
            // _onInboundComplete from the prior pump is ignored (it carries the old gen).
            _connectionGen++;
            _leaseReturned = false;
            _pendingResponseCount = 0;
            Log.Debug("TcpConnectionStage: OnLeaseAcquired gen={0}, key={1}:{2}",
                _connectionGen, lease.Key.Host, lease.Key.Port);

            // Discard stale inbound items from the prior connection's pump
            _pendingReads.Clear();

            _currentLease = lease;
            _handle = lease.Handle;
            _currentKey = lease.Key;

            StartInboundPump();

            if (_isReconnecting)
            {
                _isReconnecting = false;
                PushOutput(new ConnectedSignalItem { Key = _currentKey });
            }

            FlushPendingWrites();
        }

        private void OnOutboundWriteFailed(Exception ex)
        {
            Log.Warning("TcpConnectionStage: Outbound write failed — {0}", ex.Message);

            if (_currentLease is { } lease)
            {
                lease.MarkNoReuse();
            }

            // Reset _leaseReturned so the release is not skipped — the connection
            // may have been returned to idle (canReuse=true) earlier, but now it's dead.
            _leaseReturned = false;
            ReturnLeaseToPool(canReuse: false);

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
            PushOutput(signal);

            StopInboundPump();
            _handle = null;
            _currentLease = null;

            SignalPullInput();
        }

        private void OnAcquisitionFailed(Exception ex)
        {
            CancelTimer(ConnectTimerKey);

            // Stop WaitForConnection span with error
            if (_waitActivity is not null)
            {
                TurboHttpInstrumentation.SetError(_waitActivity, ex);
                _waitActivity.Stop();
                _waitActivity = null;
            }

            Log.Warning("TcpConnectionStage: Connection acquisition failed — {0}", ex.Message);

            if (_pendingConnect is null)
            {
                return;
            }

            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Value.Key };
            _pendingConnect = null;

            PushOutput(signal);
            SignalPullInput();
        }

        private void OnInboundComplete((TlsCloseKind CloseKind, int Gen) tuple)
        {
            var (closeKind, gen) = tuple;

            // Ignore stale pump completions from a prior connection generation
            if (gen != _connectionGen)
            {
                Log.Debug("TcpConnectionStage: OnInboundComplete gen MISMATCH (stale={0}, current={1}) — ignored",
                    gen, _connectionGen);
                return;
            }

            Log.Debug("TcpConnectionStage: OnInboundComplete gen={0}, closeKind={1}", gen, closeKind);

            var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
            PushOutput(signal);

            if (_currentLease is { } lease)
            {
                lease.MarkNoReuse();
            }

            // Reset _leaseReturned so the release is not skipped — the connection
            // may have been returned to idle (canReuse=true) earlier, but now it's dead.
            // Without this, the actor never learns the connection died, the slot stays
            // occupied, and subsequent acquire requests time out.
            _leaseReturned = false;
            ReturnLeaseToPool(canReuse: false);

            _handle = null;
            _currentLease = null;

            if (_upstreamFinished)
            {
                CompleteStage();
            }
            else
            {
                SignalPullInput();
            }
        }


        public override void PostStop()
        {
            CancelTimer(ConnectTimerKey);
            CleanupTransport();

            // Dispose any NetworkBuffers buffered before the handle was available
            while (_pendingWrites.TryDequeue(out var orphan))
            {
                orphan.Dispose();
            }
        }


        private void AcquireConnection(ConnectItem connect)
        {
            _waitActivity = TurboHttpInstrumentation.StartWaitForConnection(
                connect.Key.Host, connect.Key.Port);
            _acquireTimestamp = Stopwatch.GetTimestamp();

            var acquireTask = ConnectionManagerActor.AcquireAsync(
                _stage.ConnectionManager, connect.Options, connect.Key);

            acquireTask.ContinueWith(
                t => _onLeaseAcquired!(t.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);

            acquireTask.ContinueWith(
                t => _onAcquisitionFailed!(t.Exception!.GetBaseException()),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);

            const int DefaultConnectTimeoutSeconds = 10; // Conservative default; most TCP stacks retry for 30–60 s
            var timeout = connect.Options.ConnectTimeout;
            if (timeout <= TimeSpan.Zero)
            {
                timeout = TimeSpan.FromSeconds(DefaultConnectTimeoutSeconds);
            }

            ScheduleOnce(ConnectTimerKey, timeout);
        }

        private void ReturnLeaseToPool(bool canReuse)
        {
            if (_leaseReturned || _currentLease is null)
            {
                return;
            }

            _leaseReturned = true;
            _stage.ConnectionManager.Tell(new ConnectionManagerActor.Release(_currentLease, canReuse));
        }

        private void CleanupTransport()
        {
            Log.Debug("TcpConnectionStage: CleanupTransport gen={0}", _connectionGen);
            _connectionGen++;
            StopInboundPump();

            if (_currentLease is { } lease)
            {
                // Notify actor that this connection is done (frees the slot)
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
            var onBatch = _onInboundBatch!;
            var onComplete = _onInboundComplete!;
            var onFailed = _onInboundPumpFailed!;

            _ = PumpAsync(reader, key, gen, ct, onBatch, onComplete, onFailed);
        }

        private async Task PumpAsync(
            ChannelReader<NetworkBuffer> reader,
            RequestEndpoint key,
            int gen,
            CancellationToken ct,
            Action<(IInputItem[] Batch, int Count)> onBatch,
            Action<(TlsCloseKind CloseKind, int Gen)> onComplete,
            Action<Exception> onFailed)
        {
            var closeKind = TlsCloseKind.CleanClose;
            try
            {
                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    // Drain all available chunks into a single batch and dispatch once
                    // to the stage actor, collapsing N GetAsyncCallback dispatches into 1.
                    IInputItem[]? batch = null;
                    var count = 0;

                    while (reader.TryRead(out var chunk))
                    {
                        if (gen != _connectionGen)
                        {
                            // Stale pump — drain and exit
                            chunk.Dispose();
                            while (reader.TryRead(out var stale)) stale.Dispose();
                            if (batch is not null) ArrayPool<IInputItem>.Shared.Return(batch);
                            return;
                        }

                        chunk.Key = key;
                        batch ??= ArrayPool<IInputItem>.Shared.Rent(8);

                        if (count == batch.Length)
                        {
                            // Rare: batch full — flush current and rent a larger one
                            onBatch((batch, count));
                            batch = ArrayPool<IInputItem>.Shared.Rent(count * 2);
                            count = 0;
                        }

                        batch[count++] = chunk;
                    }

                    if (count > 0)
                    {
                        onBatch((batch!, count));
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
                onFailed(ex);
                return;
            }

            onComplete((closeKind, gen));
        }

        private void StopInboundPump()
        {
            if (_pumpCts is null)
            {
                return;
            }

            Log.Debug("TcpConnectionStage: StopInboundPump gen={0}", _connectionGen);
            _pumpCts.Cancel();
            _pumpCts.Dispose();
            _pumpCts = null;
        }


        private void WriteToOutbound(NetworkBuffer buffer)
        {
            var vt = _handle!.OutboundWriter.WriteAsync(buffer);

            if (vt.IsCompletedSuccessfully)
            {
                // Fast path: synchronous completion — pull next directly without
                // routing through the actor mailbox (avoids GetAsyncCallback overhead).
                SignalPullInput();
                return;
            }

            // Slow path: async completion — dispatch through actor mailbox.
            var onDone = _onOutboundWriteDone!;
            var onFailed = _onOutboundWriteFailed!;
            _ = AwaitWrite(vt, onDone, onFailed);

            static async Task AwaitWrite(
                ValueTask vt,
                Action onDone,
                Action<Exception> onFailed)
            {
                try
                {
                    await vt.ConfigureAwait(false);
                    onDone();
                }
                catch (Exception ex)
                {
                    onFailed(ex);
                }
            }
        }

        private void FlushPendingWrites()
        {
            if (_pendingWrites.Count == 0)
            {
                SignalPullInput();
                return;
            }

            FlushNext();
        }

        private void FlushNext()
        {
            if (!_pendingWrites.TryDequeue(out var dataItem))
            {
                SignalPullInput();
                return;
            }

            if (_handle is { } handle)
            {
                var vt = handle.OutboundWriter.WriteAsync(dataItem);

                if (vt.IsCompletedSuccessfully)
                {
                    // Fast path: synchronous completion — continue draining without
                    // routing through the actor mailbox.
                    FlushNext();
                    return;
                }

                var onDone = _onFlushNext!;
                var onFailed = _onOutboundWriteFailed!;
                _ = AwaitFlushNext(vt, onDone, onFailed);

                static async Task AwaitFlushNext(
                    ValueTask vt,
                    Action onDone,
                    Action<Exception> onFailed)
                {
                    try
                    {
                        await vt.ConfigureAwait(false);
                        onDone();
                    }
                    catch (Exception ex)
                    {
                        onFailed(ex);
                    }
                }
            }
            else
            {
                FlushNext();
            }
        }


        private void PushOutput(IInputItem item)
        {
            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, item);
            }
            else
            {
                _pendingReads.Enqueue(item);
            }
        }

        private void SignalPullInput()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}