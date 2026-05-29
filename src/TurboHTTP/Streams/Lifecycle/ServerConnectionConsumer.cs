using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class ServerConnectionConsumer : ReceiveActor
{
    internal sealed record SinkCompleted(Exception? Error);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly int _connectionId;
    private readonly ChannelReader<IFeatureCollection> _requestReader;
    private readonly ChannelWriter<IFeatureCollection> _responseWriter;
    private readonly Sink<IFeatureCollection, NotUsed> _requestIngress;
    private readonly Source<IFeatureCollection, NotUsed> _responseFanoutSource;
    private readonly IMaterializer _materializer;

    private UniqueKillSwitch? _responseKillSwitch;

    public static Props Props(
        int connectionId,
        ChannelReader<IFeatureCollection> requestReader,
        ChannelWriter<IFeatureCollection> responseWriter,
        Sink<IFeatureCollection, NotUsed> requestIngress,
        Source<IFeatureCollection, NotUsed> responseFanoutSource,
        IMaterializer materializer)
        => Akka.Actor.Props.CreateBy(new ProducerFactory(
            connectionId, requestReader, responseWriter,
            requestIngress, responseFanoutSource));

    private sealed class ProducerFactory(
        int connectionId,
        ChannelReader<IFeatureCollection> requestReader,
        ChannelWriter<IFeatureCollection> responseWriter,
        Sink<IFeatureCollection, NotUsed> requestIngress,
        Source<IFeatureCollection, NotUsed> responseFanoutSource) : IIndirectActorProducer
    {
        public Type ActorType => typeof(ServerConnectionConsumer);

        public ActorBase Produce() => new ServerConnectionConsumer(
            connectionId, requestReader, responseWriter,
            requestIngress, responseFanoutSource);

        public void Release(ActorBase actor)
        {
        }
    }

    private ServerConnectionConsumer(
        int connectionId,
        ChannelReader<IFeatureCollection> requestReader,
        ChannelWriter<IFeatureCollection> responseWriter,
        Sink<IFeatureCollection, NotUsed> requestIngress,
        Source<IFeatureCollection, NotUsed> responseFanoutSource)
    {
        _connectionId = connectionId;
        _requestReader = requestReader;
        _responseWriter = responseWriter;
        _requestIngress = requestIngress;
        _responseFanoutSource = responseFanoutSource;
        _materializer = Context.Materializer();

        Receive<SinkCompleted>(HandleSinkCompleted);
    }

    protected override void PreStart()
    {
        MaterializeRequestIngress();
        MaterializeResponseEgress();
    }

    private void MaterializeRequestIngress()
    {
        var connId = _connectionId;

        ChannelSource.FromReader(_requestReader)
            .Select(features =>
            {
                features.Set(new ConnectionRoutingFeature { ConnectionId = connId });
                return features;
            })
            .RunWith(_requestIngress, _materializer);
    }

    private void MaterializeResponseEgress()
    {
        var writer = _responseWriter;
        var (killSwitch, completionTask) = _responseFanoutSource
            .ViaMaterialized(KillSwitches.Single<IFeatureCollection>(), Keep.Right)
            .ToMaterialized(
                Sink.ForEach<IFeatureCollection>(response =>
                {
                    writer.TryWrite(response);
                }),
                Keep.Both)
            .Run(_materializer);

        _responseKillSwitch = killSwitch;

        completionTask.PipeTo(Self, Self,
            () => new SinkCompleted(null),
            ex => new SinkCompleted(ex.GetBaseException()));
    }

    private void HandleSinkCompleted(SinkCompleted completed)
    {
        _responseKillSwitch = null;
        if (completed.Error is not null and not OperationCanceledException)
        {
            _log.Warning("ServerConnectionConsumer {0} sink completed with error: {1}",
                _connectionId, completed.Error.Message);
        }
    }

    protected override void PostStop()
    {
        if (_responseKillSwitch is null)
        {
            return;
        }

        _responseKillSwitch.Abort(new OperationCanceledException("Connection closed"));
        _responseKillSwitch = null;
    }
}
