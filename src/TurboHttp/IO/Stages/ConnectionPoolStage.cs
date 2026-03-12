using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

/// <summary>
/// Central connection pool stage that orchestrates multiple <see cref="ConnectionStage"/> instances
/// per host. Routes <see cref="RoutedTransportItem"/> to per-host connection pools and emits
/// <see cref="RoutedDataItem"/> responses.
/// </summary>
public sealed class ConnectionPoolStage : GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>
{
    private readonly Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> _connectionFlowFactory;
    private readonly PoolConfig _config;

    private readonly Inlet<RoutedTransportItem> _inlet = new("pool.in");
    private readonly Outlet<RoutedDataItem> _outlet = new("pool.out");

    public override FlowShape<RoutedTransportItem, RoutedDataItem> Shape { get; }

    public ConnectionPoolStage(
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> connectionFlowFactory,
        PoolConfig config)
    {
        _connectionFlowFactory = connectionFlowFactory ?? throw new ArgumentNullException(nameof(connectionFlowFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Shape = new FlowShape<RoutedTransportItem, RoutedDataItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic
    {
        private readonly ConnectionPoolStage _stage;
        private IActorRef _self = ActorRefs.Nobody;
        private bool _stopping;
        private int _inFlightCount;

        /// <summary>
        /// Tracks per-host connection pools. Maps pool key → HostPool.
        /// </summary>
        private readonly Dictionary<string, HostPool> _hostPools = new();

        /// <summary>
        /// Pending responses waiting to be pushed downstream.
        /// </summary>
        private readonly Queue<RoutedDataItem> _pendingResponses = new();

        private Action<(string poolKey, int slotId, IMemoryOwner<byte> memory, int length)>? _onResponseCallback;
        private Action<(string poolKey, int slotId, Exception? exception)>? _onSlotDeathCallback;

        public Logic(ConnectionPoolStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet, onPush: HandlePush, onUpstreamFinish: () =>
            {
                _stopping = true;
                TryComplete();
            });

            SetHandler(stage._outlet, onPull: () =>
            {
                if (_pendingResponses.TryDequeue(out var item))
                {
                    Push(_stage._outlet, item);
                    TryComplete();
                }
                else if (_stopping && _inFlightCount == 0)
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(_stage._inlet) && !_stopping)
                {
                    Pull(_stage._inlet);
                }
            }, onDownstreamFinish: _ =>
            {
                _stopping = true;
                CompleteStage();
            });
        }

        private const string IdleEvictionTimerKey = "idle-eviction";

        public override void PreStart()
        {
            _self = GetStageActor(OnMessage).Ref;
            _onResponseCallback = GetAsyncCallback<(string, int, IMemoryOwner<byte>, int)>(OnSubGraphResponse);
            _onSlotDeathCallback = GetAsyncCallback<(string, int, Exception?)>(OnSlotDeath);
            Pull(_stage._inlet);

            // Start periodic idle eviction timer
            ScheduleRepeatedly(IdleEvictionTimerKey,
                _stage._config.IdleCheckInterval,
                _stage._config.IdleCheckInterval);
        }

        private void HandlePush()
        {
            var elem = Grab(_stage._inlet);

            switch (elem.Item)
            {
                case ConnectItem connect:
                    RegisterHost(elem.PoolKey, connect);
                    Pull(_stage._inlet);
                    break;

                case DataItem data:
                    if (!_hostPools.ContainsKey(elem.PoolKey))
                    {
                        FailStage(new InvalidOperationException(
                            $"Received DataItem for unknown pool key '{elem.PoolKey}'. " +
                            $"A ConnectItem must be sent before any DataItem for a given pool key."));
                        return;
                    }

                    RouteDataItem(elem.PoolKey, data);
                    if (!_stopping)
                    {
                        Pull(_stage._inlet);
                    }
                    break;

                default:
                    FailStage(new InvalidOperationException(
                        $"Unexpected transport item type: {elem.Item?.GetType().Name ?? "null"}"));
                    break;
            }
        }

        private void RegisterHost(string poolKey, ConnectItem connect)
        {
            if (!_hostPools.ContainsKey(poolKey))
            {
                _hostPools[poolKey] = new HostPool(connect.Options);
            }
        }

        private void RouteDataItem(string poolKey, DataItem data)
        {
            var pool = _hostPools[poolKey];
            var slot = FindOrCreateSlot(poolKey, pool);

            if (slot is not null)
            {
                SendToSlot(poolKey, slot, data);
            }
            else
            {
                // All connections busy and at max — queue internally (backpressure)
                pool.PendingDataItems.Enqueue(data);
            }
        }

        private void SendToSlot(string poolKey, ConnectionSlot slot, DataItem data)
        {
            _inFlightCount++;
            slot.PendingRequestCount++;
            slot.Idle = false;
            slot.LastActivityUtc = DateTime.UtcNow;
            slot.Queue.OfferAsync(data);
        }

        /// <summary>
        /// Finds an idle connection using the configured strategy, or creates a new one if under the limit.
        /// Returns null when all connections are busy and the max has been reached (triggers queuing).
        /// </summary>
        private ConnectionSlot? FindOrCreateSlot(string poolKey, HostPool pool)
        {
            // 1. Always prefer an idle (non-busy) active connection — strategy selects which one
            var idleSlot = SelectIdleSlot(pool);
            if (idleSlot is not null)
            {
                return idleSlot;
            }

            // 2. If under the limit, materialise a new connection
            if (pool.Connections.Count < _stage._config.MaxConnectionsPerHost)
            {
                return MaterialiseNewSlot(poolKey, pool);
            }

            // 3. All connections busy and at max — return null to signal queuing
            return null;
        }

        /// <summary>
        /// Selects an idle connection using the configured load balancing strategy.
        /// </summary>
        private ConnectionSlot? SelectIdleSlot(HostPool pool)
        {
            return _stage._config.Strategy switch
            {
                LoadBalancingStrategy.LeastLoaded => SelectIdleLeastLoaded(pool),
                LoadBalancingStrategy.RoundRobin => SelectIdleRoundRobin(pool),
                _ => SelectIdleLeastLoaded(pool)
            };
        }

        /// <summary>
        /// LeastLoaded: among idle connections, selects the one with fewest pending requests.
        /// All idle connections have 0 pending, so this effectively picks the first idle one —
        /// but when requests arrive faster than responses, it distributes to the least loaded.
        /// </summary>
        private static ConnectionSlot? SelectIdleLeastLoaded(HostPool pool)
        {
            ConnectionSlot? best = null;
            var bestCount = int.MaxValue;
            foreach (var conn in pool.Connections)
            {
                if (conn.Active && conn.Idle && conn.PendingRequestCount < bestCount)
                {
                    best = conn;
                    bestCount = conn.PendingRequestCount;
                }
            }
            return best;
        }

        /// <summary>
        /// RoundRobin: cycles through idle connections in order, advancing the index each time.
        /// </summary>
        private static ConnectionSlot? SelectIdleRoundRobin(HostPool pool)
        {
            var connections = pool.Connections;
            if (connections.Count == 0)
            {
                return null;
            }

            for (var i = 0; i < connections.Count; i++)
            {
                var index = pool.RoundRobinIndex % connections.Count;
                pool.RoundRobinIndex = index + 1;
                var slot = connections[index];
                if (slot.Active && slot.Idle)
                {
                    return slot;
                }
            }

            return null;
        }

        private ConnectionSlot MaterialiseNewSlot(string poolKey, HostPool pool)
        {
            var connectionFlow = _stage._connectionFlowFactory();
            var capturedPoolKey = poolKey;
            var capturedCallback = _onResponseCallback!;
            var slotId = pool.ConnectionCounter;

            var (queue, completion) = Source.Queue<ITransportItem>(16, OverflowStrategy.Backpressure)
                .Via(connectionFlow)
                .ToMaterialized(
                    Sink.ForEach<(IMemoryOwner<byte>, int)>(chunk =>
                    {
                        capturedCallback((capturedPoolKey, slotId, chunk.Item1, chunk.Item2));
                    }),
                    Keep.Both)
                .Run(Materializer);

            var slot = new ConnectionSlot(slotId, queue, completion);
            pool.Connections.Add(slot);
            pool.ConnectionCounter++;

            // Watch for sub-graph failure to detect dead connections.
            // Only faults trigger the death callback; normal completions (during shutdown) are ignored.
            var capturedDeathCallback = _onSlotDeathCallback!;
            completion.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    capturedDeathCallback((capturedPoolKey, slotId, t.Exception?.GetBaseException()));
                }
            }, TaskScheduler.Default);

