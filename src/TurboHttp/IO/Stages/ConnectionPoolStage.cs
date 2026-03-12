using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

/// <summary>
/// Thin GraphStage bridge between Akka.Streams and the actor-based connection pool.
/// Routes <see cref="RoutedTransportItem"/> requests to the <see cref="PoolRouterActor"/>
/// and pushes <see cref="RoutedDataItem"/> responses back to the stream.
/// </summary>
public sealed class ConnectionPoolStage : GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>
{
    private readonly IActorRef _router;

    private readonly Inlet<RoutedTransportItem> _inlet = new("connectionpool.in");
    private readonly Outlet<RoutedDataItem> _outlet = new("connectionpool.out");

    public override FlowShape<RoutedTransportItem, RoutedDataItem> Shape { get; }

    public ConnectionPoolStage(IActorRef router)
    {
        _router = router;
        Shape = new FlowShape<RoutedTransportItem, RoutedDataItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionPoolStage _stage;

        private readonly Queue<RoutedDataItem> _responses = new();

        private IActorRef _stageActorRef = ActorRefs.Nobody;

        public Logic(ConnectionPoolStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(_stage._inlet, onPush: () =>
            {
                var item = Grab(_stage._inlet);

                if (item.Item is DataItem data)
                {
                    _stage._router.Tell(
                        new PoolRouterActor.SendRequest(
                            item.PoolKey,
                            data,
                            _stageActorRef));
                }

                Pull(_stage._inlet);
            });

            SetHandler(_stage._outlet, onPull: PushIfAvailable);
        }

        public override void PreStart()
        {
            var stageActor = GetStageActor(OnMessage);
            _stageActorRef = stageActor.Ref;
            Pull(_stage._inlet);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is PoolRouterActor.Response resp)
            {
                _responses.Enqueue(
                    new RoutedDataItem(
                        resp.PoolKey,
                        resp.Memory,
                        resp.Length));

                PushIfAvailable();
            }
        }

        private void PushIfAvailable()
        {
            if (_responses.Count > 0 && IsAvailable(_stage._outlet))
            {
                Push(_stage._outlet, _responses.Dequeue());
            }
        }
    }
}
