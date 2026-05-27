using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Client;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Client;

namespace TurboHTTP.Streams.Lifecycle;

internal sealed class Consumer : ReceiveActor
{
    internal sealed record ConsumerSinkCompleted(Exception? Error);

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IMaterializer _materializer = Context.Materializer();
    private readonly Guid _consumerId;
    private readonly ChannelReader<HttpRequestMessage> _requestReader;
    private readonly Func<TurboRequestOptions> _optionsFactory;
    private readonly ChannelWriter<HttpResponseMessage> _responseEgress;
    private readonly Sink<HttpRequestMessage, NotUsed> _requestIngress;
    private readonly Source<HttpResponseMessage, NotUsed> _responseFanoutSource;

    private UniqueKillSwitch? _sinkKillSwitch;

    public static Props Props(
        Guid consumerId,
        ChannelReader<HttpRequestMessage> requestReader,
        Func<TurboRequestOptions> optionsFactory,
        ChannelWriter<HttpResponseMessage> responseWriter,
        Sink<HttpRequestMessage, NotUsed> requestIngress,
        Source<HttpResponseMessage, NotUsed> responseFanoutSource,
        IMaterializer materializer) => Akka.Actor.Props.CreateBy(new ConsumerActorProducer(
            consumerId, requestReader, optionsFactory,
            responseWriter, requestIngress,
            responseFanoutSource));

    private sealed class ConsumerActorProducer(
        Guid consumerId,
        ChannelReader<HttpRequestMessage> requestReader,
        Func<TurboRequestOptions> optionsFactory,
        ChannelWriter<HttpResponseMessage> fallbackResponseWriter,
        Sink<HttpRequestMessage, NotUsed> requestIngress,
        Source<HttpResponseMessage, NotUsed> responseFanoutSource) : IIndirectActorProducer
    {
        public Type ActorType => typeof(Consumer);

        public ActorBase Produce() => new Consumer(
            consumerId, requestReader,
            fallbackResponseWriter, optionsFactory,
            requestIngress, responseFanoutSource);

        public void Release(ActorBase actor)
        {
        }
    }

    private Consumer(
        Guid consumerId,
        ChannelReader<HttpRequestMessage> requestReader,
        ChannelWriter<HttpResponseMessage> responseEgress,
        Func<TurboRequestOptions> optionsFactory,
        Sink<HttpRequestMessage, NotUsed> requestIngress,
        Source<HttpResponseMessage, NotUsed> responseFanoutSource)
    {
        _consumerId = consumerId;
        _requestReader = requestReader;
        _optionsFactory = optionsFactory;
        _responseEgress = responseEgress;
        _requestIngress = requestIngress;
        _responseFanoutSource = responseFanoutSource;

        Receive<ConsumerSinkCompleted>(HandleSinkCompleted);
    }

    protected override void PreStart()
    {
        MaterializeIngress();
        MaterializeResponseSink();
    }

    private void MaterializeIngress()
    {
        var enricher = new RequestEnricher(_optionsFactory);
        var cid = _consumerId;

        ChannelSource.FromReader(_requestReader)
            .Select(request =>
            {
                if (!request.Options.TryGetValue(OptionsKey.ConsumerIdKey, out _))
                {
                    request.Options.Set(OptionsKey.ConsumerIdKey, cid);
                }

                return enricher.Enrich(request);
            })
            .RunWith(_requestIngress, _materializer);
    }

    private void MaterializeResponseSink()
    {
        var fallback = _responseEgress;
        var (killSwitch, completionTask) = _responseFanoutSource
            .ViaMaterialized(KillSwitches.Single<HttpResponseMessage>(), Keep.Right)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(response =>
                {
                    if (response.RequestMessage is { } req
                        && req.Options.TryGetValue(OptionsKey.Key, out var pending)
                        && req.Options.TryGetValue(OptionsKey.VersionKey, out var ver))
                    {
                        pending.TrySetResult(response, ver);
                        return;
                    }

                    fallback.TryWrite(response);
                }),
                Keep.Both)
            .Run(_materializer);

        _sinkKillSwitch = killSwitch;

        completionTask.PipeTo(Self, Self,
            () => new ConsumerSinkCompleted(null),
            ex => new ConsumerSinkCompleted(ex.GetBaseException()));
    }

    private void HandleSinkCompleted(ConsumerSinkCompleted completed)
    {
        _sinkKillSwitch = null;
        if (completed.Error is not null and not OperationCanceledException)
        {
            _log.Warning("Consumer {0} sink completed with error: {1}", _consumerId, completed.Error.Message);
        }
    }

    protected override void PostStop()
    {
        if (_sinkKillSwitch is null) return;
        _sinkKillSwitch.Abort(new OperationCanceledException("Consumer stopped"));
        _sinkKillSwitch = null;
    }
}