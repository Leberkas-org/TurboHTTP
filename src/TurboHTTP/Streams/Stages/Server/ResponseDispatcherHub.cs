using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal interface IResponseDispatcher<T>
{
    Source<T, NotUsed> Subscribe(int connectionId);
}

internal sealed class ResponseDispatcherHub
    : GraphStageWithMaterializedValue<SinkShape<IFeatureCollection>, IResponseDispatcher<IFeatureCollection>>
{
    private readonly Inlet<IFeatureCollection> _in = new("ResponseDispatcher.In");

    public override SinkShape<IFeatureCollection> Shape { get; }

    public ResponseDispatcherHub()
    {
        Shape = new SinkShape<IFeatureCollection>(_in);
    }

    public override ILogicAndMaterializedValue<IResponseDispatcher<IFeatureCollection>>
        CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
    {
        var sinkActorTcs = new TaskCompletionSource<IActorRef>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var logic = new DispatcherLogic(this, sinkActorTcs);
        var dispatcher = new ResponseDispatcherImpl(sinkActorTcs.Task);
        return new LogicAndMaterializedValue<IResponseDispatcher<IFeatureCollection>>(logic, dispatcher);
    }

    private sealed record Register(int ConnectionId, IActorRef SourceActor);

    private sealed record Unregister(int ConnectionId);

    private sealed record Deliver(IFeatureCollection Element);

    private sealed record HubCompleted(Exception? Failure);

    private sealed class DispatcherLogic : GraphStageLogic
    {
        private readonly ResponseDispatcherHub _hub;
        private readonly TaskCompletionSource<IActorRef> _sinkActorTcs;
        private readonly Dictionary<int, IActorRef> _consumers = [];
        private readonly Dictionary<int, List<IFeatureCollection>> _pending = [];
        private IActorRef? _sinkActor;

        public DispatcherLogic(
            ResponseDispatcherHub hub,
            TaskCompletionSource<IActorRef> sinkActorTcs) : base(hub.Shape)
        {
            _hub = hub;
            _sinkActorTcs = sinkActorTcs;

            SetHandler(hub._in,
                onPush: OnPush,
                onUpstreamFinish: () =>
                {
                    foreach (var consumer in _consumers.Values)
                    {
                        consumer.Tell(new HubCompleted(null));
                    }

                    CompleteStage();
                },
                onUpstreamFailure: ex =>
                {
                    foreach (var consumer in _consumers.Values)
                    {
                        consumer.Tell(new HubCompleted(ex));
                    }

                    FailStage(ex);
                });
        }

        public override void PreStart()
        {
            _sinkActor = GetStageActor(OnHubMessage).Ref;
            _sinkActorTcs.SetResult(_sinkActor);
            Pull(_hub._in);
        }

        private void OnPush()
        {
            var element = Grab(_hub._in);
            var routingFeature = element.Get<ConnectionRoutingFeature>();

            if (routingFeature is not null)
            {
                var id = routingFeature.ConnectionId;
                if (_consumers.TryGetValue(id, out var sourceActor))
                {
                    sourceActor.Tell(new Deliver(element));
                }
                else
                {
                    if (!_pending.TryGetValue(id, out var list))
                    {
                        list = [];
                        _pending[id] = list;
                    }

                    list.Add(element);
                }
            }

            Pull(_hub._in);
        }

        private void OnHubMessage((IActorRef sender, object msg) args)
        {
            switch (args.msg)
            {
                case Register(var id, var sourceActor):
                    _consumers[id] = sourceActor;
                    if (_pending.Remove(id, out var buffered))
                    {
                        foreach (var element in buffered)
                        {
                            sourceActor.Tell(new Deliver(element));
                        }
                    }

                    break;
                case Unregister(var id):
                    _consumers.Remove(id);
                    _pending.Remove(id);
                    break;
            }
        }
    }

    private sealed class ResponseDispatcherImpl(Task<IActorRef> sinkActorTask) : IResponseDispatcher<IFeatureCollection>
    {
        public Source<IFeatureCollection, NotUsed> Subscribe(int connectionId)
        {
            return Source.FromGraph(new DispatcherSourceStage(sinkActorTask, connectionId));
        }
    }

    private sealed class DispatcherSourceStage : GraphStage<SourceShape<IFeatureCollection>>
    {
        private readonly Task<IActorRef> _sinkActorTask;
        private readonly int _connectionId;
        private readonly Outlet<IFeatureCollection> _out = new("ResponseDispatcher.Source.Out");

        public override SourceShape<IFeatureCollection> Shape { get; }

        public DispatcherSourceStage(Task<IActorRef> sinkActorTask, int connectionId)
        {
            _sinkActorTask = sinkActorTask;
            _connectionId = connectionId;
            Shape = new SourceShape<IFeatureCollection>(_out);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new SourceLogic(this);

        private sealed record SinkActorReady(IActorRef SinkActor);

        private sealed class SourceLogic : GraphStageLogic
        {
            private readonly DispatcherSourceStage _stage;
            private IActorRef? _sourceActor;
            private IActorRef? _sinkActor;
            private IFeatureCollection? _buffered;
            private bool _downstreamReady;

            public SourceLogic(DispatcherSourceStage stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._out, onPull: () =>
                {
                    if (_buffered is { } element)
                    {
                        _buffered = null;
                        Push(_stage._out, element);
                    }
                    else
                    {
                        _downstreamReady = true;
                    }
                });
            }

            public override void PreStart()
            {
                _sourceActor = GetStageActor(OnSourceMessage).Ref;
                _stage._sinkActorTask.PipeTo(_sourceActor,
                    success: sinkRef => new SinkActorReady(sinkRef));
            }

            private void OnSourceMessage((IActorRef sender, object msg) args)
            {
                switch (args.msg)
                {
                    case SinkActorReady(var sinkActor):
                        _sinkActor = sinkActor;
                        sinkActor.Tell(new Register(_stage._connectionId, _sourceActor!));
                        break;

                    case Deliver(var element):
                        if (_downstreamReady)
                        {
                            _downstreamReady = false;
                            Push(_stage._out, element);
                        }
                        else
                        {
                            _buffered = element;
                        }

                        break;

                    case HubCompleted(var failure):
                        if (failure is not null)
                        {
                            FailStage(failure);
                        }
                        else
                        {
                            CompleteStage();
                        }

                        break;
                }
            }

            public override void PostStop()
            {
                _sinkActor?.Tell(new Unregister(_stage._connectionId));
            }
        }
    }
}