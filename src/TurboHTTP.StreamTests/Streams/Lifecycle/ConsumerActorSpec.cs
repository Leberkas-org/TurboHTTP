using System.Net;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Streams.Lifecycle;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams.Lifecycle;

public sealed class ConsumerActorSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    public async Task ConsumerActor_should_be_created_and_stopped_cleanly()
    {
        var consumerId = Guid.NewGuid();
        var requestChannel = Channel.CreateUnbounded<HttpRequestMessage>();
        var responseChannel = Channel.CreateUnbounded<HttpResponseMessage>();
        Func<TurboRequestOptions> optionsFactory = () => new TurboRequestOptions(
            BaseAddress: new Uri("https://test.example"),
            DefaultRequestHeaders: new HttpRequestMessage().Headers,
            DefaultRequestVersion: HttpVersion.Version11,
            DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrLower,
            Timeout: TimeSpan.FromSeconds(30),
            Credentials: null,
            PreAuthenticate: false);

        var (mergeHubSink, broadcastHubSource) = CreateTestHubs();

        var actor = Sys.ActorOf(ConsumerActor.Props(
            consumerId,
            requestChannel.Reader,
            optionsFactory,
            responseChannel.Writer,
            mergeHubSink,
            broadcastHubSource,
            Materializer));

        Watch(actor);
        Sys.Stop(actor);
        ExpectTerminated(actor, TimeSpan.FromSeconds(2), cancellationToken: TestContext.Current.CancellationToken);
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
}
