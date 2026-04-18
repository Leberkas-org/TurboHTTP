using System.Collections.Concurrent;
using System.Net;
using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams.Stages.Internal;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.StreamTests.Streams;

public sealed class EndpointDispatchCachingSpec : StreamTestBase
{
    private static HttpRequestMessage Req(string url, Version version)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Version = version;
        return req;
    }

    private static HttpResponseMessage Echo(HttpRequestMessage request)
        => new(HttpStatusCode.OK) { RequestMessage = request };

    private static Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> EchoFlow()
        => Flow.Create<HttpRequestMessage>().Select(Echo);

    [Fact(Timeout = 5000)]
    public async Task EndpointDispatch_should_call_flow_factory_only_once_per_endpoint()
    {
        var factoryCallCount = new ConcurrentDictionary<RequestEndpoint, int>();

        var stage = new EndpointDispatchStage(endpoint =>
        {
            factoryCallCount.AddOrUpdate(endpoint, 1, (_, count) => count + 1);
            return EchoFlow();
        });

        var flow = Flow.FromGraph(stage);

        // Send multiple requests with the same endpoint through the stage.
        // Each request triggers a new substream in the real pipeline, but here we test
        // that the stage reuses the cached flow blueprint after the first materialization.
        var requests = new[]
        {
            Req("http://example.com/1", HttpVersion.Version11),
            Req("http://example.com/2", HttpVersion.Version11),
            Req("http://example.com/3", HttpVersion.Version11),
        };

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Equal(3, results.Count);

        // Factory should have been called exactly once for this endpoint
        var expectedEndpoint = RequestEndpoint.FromRequest(requests[0]);
        Assert.True(factoryCallCount.ContainsKey(expectedEndpoint));
        Assert.Equal(1, factoryCallCount[expectedEndpoint]);
    }

    [Fact(Timeout = 5000)]
    public async Task EndpointDispatch_should_call_flow_factory_once_per_distinct_endpoint()
    {
        var factoryCallCount = new ConcurrentDictionary<RequestEndpoint, int>();

        var stage = new EndpointDispatchStage(endpoint =>
        {
            factoryCallCount.AddOrUpdate(endpoint, 1, (_, count) => count + 1);
            return EchoFlow();
        });

        var flow = Flow.FromGraph(stage);

        // First endpoint determines the flow for this stage instance
        var requests = new[]
        {
            Req("http://example.com/1", HttpVersion.Version11),
            Req("http://example.com/2", HttpVersion.Version11),
        };

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Equal(2, results.Count);
        var expectedEndpoint = RequestEndpoint.FromRequest(requests[0]);
        Assert.Equal(1, factoryCallCount[expectedEndpoint]);
    }

    [Fact(Timeout = 5000)]
    public async Task EndpointDispatch_should_cache_across_multiple_materializations()
    {
        var factoryCallCount = new ConcurrentDictionary<RequestEndpoint, int>();

        // Single stage instance shared across materializations
        var stage = new EndpointDispatchStage(endpoint =>
        {
            factoryCallCount.AddOrUpdate(endpoint, 1, (_, count) => count + 1);
            return EchoFlow();
        });

        // Materialize twice with the same endpoint — second materialization should reuse the cached blueprint
        var flow = Flow.FromGraph(stage);

        var req1 = Req("http://example.com/1", HttpVersion.Version11);
        var req2 = Req("http://example.com/2", HttpVersion.Version11);

        var result1 = await Source.Single(req1)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        var result2 = await Source.Single(req2)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(result1);
        Assert.Single(result2);

        // Factory called only once despite two separate materializations (same endpoint)
        var expectedEndpoint = RequestEndpoint.FromRequest(req1);
        Assert.Equal(1, factoryCallCount[expectedEndpoint]);
    }

    [Fact(Timeout = 5000)]
    public async Task EndpointDispatch_should_forward_all_elements_through_inner_flow()
    {
        var stage = new EndpointDispatchStage(_ => EchoFlow());
        var flow = Flow.FromGraph(stage);

        var requests = new[]
        {
            Req("http://example.com/a", HttpVersion.Version11),
            Req("http://example.com/b", HttpVersion.Version11),
            Req("http://example.com/c", HttpVersion.Version11),
        };

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 5000)]
    public async Task EndpointDispatch_should_complete_cleanly_when_source_completes()
    {
        var stage = new EndpointDispatchStage(_ => EchoFlow());
        var flow = Flow.FromGraph(stage);

        var results = await Source.Single(Req("http://example.com/done", HttpVersion.Version20))
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Single(results);
    }

    [Fact(Timeout = 5000)]
    public async Task EndpointDispatch_should_complete_cleanly_on_empty_source()
    {
        var stage = new EndpointDispatchStage(_ => EchoFlow());
        var flow = Flow.FromGraph(stage);

        var results = await Source.Empty<HttpRequestMessage>()
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        Assert.Empty(results);
    }
}