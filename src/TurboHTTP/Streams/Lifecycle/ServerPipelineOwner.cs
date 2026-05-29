using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ServerPipelineOwner : ReceiveActor, IWithStash
{
    internal sealed record Initialize;
    internal sealed record PipelineReady(
        Sink<IFeatureCollection, NotUsed> RequestIngress,
        IResponseDispatcher<IFeatureCollection> Dispatcher);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Flow<IFeatureCollection, IFeatureCollection, NotUsed> _bridgeFlow;

    private ActorMaterializer? _materializer;
    private Sink<IFeatureCollection, NotUsed>? _requestIngress;
    private IResponseDispatcher<IFeatureCollection>? _dispatcher;
    private SharedKillSwitch? _killSwitch;

    public IStash Stash { get; set; } = null!;

    public ServerPipelineOwner(Flow<IFeatureCollection, IFeatureCollection, NotUsed> bridgeFlow)
    {
        _bridgeFlow = bridgeFlow;
        Initializing();
    }

    private void Initializing()
    {
        Receive<Initialize>(_ => MaterializePipeline());
    }

    private void Ready()
    {
        Receive<Initialize>(_ =>
        {
            Sender.Tell(new PipelineReady(_requestIngress!, _dispatcher!));
        });
    }

    protected override void PreStart()
    {
        _log.Debug("ServerPipelineOwner starting");
    }

    private void MaterializePipeline()
    {
        _log.Debug("Materializing server pipeline");

        try
        {
            var materializerSettings = ActorMaterializerSettings.Create(Context.System)
                .WithInputBuffer(initialSize: 32, maxSize: 128);
            _materializer = Context.System.Materializer(
                settings: materializerSettings,
                namePrefix: $"server-pipeline-{Self.Path.Name}");

            _killSwitch = KillSwitches.Shared($"server-{Self.Path.Name}");

            var requestIngressHub = MergeHub.Source<IFeatureCollection>(perProducerBufferSize: 64);
            var dispatcherHub = new ResponseDispatcherHub();

            var (requestIngress, dispatcher) = requestIngressHub
                .Via(_killSwitch.Flow<IFeatureCollection>())
                .Via(_bridgeFlow)
                .ToMaterialized(Sink.FromGraph(dispatcherHub), Keep.Both)
                .Run(_materializer);

            _requestIngress = requestIngress;
            _dispatcher = dispatcher;

            _log.Debug("Server pipeline materialized successfully");
            BecomeReady();
            Sender.Tell(new PipelineReady(_requestIngress!, _dispatcher!));
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to materialize server pipeline");
            CleanupResources();
            Context.Stop(Self);
        }
    }

    private void BecomeReady()
    {
        Become(Ready);
        Stash.UnstashAll();
    }

    private void CleanupResources()
    {
        _log.Debug("Cleaning up server pipeline resources");

        if (_killSwitch is not null)
        {
            try
            {
                _killSwitch.Shutdown();
            }
            catch (Exception ex)
            {
                _log.Warning("Error shutting down KillSwitch: {0}", ex.Message);
            }
        }

        if (_materializer is not null)
        {
            try
            {
                _materializer.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warning("Error disposing materializer: {0}", ex.Message);
            }

            _materializer = null;
        }

        _killSwitch = null;
        _dispatcher = null;
        _requestIngress = null;
    }

    protected override void PostStop()
    {
        _log.Debug("PostStop: cleaning up server pipeline resources");
        CleanupResources();
        base.PostStop();
    }
}