            // Send ConnectItem to initialise the connection in the sub-graph
            slot.Queue.OfferAsync(new ConnectItem(pool.Options));

            return slot;
        }

        /// <summary>
        /// Called (via async callback) when a materialised sub-graph faults.
        /// Marks the slot as dead, removes it, and schedules reconnection if within retry limits.
        /// </summary>
        private void OnSlotDeath((string poolKey, int slotId, Exception? exception) death)
        {
            if (_stopping)
            {
                return;
            }

            if (!_hostPools.TryGetValue(death.poolKey, out var pool))
            {
                return;
            }

            // Find and remove the dead slot
            ConnectionSlot? deadSlot = null;
            for (var i = pool.Connections.Count - 1; i >= 0; i--)
            {
                if (pool.Connections[i].Id == death.slotId)
                {
                    deadSlot = pool.Connections[i];
                    pool.Connections.RemoveAt(i);
                    break;
                }
            }

            if (deadSlot is null || !deadSlot.Active)
            {
                return; // Already cleaned up (e.g. during PostStop)
            }

            // Mark dead
            deadSlot.Active = false;

            // Adjust in-flight count for lost requests on the dead slot
            var lostRequests = deadSlot.PendingRequestCount;
            _inFlightCount -= lostRequests;

            // Check reconnect limits
            pool.ReconnectAttempts++;

            if (pool.ReconnectAttempts > _stage._config.MaxReconnectAttempts)
            {
                FailStage(new ConnectionPoolException(
                    $"Connection pool for '{death.poolKey}' exhausted {_stage._config.MaxReconnectAttempts} " +
                    $"reconnect attempts. Last error: {death.exception?.Message ?? "connection completed unexpectedly"}"));
                return;
            }

            // Schedule reconnection after the configured interval
            var timerKey = $"reconnect:{death.poolKey}";
            ScheduleOnce(timerKey, _stage._config.ReconnectInterval);
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is not string key)
            {
                return;
            }

            if (key == IdleEvictionTimerKey)
            {
                EvictIdleConnections();
                return;
            }

            if (!key.StartsWith("reconnect:"))
            {
                return;
            }

            var poolKey = key.Substring("reconnect:".Length);
            if (_stopping || !_hostPools.TryGetValue(poolKey, out var pool))
            {
                return;
            }

            // Materialise a replacement connection
            MaterialiseNewSlot(poolKey, pool);

            // Drain any pending items to the new slot
            DrainPendingQueue(poolKey, pool);

            // If outlet is waiting and we have no pending responses, pull inlet
            if (!HasBeenPulled(_stage._inlet) && !_stopping && IsAvailable(_stage._outlet) && _pendingResponses.Count == 0)
            {
                Pull(_stage._inlet);
            }
        }

        /// <summary>
        /// Scans all host pools and gracefully shuts down connections that have been idle
        /// longer than the configured timeout. Preserves at least one connection per host.
        /// A slot's <see cref="ConnectionSlot.KeepAliveTimeout"/> (from the server's Keep-Alive header)
        /// overrides <see cref="PoolConfig.IdleTimeout"/> when present.
        /// </summary>
        private void EvictIdleConnections()
        {
            if (_stopping)
            {
                return;
            }

            var now = DateTime.UtcNow;

            foreach (var (poolKey, pool) in _hostPools)
            {
                // Collect slots eligible for eviction (idle, active, timed out)
                var evictable = new List<ConnectionSlot>();

                foreach (var slot in pool.Connections)
                {
                    if (!slot.Active || !slot.Idle)
                    {
                        continue;
                    }

                    var timeout = slot.KeepAliveTimeout ?? _stage._config.IdleTimeout;
                    var idleDuration = now - slot.LastActivityUtc;

                    if (idleDuration > timeout)
                    {
                        evictable.Add(slot);
                    }
                }

                // Preserve at least one connection per host
                var activeCount = 0;
                foreach (var slot in pool.Connections)
                {
                    if (slot.Active)
                    {
                        activeCount++;
                    }
                }

                var maxEvictions = activeCount - 1;
                if (maxEvictions <= 0)
                {
                    continue;
                }

                var evicted = 0;
                foreach (var slot in evictable)
                {
                    if (evicted >= maxEvictions)
                    {
                        break;
                    }

                    slot.Active = false;
                    slot.Queue.Complete();
                    pool.Connections.Remove(slot);
                    evicted++;
                }
            }
        }

        private void OnSubGraphResponse((string poolKey, int slotId, IMemoryOwner<byte> memory, int length) response)
        {
            if (_stopping && _pendingResponses.Count == 0 && _inFlightCount <= 1)
            {
                // Stage is shutting down — still deliver this last response
            }

            _inFlightCount--;

            // Update the specific slot that produced this response
            if (_hostPools.TryGetValue(response.poolKey, out var pool))
            {
                foreach (var slot in pool.Connections)
                {
                    if (slot.Id == response.slotId && slot.PendingRequestCount > 0)
                    {
                        slot.PendingRequestCount--;
                        slot.LastActivityUtc = DateTime.UtcNow;
                        if (slot.PendingRequestCount == 0)
                        {
                            slot.Idle = true;
                        }
                        break;
                    }
                }

                // Drain queued items: dispatch to any now-idle slot
                DrainPendingQueue(response.poolKey, pool);
            }

            var item = new RoutedDataItem(response.poolKey, response.memory, response.length);

            if (IsAvailable(_stage._outlet))
            {
                Push(_stage._outlet, item);
                TryComplete();
            }
            else
            {
                _pendingResponses.Enqueue(item);
            }
        }

        /// <summary>
        /// Dispatches queued DataItems to idle connections for the given host.
        /// </summary>
        private void DrainPendingQueue(string poolKey, HostPool pool)
        {
            while (pool.PendingDataItems.Count > 0)
            {
                var slot = FindOrCreateSlot(poolKey, pool);
                if (slot is null)
                {
                    break; // Still all busy
                }

                var queued = pool.PendingDataItems.Dequeue();
                SendToSlot(poolKey, slot, queued);
            }
        }

        private void TryComplete()
        {
            if (_stopping && _inFlightCount == 0 && _pendingResponses.Count == 0)
            {
                CompleteStage();
            }
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            // Future use for actor-based sub-graph communication
        }

        public override void PostStop()
        {
            _stopping = true;

            foreach (var pool in _hostPools.Values)
            {
                foreach (var slot in pool.Connections)
                {
                    slot.Queue.Complete();
                    slot.Active = false;
                }
            }

            _hostPools.Clear();
        }
    }

    /// <summary>
    /// Per-host connection pool. Tracks TCP options, active connections, and connection count.
    /// </summary>
    private sealed class HostPool
    {
        public TcpOptions Options { get; }
        public List<ConnectionSlot> Connections { get; } = new();
        public int ConnectionCounter { get; set; }

        /// <summary>
        /// Round-robin index for cycling through connections. Used only when
        /// <see cref="LoadBalancingStrategy.RoundRobin"/> is active.
        /// </summary>
        public int RoundRobinIndex { get; set; }

        /// <summary>
        /// DataItems waiting for a free connection slot. Drained when a response arrives
        /// and a slot becomes idle, or when a new slot is materialised.
        /// </summary>
        public Queue<DataItem> PendingDataItems { get; } = new();

        /// <summary>
        /// Number of reconnect attempts for this host pool. Incremented each time a
        /// connection dies and a reconnect is attempted.
        /// </summary>
        public int ReconnectAttempts { get; set; }

        public HostPool(TcpOptions options)
        {
            Options = options;
        }
    }

    /// <summary>
    /// A single materialised connection within a host pool.
    /// Wraps the Source.Queue handle and tracks connection status.
    /// </summary>
    private sealed class ConnectionSlot
    {
        public int Id { get; }
        public ISourceQueueWithComplete<ITransportItem> Queue { get; }
        public Task Completion { get; }
        public bool Active { get; set; } = true;
        public bool Idle { get; set; } = true;
        public int PendingRequestCount { get; set; }

        /// <summary>
        /// UTC timestamp of last request or response activity on this slot.
        /// Updated on every <see cref="SendToSlot"/> and <see cref="OnSubGraphResponse"/>.
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Optional per-slot idle timeout override from the server's Keep-Alive header.
        /// When set, overrides <see cref="PoolConfig.IdleTimeout"/> for this slot.
        /// </summary>
        public TimeSpan? KeepAliveTimeout { get; set; }

        public ConnectionSlot(int id, ISourceQueueWithComplete<ITransportItem> queue, Task completion)
        {
            Id = id;
            Queue = queue;
            Completion = completion;
        }
    }
}
