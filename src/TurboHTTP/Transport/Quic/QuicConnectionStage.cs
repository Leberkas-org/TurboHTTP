using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Tcp;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Transport stage for HTTP/3 (QUIC). Manages multi-stream I/O (request, control, encoder),
/// tagged item routing, and multiple inbound pumps. Connection lifecycle (pooling, reuse,
/// eviction) is handled by <see cref="TurboHTTP.Transport.Connection.QuicConnectionManagerActor"/>.
/// </summary>
internal sealed class QuicConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Inlet<IOutputItem> _in = new("QuicConnection.In");
    private readonly Outlet<IInputItem> _out = new("QuicConnection.Out");

    private readonly IActorRef _connectionManager;
    private readonly bool _allowConnectionMigration;

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public QuicConnectionStage(IActorRef connectionManager, bool allowConnectionMigration = true)
    {
        _connectionManager = connectionManager;
        _allowConnectionMigration = allowConnectionMigration;
        Shape = new FlowShape<IOutputItem, IInputItem>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : TimerGraphStageLogic, ITransportOperations
    {
        private readonly QuicConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();
        private QuicTransportStateMachine _sm = null!;

        public Logic(QuicConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;
            
            SetHandler(stage._in,
                onPush: () => _sm.HandlePush(Grab(stage._in)),
                onUpstreamFinish: () => _sm.HandleUpstreamFinish());

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
                    _sm.HandleDownstreamFinish();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            var stageActor = GetStageActor(OnReceive);
            _sm = new QuicTransportStateMachine(this, stageActor.Ref, _stage._connectionManager,
                _stage._allowConnectionMigration);
            Pull(_stage._in);
        }

        private void OnReceive((IActorRef sender, object message) args)
        {
            if (args.message is IQuicTransportEvent evt)
            {
                _sm.Dispatch(evt);
            }
        }

        protected override void OnTimer(object timerKey)
            => _sm.OnTimer(timerKey as string);

        public override void PostStop() => _sm.PostStop();

        void ITransportOperations.OnPushOutput(IInputItem item)
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

        void ITransportOperations.OnSignalPullInput()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        void ITransportOperations.OnCompleteStage() => CompleteStage();

        void ITransportOperations.OnScheduleTimer(string key, TimeSpan delay) => ScheduleOnce(key, delay);

        void ITransportOperations.OnCancelTimer(string key) => CancelTimer(key);

        ILoggingAdapter ITransportOperations.Log => Log;
    }
}
