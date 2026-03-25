using System;
using System.Collections.Generic;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;

namespace TurboHttp.Transport;

/// <summary>
/// Unified transport stage for ALL HTTP versions.
/// Delegates all transport-specific logic to an <see cref="ITransportHandler"/> created lazily
/// on the first <see cref="ConnectItem"/>: <see cref="TcpTransportHandler"/> for HTTP/1.x and HTTP/2,
/// <see cref="QuicTransportHandler"/> for HTTP/3.
/// </summary>
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

    private sealed class Logic : TimerGraphStageLogic, IStageCallbacks
    {
        /// <summary>Timer key for connection acquisition timeout.</summary>
        private const string ConnectTimerKey = "connect-timeout";

        private readonly ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        /// <summary>Active transport handler — null until the first <see cref="ConnectItem"/> arrives.</summary>
        private ITransportHandler? _transport;

        /// <summary>
        /// DataItems buffered before the first <see cref="ConnectItem"/> arrives
        /// (e.g. HTTP/2 connection preface emitted by <c>PrependPrefaceStage</c> racing ahead of ConnectItem).
        /// Flushed to <see cref="_transport"/> immediately after it is initialised.
        /// </summary>
        private readonly Queue<DataItem> _pendingWrites = new();

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    if (_transport is not null)
                    {
                        _transport.OnUpstreamFinished();
                    }
                    else
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
                    _transport?.Cleanup();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            Pull(_stage._in);
        }

        private void HandlePush()
        {
            var item = Grab(_stage._in);

            switch (item)
            {
                case ConnectItem connect:
                    // Clean up prior transport if a new ConnectItem arrives (defensive: rarely occurs in practice).
                    _transport?.Cleanup();
                    _transport = connect.Options is QuicOptions
                        ? new QuicTransportHandler()
                        : new TcpTransportHandler(_stage.Pool);
                    _transport.Initialize(this);
                    // Flush DataItems buffered before this ConnectItem arrived
                    // (e.g. HTTP/2 preface from PrependPrefaceStage racing ahead of ConnectItem).
                    // The transport's own _pendingWrites queue will hold them until the connection is ready.
                    while (_pendingWrites.TryDequeue(out var buffered))
                    {
                        _transport.HandleDataItem(buffered);
                    }
                    _transport.HandleConnectItem(connect);
                    // Do NOT pull — wait for ConnectionLease before accepting data.
                    break;

                case Http3TaggedItem tagged:
                    if (_transport is null) { Log.Warning("ConnectionStage: {0} received without prior ConnectItem — dropping.", nameof(Http3TaggedItem)); Pull(_stage._in); break; }
                    _transport.HandleTaggedItem(tagged);
                    break;

                case DataItem dataItem:
                    if (_transport is null)
                    {
                        // Buffer data that arrives before the ConnectItem
                        // (e.g. HTTP/2 preface from PrependPrefaceStage racing ahead of ConnectItem).
                        _pendingWrites.Enqueue(dataItem);
                        if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
                        {
                            Pull(_stage._in);
                        }
                        break;
                    }
                    _transport.HandleDataItem(dataItem);
                    break;

                case ConnectionReuseItem reuseItem:
                    if (_transport is null) { Log.Warning("ConnectionStage: {0} received without prior ConnectItem — dropping.", nameof(ConnectionReuseItem)); Pull(_stage._in); break; }
                    _transport.HandleConnectionReuseItem(reuseItem);
                    break;

                case MaxConcurrentStreamsItem maxStreams:
                    if (_transport is null) { Log.Warning("ConnectionStage: {0} received without prior ConnectItem — dropping.", nameof(MaxConcurrentStreamsItem)); Pull(_stage._in); break; }
                    _transport.HandleMaxConcurrentStreamsItem(maxStreams);
                    break;

                case StreamAcquireItem acquireItem:
                    if (_transport is null) { Log.Warning("ConnectionStage: {0} received without prior ConnectItem — dropping.", nameof(StreamAcquireItem)); Pull(_stage._in); break; }
                    _transport.HandleStreamAcquireItem(acquireItem);
                    break;
            }
        }

        protected override void OnTimer(object timerKey)
        {
            if (timerKey is ConnectTimerKey)
            {
                _transport?.OnConnectTimeout();
            }
        }

        public override void PostStop()
        {
            CancelTimer(ConnectTimerKey);
            _transport?.Cleanup();
            // Dispose any DataItems buffered before a ConnectItem arrived.
            while (_pendingWrites.TryDequeue(out var orphan))
            {
                orphan.Memory.Dispose();
            }
        }

        // ── IStageCallbacks implementation ──

        void IStageCallbacks.PushOutput(IInputItem item)
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

        void IStageCallbacks.SignalPullInput()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        bool IStageCallbacks.IsOutputAvailable() => IsAvailable(_stage._out);

        bool IStageCallbacks.IsInputClosed() => IsClosed(_stage._in);

        bool IStageCallbacks.HasInputBeenPulled() => HasBeenPulled(_stage._in);

        void IStageCallbacks.ScheduleConnectTimeout(TimeSpan timeout)
            => ScheduleOnce(ConnectTimerKey, timeout);

        void IStageCallbacks.CancelConnectTimeout()
            => CancelTimer(ConnectTimerKey);

        void IStageCallbacks.RequestCompleteStage()
            => CompleteStage();

        void IStageCallbacks.LogWarning(string format, params object[] args)
            => Log.Warning(format, args);

        Action<T> IStageCallbacks.GetAsyncCallback<T>(Action<T> handler)
            => GetAsyncCallback(handler);

        Action IStageCallbacks.GetAsyncCallback(Action handler)
            => GetAsyncCallback(handler);

        void IStageCallbacks.ClearPendingOutput()
            => _pendingReads.Clear();
    }
}
