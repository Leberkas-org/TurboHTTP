using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H2;

[Collection("H2")]
public sealed class MaxConcurrentStreamsSpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public MaxConcurrentStreamsSpec(ServerFixture server, ActorSystemFixture systemFixture)
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

    [Fact(Timeout = 30000)]
    public async Task Five_concurrent_requests_should_complete_with_limiter_active()
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

    [Fact(Timeout = 30000)]
    public async Task Sequential_requests_should_succeed_through_limiter()
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

    [Fact(Timeout = 30000)]
    public async Task Ten_concurrent_requests_should_complete_successfully()
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

    [Fact(Timeout = 30000)]
    public async Task Concurrent_delayed_requests_should_complete_as_streams_free_up()
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

    [Fact(Timeout = 30000)]
    public async Task Mixed_concurrent_requests_should_complete()
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
