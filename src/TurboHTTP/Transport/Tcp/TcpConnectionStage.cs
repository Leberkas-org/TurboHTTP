using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Transport.Tcp;

/// <summary>
/// Transport stage for HTTP/1.0, HTTP/1.1, and HTTP/2 (TCP/TLS).
/// Owns the I/O pumps (zero-copy, no thread hop) and delegates connection lifecycle
/// (acquire, release, idle reuse, eviction) to the <see cref="ConnectionManagerActor"/>.
/// <para>
/// All transport state and logic is encapsulated in <see cref="TcpTransportStateMachine"/>.
/// Async events are marshaled through <see cref="GraphStageLogic.GetStageActor"/> +
/// <see cref="IActorRef.Tell(object)"/> — no <c>GetAsyncCallback</c> is used.
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

    private sealed class Logic : TimerGraphStageLogic, ITcpTransportOperations
    {
        private readonly TcpConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();
        private TcpTransportStateMachine _sm = null!;

        public Logic(TcpConnectionStage stage) : base(stage.Shape)
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
            _sm = new TcpTransportStateMachine(
                this,
                _stage.ConnectionManager,
                _stage.ClientOptions,
                stageActor.Ref);
            Pull(_stage._in);
        }

        private void OnReceive((IActorRef sender, object message) args)
        {
            if (args.message is TcpTransportEvent evt)
            {
                _sm.Dispatch(evt);
            }
        }

        protected override void OnTimer(object timerKey)
            => _sm.OnTimer(timerKey as string);

        public override void PostStop() => _sm.PostStop();

        // ─── ITcpTransportOperations ───

        void ITcpTransportOperations.OnPushOutput(IInputItem item)
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

        void ITcpTransportOperations.OnSignalPullInput()
        {
            if (!IsClosed(_stage._in) && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        void ITcpTransportOperations.OnFailStage(Exception ex) => FailStage(ex);

        void ITcpTransportOperations.OnCompleteStage() => CompleteStage();

        void ITcpTransportOperations.OnScheduleTimer(string key, TimeSpan delay)
            => ScheduleOnce(key, delay);

        void ITcpTransportOperations.OnCancelTimer(string key) => CancelTimer(key);

        ILoggingAdapter ITcpTransportOperations.Log => Log;
    }
}
