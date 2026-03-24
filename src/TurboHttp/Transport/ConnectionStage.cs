using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Pooling;

namespace TurboHttp.Transport;

public sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private IActorRef PoolRouter { get; }

    private readonly Inlet<IOutputItem> _in = new("Connection.In");
    private readonly Outlet<IInputItem> _out = new("Connection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }


    public ConnectionStage(IActorRef poolRouter)
    {
        PoolRouter = poolRouter;
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

        /// <summary>Callback bridging async channel reads into the stage event loop.</summary>
        private Action<IInputItem>? _onInboundData;

        /// <summary>Callback bridging async channel write completion into the stage event loop.</summary>
        private Action? _onOutboundWriteDone;

        /// <summary>Callback invoked when an outbound channel write fails (e.g. channel closed).</summary>
        private Action<Exception>? _onOutboundWriteFailed;

        /// <summary>Callback invoked when a <see cref="ConnectionHandle"/> is received from the actor hierarchy.</summary>
        private Action<ConnectionHandle>? _onHandleReceived;

        /// <summary>Callback invoked when the inbound channel completes (connection closed).</summary>
        private Action<TlsCloseKind>? _onInboundComplete;

        /// <summary>Callback bridging async flush-next completion into the stage event loop.</summary>
        private Action? _onFlushNext;

        private StageActor? _stageActor;
        private CancellationTokenSource? _pumpCts;

        /// <summary>Set when upstream finishes; defers stage completion until the inbound pump drains.</summary>
        private bool _upstreamFinished;

        /// <summary>The RequestEndpoint from the most recent ConnectItem — used to tag inbound DataItems.</summary>
        private RequestEndpoint _currentKey;

        /// <summary>The ConnectItem currently awaiting a ConnectionHandle reply.</summary>
        private ConnectItem? _pendingConnect;

        /// <summary>
        /// Whether StreamCompleted has been sent to the pool for the current handle.
        /// Prevents double StreamCompleted when both HandlePush(ConnectionReuseItem) and
        /// _onInboundComplete fire for the same connection lifecycle.
        /// </summary>
        private bool _handleReturned;

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

                // Notify the pool to tear down the connection (same as ConnectionReuseItem(Close) path).
                if (_handle is { } h)
                {
                    h.ConnectionActor.Tell(
                        new HostPool.MarkConnectionNoReuse(h.ConnectionActor));
                }

                ReturnHandleToPool();

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

                // Accept next element (e.g. a new ConnectItem for reconnection).
                TryPull();
            });

            _onHandleReceived = GetAsyncCallback<ConnectionHandle>(handle =>
            {
                CancelTimer(ConnectTimerKey);

                // Guard: if _pendingConnect is already null, the handle was already
                // received (duplicate delivery from HostPool serving the same requester
                // multiple times). Skip to avoid restarting the inbound pump concurrently.
                if (_pendingConnect is null && _handle is not null)
                {
                    return;
                }

                _pendingConnect = null;

                _handleReturned = false;

                _handle = handle;
                _currentKey = handle.Key;
                StartInboundPump();

                // Flush items that arrived before the handle was available
                // (e.g. HTTP/2 preface buffered during connection setup).
                FlushPendingWrites();
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

                // Notify the pool BEFORE clearing the handle. For HTTP/1.0 the TCP
                // connection closes after every response. If ConnectionReuseItem's
                // HandlePush already sent these messages (synchronous fused-graph path),
                // ReturnHandleToPool is a no-op thanks to _handleReturned.
                if (_handle is { } h)
                {
                    h.ConnectionActor.Tell(
                        new HostPool.MarkConnectionNoReuse(h.ConnectionActor));
                }

                ReturnHandleToPool();

                // Connection closed — clear the handle so next ConnectItem re-acquires.
                _handle = null;

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

            _stageActor = GetStageActor(OnMessage);

            // Ready to accept ConnectItem immediately — no GlobalRefs needed.
            Pull(_stage._in);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is ConnectionHandle handle)
            {
                _onHandleReceived!(handle);
            }
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);
            var handle = _handle;

            if (item is MaxConcurrentStreamsItem maxStreams)
            {
                handle?.ConnectionActor.Tell(
                    new HostPool.UpdateMaxConcurrentStreams(handle.ConnectionActor, maxStreams.MaxStreams));
                TryPull();
                return;
            }

            if (item is StreamAcquireItem)
            {
                handle?.ConnectionActor.Tell(
                    new HostPool.StreamAcquired(handle.ConnectionActor));
                TryPull();
                return;
            }

            if (item is ConnectionReuseItem reuseItem)
            {
                if (!reuseItem.Decision.CanReuse)
                {
                    handle?.ConnectionActor.Tell(
                        new HostPool.MarkConnectionNoReuse(handle.ConnectionActor));
                }

                ReturnHandleToPool();
                TryPull();
                return;
            }

            if (item is ConnectItem connect)
            {
                _pendingConnect = connect;

                SendEnsureHost(connect);

                // Do NOT pull — wait for ConnectionHandle reply before accepting data.
                return;
            }

            if (item is not DataItem dataItem) return;
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
        /// Called after a <see cref="ConnectionHandle"/> is received.
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
        /// Sends <see cref="HostPool.StreamCompleted"/> to the pool exactly once per
        /// connection lifecycle. Idempotent — safe to call from both HandlePush
        /// (ConnectionReuseItem) and <see cref="_onInboundComplete"/>.
        /// </summary>
        private void ReturnHandleToPool()
        {
            if (_handleReturned || _handle is null)
            {
                return;
            }

            _handleReturned = true;
            _handle.ConnectionActor.Tell(
                new HostPool.StreamCompleted(_handle.ConnectionActor));
        }

        /// <summary>
        /// Sends <see cref="PoolRouter.EnsureHost"/> and schedules a single timeout.
        /// If no <see cref="ConnectionHandle"/> arrives before the timer fires,
        /// the stage emits a <see cref="CloseSignalItem"/> and moves on.
        /// Connection establishment and retry are handled by the pool layer
        /// (HostPool + ConnectionActorBase); this stage just waits.
        /// </summary>
        private void SendEnsureHost(ConnectItem connect)
        {
            _stage.PoolRouter.Tell(
                new PoolRouter.EnsureHost(connect.Key, connect.Options),
                _stageActor!.Ref);

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
        }
    }
}