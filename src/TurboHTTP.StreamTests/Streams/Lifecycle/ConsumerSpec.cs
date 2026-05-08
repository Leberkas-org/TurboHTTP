using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

public sealed class ConsumerSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_be_created_and_stopped_cleanly()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubs();

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        await WatchAsync(actor);
        Sys.Stop(actor);
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_stamp_consumer_id_on_ingress_requests()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var enrichedRequests = new List<HttpRequestMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubsWithTap(enrichedRequests);

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example/test");
        await requestChannel.Writer.WriteAsync(request, TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Single(enrichedRequests);
        var tappedRequest = enrichedRequests[0];

        Assert.True(tappedRequest.Options.TryGetValue(TurboClientCorrelation.ConsumerIdKey, out var stampedId));
        Assert.Equal(consumerId, stampedId);

        Sys.Stop(actor);
    }

    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_enrich_requests_with_base_address()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var baseAddress = new Uri("https://api.example");
        var enrichedRequests = new List<HttpRequestMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: baseAddress,
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubsWithTap(enrichedRequests);

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        var relativeUri = new Uri("/test", UriKind.Relative);
        var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        await requestChannel.Writer.WriteAsync(request, TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Single(enrichedRequests);
        var tappedRequest = enrichedRequests[0];

        Assert.NotNull(tappedRequest.RequestUri);
        Assert.Equal("https://api.example/test", tappedRequest.RequestUri!.AbsoluteUri);

        Sys.Stop(actor);
    }

    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_complete_pending_request_tcs_on_response()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubs();

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        var pending = PendingRequest.Rent();
        var version = pending.Version;
        var responseTask = pending.GetValueTask();

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.example/test");
        request.Options.Set(TurboClientCorrelation.Key, pending);
        request.Options.Set(TurboClientCorrelation.VersionKey, version);

        await requestChannel.Writer.WriteAsync(request, TestContext.Current.CancellationToken);

        var response = await responseTask.AsTask();
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(request, response.RequestMessage);

        PendingRequest.Return(pending);
        Sys.Stop(actor);
    }

    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_write_to_fallback_channel_when_no_tcs()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var responseInjectChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubsWithManualResponses(responseInjectChannel.Reader);

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.example/test");
        request.Options.Set(TurboClientCorrelation.ConsumerIdKey, consumerId);

        await requestChannel.Writer.WriteAsync(request, TestContext.Current.CancellationToken);

        var responseToWrite = new HttpResponseMessage(HttpStatusCode.Created);
        responseToWrite.RequestMessage = request;

        await responseInjectChannel.Writer.WriteAsync(responseToWrite, TestContext.Current.CancellationToken);

        var fallbackResponse = await responseChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(fallbackResponse);
        Assert.Equal(HttpStatusCode.Created, fallbackResponse.StatusCode);

        Sys.Stop(actor);
    }

    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_abort_killswitch_on_stop()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        var optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubs();

        var actor = Sys.ActorOf(Consumer.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        await WatchAsync(actor);
        Sys.Stop(actor);
        await ExpectTerminatedAsync(actor, TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
    }

    private (Sink<HttpRequestMessage, NotUsed>, Source<HttpResponseMessage, NotUsed>) CreateTestHubs()
    {
        var (sink, source) = MergeHub.Source<HttpRequestMessage>(16)
            .Via(Flow.Create<HttpRequestMessage>().Select(req =>
                new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req }))
            .ToMaterialized(BroadcastHub.Sink<HttpResponseMessage>(256), Keep.Both)
            .Run(Materializer);
        return (sink, source);
    }

    private (Sink<HttpRequestMessage, NotUsed>, Source<HttpResponseMessage, NotUsed>) CreateTestHubsWithTap(
        List<HttpRequestMessage> enrichedRequests)
    {
        var (mergeSource, broadcastSink) = MergeHub.Source<HttpRequestMessage>(16)
            .Via(Flow.Create<HttpRequestMessage>()
                .Select(req =>
                {
                    enrichedRequests.Add(req);
                    return req;
                })
                .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req }))
            .ToMaterialized(BroadcastHub.Sink<HttpResponseMessage>(256), Keep.Both)
            .Run(Materializer);
        return (mergeSource, broadcastSink);
    }

    private (Sink<HttpRequestMessage, NotUsed>, Source<HttpResponseMessage, NotUsed>) CreateTestHubsWithManualResponses(
        ChannelReader<HttpResponseMessage> responseReader)
    {
        var responseSource = ChannelSource.FromReader(responseReader);

        var mergeSink = MergeHub.Source<HttpRequestMessage>(16)
            .To(Sink.Ignore<HttpRequestMessage>())
            .Run(Materializer);

        return (mergeSink, responseSource);
    }
}
