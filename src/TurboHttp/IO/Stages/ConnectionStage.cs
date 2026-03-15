using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

public sealed class ConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private IActorRef PoolRouter { get; }

    private readonly Inlet<IOutputItem> _inlet = new("pool.in");
    private readonly Outlet<IInputItem> _outlet = new("pool.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public ConnectionStage(IActorRef poolRouter)
    {
        PoolRouter = poolRouter;
        Shape = new FlowShape<IOutputItem, IInputItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionStage _stage;
        private readonly Queue<IInputItem> _pendingReads = new();

        private ISourceQueueWithComplete<DataItem>? _globalRequestQueue;

        private Action<IInputItem>? _onResponse;
        private Action? _onOfferDone;
        private StageActor? _stageActor;

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _globalRequestQueue?.Complete();
                    CompleteStage();
                });

            SetHandler(stage._outlet,
                onPull: () =>
                {
                    if (_pendingReads.TryDequeue(out var item))
                    {
                        Push(_stage._outlet, item);
                    }
                },
                onDownstreamFinish: _ =>
                {
                    _globalRequestQueue?.Complete();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            _onResponse = GetAsyncCallback<IInputItem>(item =>
            {
                if (IsAvailable(_stage._outlet))
                {
                    Push(_stage._outlet, item);
                }
                else
                {
                    _pendingReads.Enqueue(item);
                }
            });

            _onOfferDone = GetAsyncCallback(() =>
            {
                if (!IsClosed(_stage._inlet) && !HasBeenPulled(_stage._inlet))
                {
                    Pull(_stage._inlet);
                }
            });

            _stageActor = GetStageActor(OnMessage);

            // Ask for global refs — do NOT pull until we receive them.
            _stage.PoolRouter.Tell(new PoolRouterActor.GetGlobalRefs(), _stageActor.Ref);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is not PoolRouterActor.GlobalRefs refs)
            {
                return;
            }

            _globalRequestQueue = refs.RequestQueue;

            // Subscribe to the global aggregated response stream.
            refs.ResponseSource.RunWith(
                Sink.ForEach<DataItem>(item => _onResponse!(item)),
                Materializer);

            // Now ready to process items.
            Pull(_stage._inlet);
        }

        private void HandlePush()
        {
            var item = Grab(_stage._inlet);

            if (item is ConnectItem connect)
            {
                // Ensure a HostPoolActor exists for this host (fire-and-forget, no reply).
                _stage.PoolRouter.Tell(new PoolRouterActor.EnsureHost(connect.Key, connect.Options));

                // Pull immediately — no need to wait for a reply.
                Pull(_stage._inlet);
                return;
            }

            if (item is DataItem dataItem)
            {
                _ = _globalRequestQueue!
                    .OfferAsync(dataItem)
                    .ContinueWith(_ => _onOfferDone!(), TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        public override void PostStop()
        {
            _globalRequestQueue?.Complete();
        }
    }
}