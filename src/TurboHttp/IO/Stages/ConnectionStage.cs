using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

public record struct HostKey
{
    public static HostKey Default => new() { Host = string.Empty, Port = ushort.MinValue, Schema = string.Empty };
    public required string Schema { get; init; }
    public required string Host { get; init; }
    public required ushort Port { get; init; }
}

public interface ITransportItem
{
    HostKey Key => HostKey.Default;
}

public interface IDataItem
{
    HostKey Key => HostKey.Default;
    IMemoryOwner<byte> Memory { get; }
    int Length { get; }
}

public record ConnectItem(TcpOptions Options) : ITransportItem;

public record DataItem(IMemoryOwner<byte> Memory, int Length, bool IsTls = false) : ITransportItem, IDataItem;

/// <summary>
/// Pure stream bridge between the Akka.Streams pipeline and the actor-based
/// connection pool. Obtains a <see cref="PoolRouterActor.PoolRefs"/> from
/// <paramref name="poolRouter"/> on start, then:
/// <list type="bullet">
///   <item>Inlet → <c>ISinkRef&lt;ITransportItem&gt;.Sink</c> (requests to pool)</item>
///   <item><c>ISourceRef&lt;IDataItem&gt;.Source</c> → Outlet (responses from pool)</item>
/// </list>
/// Contains zero references to TCP infrastructure (ClientManager, Channel, etc.).
/// </summary>
public sealed class ConnectionStage : GraphStage<FlowShape<ITransportItem, IDataItem>>
{
    private IActorRef PoolRouter { get; }

    private readonly Inlet<ITransportItem> _inlet = new("pool.in");
    private readonly Outlet<IDataItem> _outlet = new("pool.out");

    public override FlowShape<ITransportItem, IDataItem> Shape { get; }

    public ConnectionStage(IActorRef poolRouter)
    {
        PoolRouter = poolRouter;
        Shape = new FlowShape<ITransportItem, IDataItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionStage _stage;
        private readonly Queue<IDataItem> _pendingReads = new();

        private ISourceQueueWithComplete<ITransportItem>? _requestQueue;
        private Action<IDataItem>? _onResponse;
        private Action? _onOfferDone;

        public Logic(ConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: HandlePush,
                onUpstreamFinish: () =>
                {
                    _requestQueue?.Complete();
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
                    _requestQueue?.Complete();
                    CompleteStage();
                });
        }

        public override void PreStart()
        {
            // Callbacks created once, reused for every push/offer
            _onResponse = GetAsyncCallback<IDataItem>(item =>
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

            // Requests: inlet → Source.Queue → sinkRef.Sink
            var queue = Source.Queue<ITransportItem>(256, OverflowStrategy.Backpressure)
                .ToMaterialized(refs.Sink.Sink, Keep.Left)
                .Run(mat);
            _requestQueue = queue;

            // Responses: sourceRef.Source → GetAsyncCallback → outlet
            refs.Source.Source.RunWith(
                Sink.ForEach<IDataItem>(item => _onResponse!(item)),
                mat);

            // Ready to receive
            Pull(_stage._inlet);
        }

        private void HandlePush()
        {
            var item = Grab(_stage._inlet);
            _ = _requestQueue!.OfferAsync(item).ContinueWith(
                _ => _onOfferDone!(),
                TaskContinuationOptions.ExecuteSynchronously);
        }

        public override void PostStop()
        {
            _requestQueue?.Complete();

            while (_pendingReads.TryDequeue(out var item))
            {
                item.Memory.Dispose();
            }
        }
    }
}
