using System;
using System.Collections.Generic;
using System.Threading.Channels;
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

        private ChannelWriter<IOutputItem>? _requestWriter;
        private Action<IInputItem>? _onResponse;
        private Action? _onOfferDone;
        private HostKey _hostKey;

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _requestWriter?.TryComplete();
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
                    _requestWriter?.TryComplete();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            // Callbacks created once, reused for every push/offer
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

            // Ask PoolRouter for refs via stage actor (avoids needing an external actor for PipeTo)
            var stageActor = GetStageActor(OnMessage);
            _stage.PoolRouter.Tell(new PoolRouterActor.GetPoolRefs(), stageActor.Ref);
            // inlet pull is deferred until PoolRefs arrive in OnMessage
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is not PoolRouterActor.PoolRefs refs)
            {
                return;
            }

            var mat = Materializer;
            var channel = Channel.CreateUnbounded<IOutputItem>();
            _requestWriter = channel.Writer;

            // Requests: inlet → ChannelSource.FromReader → sinkRef.Sink
            ChannelSource.FromReader(channel.Reader)
                .ToMaterialized(refs.Sink.Sink, Keep.Left)
                .Run(mat);

            // Responses: sourceRef.Source → GetAsyncCallback → outlet
            refs.Source.Source.RunWith(
                Sink.ForEach<DataItem>(item => _onResponse!(item)),
                mat);

            // Ready to receive
            Pull(_stage._inlet);
        }

        private void HandlePush()
        {
            var item = Grab(_stage._inlet);

            if (item is DataItem data && data.Key.Equals(HostKey.Default))
            {
                item = data with { Key = _hostKey };
            }

            _ = _requestWriter!.WriteAsync(item).AsTask().ContinueWith(_ => _onOfferDone!(),
                TaskContinuationOptions.ExecuteSynchronously);
        }

        public override void PostStop()
        {
            _requestWriter?.TryComplete();
        }
    }
}