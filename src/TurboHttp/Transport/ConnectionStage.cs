using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Pooling;

namespace TurboHttp.Transport;

internal sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private ConnectionPool Pool { get; }

    private readonly Inlet<IOutputItem> _in = new("Connection.In");
    private readonly Outlet<IInputItem> _out = new("Connection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public ConnectionStage(ConnectionPool pool)
    {
        Pool = pool;
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        /// <summary>Timer key for connection acquisition timeout.</summary>
        private const string ConnectTimerKey = "connect-timeout";

        private readonly ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        /// <summary>Outbound items received before the ConnectionHandle was available (e.g. HTTP/2 preface).</summary>
        private readonly Queue<IOutputItem> _pendingWrites = new();

        /// <summary>Current connection handle providing direct channel I/O.</summary>
        private ConnectionHandle? _handle;

        /// <summary>Current connection lease wrapping the handle with lifecycle management.</summary>
        private ConnectionLease? _currentLease;

        /// <summary>Callback bridging async channel reads into the stage event loop.</summary>
        private Action<IInputItem>? _onInboundData;

        /// <summary>Callback bridging async channel write completion into the stage event loop.</summary>
        private Action? _onOutboundWriteDone;

        /// <summary>Callback invoked when an outbound channel write fails (e.g. channel closed).</summary>
        private Action<Exception>? _onOutboundWriteFailed;

        /// <summary>Callback invoked when a <see cref="ConnectionLease"/> is acquired from the pool.</summary>
        private Action<ConnectionLease>? _onLeaseAcquired;

        /// <summary>Callback invoked when connection acquisition fails.</summary>
        private Action<Exception>? _onAcquisitionFailed;

        /// <summary>Callback invoked when the inbound channel completes (connection closed).</summary>
        private Action<TlsCloseKind>? _onInboundComplete;

        /// <summary>Callback bridging async flush-next completion into the stage event loop.</summary>
        private Action? _onFlushNext;

        private CancellationTokenSource? _pumpCts;

        /// <summary>Set when upstream finishes; defers stage completion until the inbound pump drains.</summary>
        private bool _upstreamFinished;

        /// <summary>The RequestEndpoint from the most recent ConnectItem — used to tag inbound DataItems.</summary>
        private RequestEndpoint _currentKey;

        /// <summary>The ConnectItem currently awaiting a ConnectionLease.</summary>
        private ConnectItem? _pendingConnect;

        /// <summary>
        /// Whether the lease has been returned to the pool for the current connection lifecycle.
        /// Prevents double-release when both HandlePush(ConnectionReuseItem) and
        /// _onInboundComplete fire for the same connection lifecycle.
        /// </summary>
        private bool _leaseReturned;

        public Logic(ConnectionStage stage) : base(stage.Shape)
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
                    StopInboundPump();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onInboundData = GetAsyncCallback<IInputItem>(item =>
            {
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, item);
                }
                else
                {
                    _pendingReads.Enqueue(item);
                }
            });

            _onOutboundWriteDone = GetAsyncCallback(() =>
            {
                if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            _onOutboundWriteFailed = GetAsyncCallback<Exception>(ex =>
            {
                Log.Warning("ConnectionStage: Outbound write failed — {0}", ex.Message);

                // Mark lease as non-reusable and release it back to the pool.
                if (_currentLease is { } lease)
                {
                    lease.MarkNoReuse();
                }

                ReturnLeaseToPool(canReuse: false);

                // Emit close signal downstream so decoder stages know the connection is dead.
                var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _currentKey };
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, signal);
                }
                else
                {
                    _pendingReads.Enqueue(signal);
                }

                // Connection is dead — clear handle so next ConnectItem re-acquires.
                StopInboundPump();
                _handle = null;
                _currentLease = null;

                // Accept next element (e.g. a new ConnectItem for reconnection).
                TryPull();
            });

            _onLeaseAcquired = GetAsyncCallback<ConnectionLease>(lease =>
            {
                CancelTimer(ConnectTimerKey);

                // Guard: if _pendingConnect is already null, the lease was already
                // received (e.g. duplicate). Skip to avoid restarting the inbound pump concurrently.
                if (_pendingConnect is null && _handle is not null)
                {
                    return;
                }

                _pendingConnect = null;

                _leaseReturned = false;

                _currentLease = lease;
                _handle = lease.Handle;
                _currentKey = lease.Key;
                StartInboundPump();

                // Flush items that arrived before the handle was available
                // (e.g. HTTP/2 preface buffered during connection setup).
                FlushPendingWrites();
            });

            _onAcquisitionFailed = GetAsyncCallback<Exception>(ex =>
            {
                CancelTimer(ConnectTimerKey);

                Log.Warning("ConnectionStage: Connection acquisition failed — {0}", ex.Message);

                if (_pendingConnect is null)
                {
                    return;
                }

                // Emit close signal so the decoder/correlation stage fails the pending request.
                var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
                _pendingConnect = null;

                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, signal);
                }
                else
                {
                    _pendingReads.Enqueue(signal);
                }

                // Accept next element from upstream.
                TryPull();
            });

            _onInboundComplete = GetAsyncCallback<TlsCloseKind>(closeKind =>
            {
                // Emit close signal to downstream decoder stages before clearing the handle.
                var signal = new CloseSignalItem(closeKind) { Key = _currentKey };
                if (IsAvailable(_stage._out))
                {
                    Push(_stage._out, signal);
                }
                else
                {
                    _pendingReads.Enqueue(signal);
                }

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
                    CompleteStage();
                }
                else
                {
                    // Maintain demand on the inlet so that upstream stages (e.g. Broadcast
                    // feeding both ExtractOptionsStage and ConnectionStage) are not blocked.
                    // Without this, the Broadcast requires ALL outputs to have demand before
                    // pushing — if ConnectionStage has no demand, the reconnection signal
                    // never reaches ExtractOptionsStage.InReuse, causing HTTP/1.0 requests
                    // that need reconnection (redirect/retry) to deadlock.
                    TryPull();
                }
            });

            _onFlushNext = GetAsyncCallback(FlushNext);

            // Ready to accept ConnectItem immediately.
            Pull(_stage._in);
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);
            var lease = _currentLease;

            if (item is MaxConcurrentStreamsItem maxStreams)
            {
                lease?.UpdateMaxConcurrentStreams(maxStreams.MaxStreams);
                TryPull();
                return;
            }

            if (item is StreamAcquireItem)
            {
                lease?.MarkBusy();
                TryPull();
                return;
            }

            if (item is ConnectionReuseItem reuseItem)
            {
                if (!reuseItem.Decision.CanReuse)
                {
                    lease?.MarkNoReuse();
                }

                ReturnLeaseToPool(reuseItem.Decision.CanReuse);
                TryPull();
                return;
            }

            if (item is ConnectItem connect)
            {
                _pendingConnect = connect;

                AcquireConnection(connect);

                // Do NOT pull — wait for ConnectionLease before accepting data.
                return;
            }

            if (item is not DataItem dataItem) return;
            var handle = _handle;
            if (handle is null)
            {
                // Buffer items that arrive before the connection is established
                // (e.g. HTTP/2 preface from PrependPrefaceStage racing ahead of ConnectItem).
                _pendingWrites.Enqueue(dataItem);
                TryPull();
                return;
            }

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

        /// <summary>
        /// Writes all buffered outbound items to the connection and then pulls upstream.
        /// Called after a <see cref="ConnectionLease"/> is acquired.
        /// </summary>
        private void FlushPendingWrites()
        {
            if (_pendingWrites.Count == 0)
            {
                TryPull();
                return;
            }

            FlushNext();
        }

        private void FlushNext()
        {
            if (!_pendingWrites.TryDequeue(out var item))
            {
                // All buffered items flushed — resume normal upstream pulls.
                TryPull();
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

        private void TryPull()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        /// <summary>
        /// Releases the current lease back to the <see cref="ConnectionPool"/> exactly once per
        /// connection lifecycle. Idempotent — safe to call from both HandlePush
        /// (ConnectionReuseItem) and <see cref="_onInboundComplete"/>.
        /// </summary>
        private void ReturnLeaseToPool(bool canReuse)
        {
            if (_leaseReturned || _currentLease is null)
            {
                return;
            }

            _leaseReturned = true;
            _stage.Pool.Release(_currentLease, canReuse);
        }

        /// <summary>
        /// Acquires a connection from the <see cref="ConnectionPool"/> and schedules a timeout.
        /// If the pool returns a <see cref="ConnectionLease"/> before the timer fires,
        /// the stage starts I/O. Otherwise, a <see cref="CloseSignalItem"/> is emitted.
        /// </summary>
        private void AcquireConnection(ConnectItem connect)
        {
            var acquireTask = _stage.Pool.AcquireAsync(connect.Options, connect.Key);

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

            ScheduleOnce(ConnectTimerKey, timeout);
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

            Log.Warning(
                "ConnectionStage: Connection acquisition timed out for {0}:{1}",
                _pendingConnect.Key.Host,
                _pendingConnect.Key.Port);

            // Emit close signal so the decoder/correlation stage fails the pending request.
            // The stream stays alive — future ConnectItems can still succeed.
            var signal = new CloseSignalItem(TlsCloseKind.AbruptClose) { Key = _pendingConnect.Key };
            _pendingConnect = null;

            if (IsAvailable(_stage._out))
            {
                Push(_stage._out, signal);
            }
            else
            {
                _pendingReads.Enqueue(signal);
            }

            // Accept next element from upstream.
            TryPull();
        }

        /// <summary>
        /// Starts an async loop that reads from <see cref="ConnectionHandle.InboundReader"/>
        /// and pushes each chunk into the stage via <see cref="_onInboundData"/>.
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

                onComplete(closeKind);
            }, ct);
        }

        private void StopInboundPump()
        {
            if (_pumpCts is null) return;
            _pumpCts.Cancel();
            _pumpCts.Dispose();
            _pumpCts = null;
        }

        public override void PostStop()
        {
            CancelTimer(ConnectTimerKey);
            StopInboundPump();

            // Dispose the current lease if still held.
            if (_currentLease is { } lease)
            {
                _ = lease.DisposeAsync();
                _currentLease = null;
                _handle = null;
            }
        }
    }
}
