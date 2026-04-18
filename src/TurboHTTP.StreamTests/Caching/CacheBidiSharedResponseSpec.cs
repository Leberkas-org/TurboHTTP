using System.Net;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using TurboHTTP.Protocol.Caching;
using TurboHTTP.Streams.Stages.Features;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Caching;

public sealed class CacheBidiSharedResponseSpec : StreamTestBase
{
    [Fact(Timeout = 10_000)]
    [Trait("RFC", "RFC9111-4")]
    public void CacheBidiStage_should_serve_same_response_reference_on_multiple_cache_hits()
    {
        var store = new CacheStore();
        var stage = new CacheBidiStage(store);

        var reqPub = this.CreateManualPublisherProbe<HttpRequestMessage>();
        var respPub = this.CreateManualPublisherProbe<HttpResponseMessage>();
        var reqOutProbe = this.CreateManualSubscriberProbe<HttpRequestMessage>();
        var respOutProbe = this.CreateManualSubscriberProbe<HttpResponseMessage>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var bidi = b.Add(stage);
            b.From(Source.FromPublisher(reqPub)).To(bidi.Inlet1);
            b.From(bidi.Outlet1).To(Sink.FromSubscriber(reqOutProbe));
            b.From(Source.FromPublisher(respPub)).To(bidi.Inlet2);
            b.From(bidi.Outlet2).To(Sink.FromSubscriber(respOutProbe));
            return ClosedShape.Instance;
        })).Run(Materializer);

        var reqInSub = reqPub.ExpectSubscription(TestContext.Current.CancellationToken);
        var respInSub = respPub.ExpectSubscription(TestContext.Current.CancellationToken);
        var reqOutSub = reqOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);
        var respOutSub = respOutProbe.ExpectSubscription(TestContext.Current.CancellationToken);

        reqOutSub.Request(10);
        respOutSub.Request(10);

        // 1. First request — cache miss → forwarded to engine
        var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/shared");
        reqInSub.SendNext(request1);
        reqOutProbe.ExpectNext(TestContext.Current.CancellationToken);

        // 2. Engine responds with cacheable 200 OK
        var engineResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("hello"u8.ToArray()),
            RequestMessage = request1
        };
        engineResponse.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
        engineResponse.Headers.Date = DateTimeOffset.UtcNow;
        respInSub.SendNext(engineResponse);
        var firstResponse = respOutProbe.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // 3. Second request for same URL — cache hit
        var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/shared");
        reqInSub.SendNext(request2);
        // Cache hit → response pushed directly on Out2 (no engine forwarding)
        var cachedResponse1 = respOutProbe.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cachedResponse1.StatusCode);

        // 4. Third request for same URL — another cache hit
        var request3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/shared");
        reqInSub.SendNext(request3);
        var cachedResponse2 = respOutProbe.ExpectNext(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, cachedResponse2.StatusCode);

        // After fix: each cache hit clones the response
        Assert.NotSame(cachedResponse1, cachedResponse2);
        Assert.Same(request2, cachedResponse1.RequestMessage);
        Assert.Same(request3, cachedResponse2.RequestMessage);
    }
}