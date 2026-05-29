using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ServerPipelineOwner : ReceiveActor, IWithStash
{
    internal sealed record Initialize;
    internal sealed record PipelineReady;
    internal sealed record RegisterConnection(int ConnectionId);
    internal sealed record ConnectionRegistered(
        Sink<IFeatureCollection, NotUsed> RequestIngress,
        Source<IFeatureCollection, NotUsed> ResponseFanoutSource);
    internal sealed record UnregisterConnection(int ConnectionId);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly Flow<IFeatureCollection, IFeatureCollection, NotUsed> _bridgeFlow;

    private ActorMaterializer? _materializer;
    private Sink<IFeatureCollection, NotUsed>? _requestIngress;
    private Source<IFeatureCollection, NotUsed>? _responseFanoutSource;
    private SharedKillSwitch? _killSwitch;
    private readonly Dictionary<int, int> _connectionPartitions = [];
    private int _nextPartitionIndex;

    public IStash Stash { get; set; } = null!;

    public ServerPipelineOwner(Flow<IFeatureCollection, IFeatureCollection, NotUsed> bridgeFlow)
    {
        _bridgeFlow = bridgeFlow;
        Initializing();
    }

    private void Initializing()
    {
        Receive<Initialize>(_ => MaterializePipeline());
        Receive<RegisterConnection>(_ => Stash.Stash());
        Receive<UnregisterConnection>(_ => Stash.Stash());
    }

    private void Ready()
    {
        Receive<Initialize>(_ =>
        {
            Sender.Tell(new PipelineReady());
        });
        Receive<RegisterConnection>(HandleRegisterConnection);
        Receive<UnregisterConnection>(HandleUnregisterConnection);
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
            var responseFanoutHub = PartitionHub.Sink<IFeatureCollection>(
                partitioner: ResolveResponsePartition,
                startAfterNrOfConsumers: 1,
                bufferSize: 256);

            var (requestIngress, fanoutSource) = requestIngressHub
                .Via(_killSwitch.Flow<IFeatureCollection>())
                .Via(_bridgeFlow)
                .ToMaterialized(responseFanoutHub, Keep.Both)
                .Run(_materializer);

            _requestIngress = requestIngress;
            _responseFanoutSource = fanoutSource;

            _log.Debug("Server pipeline materialized successfully");
            BecomeReady();
            Sender.Tell(new PipelineReady());
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

    private void HandleRegisterConnection(RegisterConnection message)
    {
        _connectionPartitions[message.ConnectionId] = _nextPartitionIndex++;
        Sender.Tell(new ConnectionRegistered(_requestIngress!, _responseFanoutSource!));
    }

    private void HandleUnregisterConnection(UnregisterConnection message)
    {
        _connectionPartitions.Remove(message.ConnectionId);
    }

    private int ResolveResponsePartition(int consumerCount, IFeatureCollection features)
    {
        var routing = features.Get<ConnectionRoutingFeature>();
        if (routing is not null
            && _connectionPartitions.TryGetValue(routing.ConnectionId, out var partition)
            && partition >= 0
            && partition < consumerCount)
        {
            return partition;
        }

        return 0;
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
        _responseFanoutSource = null;
        _requestIngress = null;
        _connectionPartitions.Clear();
        _nextPartitionIndex = 0;
    }

    protected override void PostStop()
    {
        _log.Debug("PostStop: cleaning up server pipeline resources");
        CleanupResources();
        base.PostStop();
    }
}
