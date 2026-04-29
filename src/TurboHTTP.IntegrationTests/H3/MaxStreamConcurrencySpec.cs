using System.Net;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class MaxStreamConcurrencySpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public MaxStreamConcurrencySpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        _helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(3, 0),
            scheme: "https",
            system: _systemFixture.System);
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
    public async Task Five_concurrent_requests_should_complete_within_stream_limit()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var tasks = Enumerable.Range(0, 5)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/h3/delay/200");
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
    public async Task Sequential_requests_should_succeed_through_stream_limiter()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

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

        var tasks = Enumerable.Range(0, 10)
            .Select(_ =>
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

        var tasks = Enumerable.Range(0, 5)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/h3/delay/300");
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

        var endpoints = new[] { "/hello", "/ping", "/h3/delay/100", "/hello", "/ping" };
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
