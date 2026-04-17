using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.Shared;

/// <summary>
/// Verifies <see cref="ResponseMapFake"/> infrastructure:
/// static mapping, dynamic mapping, unmapped path 404, and header manipulation.
/// </summary>
public sealed class ResponseMapFakeSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public ResponseMapFakeSpec() : base(ActorSystem.Create("responsemap-test-" + Guid.NewGuid()))
    {
        _materializer = Sys.Materializer();
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseMapFake_should_return_200_for_mapped_path()
    {
        var map = new ResponseMap()
            .On("/hello", HttpStatusCode.OK, "world");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/hello");

        var response = await RunSingleRequest(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("world", body);
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseMapFake_should_return_dynamic_response_based_on_request()
    {
        var map = new ResponseMap()
            .On("/echo", req =>
            {
                var name = req.RequestUri?.Query.TrimStart('?') ?? "anonymous";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"hello {name}")
                };
            });

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/echo?alice");

        var response = await RunSingleRequest(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hello alice", body);
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseMapFake_should_return_404_for_unmapped_path()
    {
        var map = new ResponseMap()
            .On("/exists", HttpStatusCode.OK, "found");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing");

        var response = await RunSingleRequest(map, request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseMapFake_should_support_header_manipulation_via_predicate()
    {
        var map = new ResponseMap()
            .On(
                req => req.Headers.Contains("X-Custom"),
                req =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("custom")
                    };
                    response.Headers.Add("X-Echo", req.Headers.GetValues("X-Custom").First());
                    return response;
                });

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/any");
        request.Headers.Add("X-Custom", "test-value");

        var response = await RunSingleRequest(map, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-value", response.Headers.GetValues("X-Echo").Single());
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseMapFake_should_handle_multiple_requests()
    {
        var map = new ResponseMap()
            .On("/a", HttpStatusCode.OK, "alpha")
            .On("/b", HttpStatusCode.Created, "beta");

        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/a"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/b"),
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/c")
        };

        var results = await RunMultipleRequests(map, requests, 3);

        Assert.Equal(3, results.Count);
        Assert.Equal(HttpStatusCode.OK, results[0].StatusCode);
        Assert.Equal("alpha", await results[0].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Created, results[1].StatusCode);
        Assert.Equal("beta", await results[1].Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, results[2].StatusCode);
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseMapFake_should_set_request_message_on_response()
    {
        var map = new ResponseMap()
            .On("/track", HttpStatusCode.OK, "tracked");

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/track");

        var response = await RunSingleRequest(map, request);

        Assert.NotNull(response.RequestMessage);
        Assert.Equal(HttpMethod.Post, response.RequestMessage.Method);
        Assert.Equal("/track", response.RequestMessage.RequestUri?.AbsolutePath);
    }

    private async Task<HttpResponseMessage> RunSingleRequest(ResponseMap map, HttpRequestMessage request)
    {
        var bidi = ResponseMapFake.Create(map);

        // Join with a dummy flow that echoes a placeholder response.
        // ResponseMapFake discards these — responses come from the map.
        var flow = bidi.Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(
            _ => new HttpResponseMessage()));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), _materializer);

        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<List<HttpResponseMessage>> RunMultipleRequests(
        ResponseMap map, IEnumerable<HttpRequestMessage> requests, int expectedCount)
    {
        var bidi = ResponseMapFake.Create(map);

        var flow = bidi.Join(Flow.FromFunction<HttpRequestMessage, HttpResponseMessage>(
            _ => new HttpResponseMessage()));

        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == expectedCount)
                {
                    tcs.TrySetResult();
                }
            }), _materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        return results;
    }
}
