using System.Net;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H2;

/// <summary>
/// Integration tests for MAX_CONCURRENT_STREAMS enforcement over a real HTTP/2 connection.
/// Uses the shared Kestrel server with H2 h2c endpoint. Kestrel advertises its default
/// MAX_CONCURRENT_STREAMS (100) via SETTINGS. The client must respect this limit.
/// These tests verify that multiple concurrent requests complete successfully through
/// the limiter stage when sent to a real server.
/// </summary>
/// <remarks>
/// RFC 9113 §5.1.2: An endpoint MUST NOT exceed the limit set by its peer.
/// The client reads MAX_CONCURRENT_STREAMS from the server's SETTINGS frame and enforces it.
/// </remarks>
[Collection("H2")]
public sealed class MaxConcurrentStreamsIntegrationTests : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public MaxConcurrentStreamsIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        _helper = ClientHelper.CreateClient(_server.H2Port, new Version(2, 0), system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 30000, DisplayName = "RFC9113-5.1.2-INT-001: Five concurrent requests complete over H2 with limiter active")]
    public async Task Should_CompleteAllRequests_When_FiveConcurrentRequestsSentOverH2()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Send 5 concurrent requests — the limiter stage enforces MAX_CONCURRENT_STREAMS
        // from the server's SETTINGS. Kestrel defaults to 100, so all 5 should proceed
        // immediately. This verifies the limiter stage doesn't block legitimate traffic.
        var tasks = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/delay/200");
                return _helper!.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 30000, DisplayName = "RFC9113-5.1.2-INT-002: Sequential requests succeed through limiter on single H2 connection")]
    public async Task Should_CompleteSequentially_When_RequestsSentOneByOne()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Send 5 requests sequentially — verifies the limiter correctly tracks
        // stream opens and closes across the full pipeline lifecycle
        for (var i = 0; i < 5; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
            var response = await _helper!.Client.SendAsync(request, cts.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            Assert.Equal("Hello World", body);
        }
    }

    [Fact(Timeout = 30000, DisplayName = "RFC9113-5.1.2-INT-003: Ten concurrent requests all complete successfully")]
    public async Task Should_CompleteAllTen_When_TenConcurrentRequestsSent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Send 10 concurrent requests — even if queued, all should complete
        var tasks = Enumerable.Range(0, 10)
            .Select(i =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/ping");
                return _helper!.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(10, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000, DisplayName = "RFC9113-5.1.2-INT-004: Concurrent delayed requests complete as streams free up")]
    public async Task Should_CompleteAsStreamsFreeUp_When_ConcurrentDelayedRequestsSent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Send 5 requests with 300ms server-side delay each
        // The limiter should queue any that exceed the server's advertised limit
        // and send them as active streams complete
        var tasks = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/delay/300");
                return _helper!.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            Assert.Equal("delayed", body);
        }
    }

    [Fact(Timeout = 30000, DisplayName = "RFC9113-5.1.2-INT-005: Mixed GET requests with varying response sizes complete concurrently")]
    public async Task Should_CompleteAllMixed_When_ConcurrentMixedRequestsSent()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // Mix of fast and slow endpoints to exercise limiter under varying stream lifetimes
        var endpoints = new[] { "/hello", "/ping", "/delay/100", "/hello", "/ping" };
        var tasks = endpoints
            .Select(path =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, path);
                return _helper!.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
