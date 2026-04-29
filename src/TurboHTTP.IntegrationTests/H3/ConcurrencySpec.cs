using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Shared;

namespace TurboHTTP.IntegrationTests.H3;

[Collection("H3")]
[Trait("Category", "Http3")]
public sealed class ConcurrencySpec : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public ConcurrencySpec(ServerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        QuicAvailability.SkipIfUnavailable();
        _helper = ClientHelper.CreateClient(_server.HttpsPort, new Version(3, 0), scheme: "https", system: _systemFixture.System);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_helper is not null)
        {
            await _helper.DisposeAsync();
        }
    }

    [Fact(Timeout = 60000)]
    public async Task Ten_parallel_gets_should_be_multiplexed_over_quic_streams()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

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

    [Fact(Timeout = 60000)]
    public async Task Twenty_parallel_requests_should_succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var tasks = Enumerable.Range(0, 20)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/hello");
                return _helper!.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(20, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 60000)]
    public async Task Mixed_get_and_post_should_be_multiplexed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var getTask1 = _helper!.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        var getTask2 = _helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token);
        var postTask1 = _helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/h3/echo-binary")
            {
                Content = new ByteArrayContent(new byte[512])
            }, cts.Token);
        var postTask2 = _helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/echo")
            {
                Content = new StringContent("h3-concurrent-post", Encoding.UTF8, "text/plain")
            }, cts.Token);
        var getTask3 = _helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/h3/settings"), cts.Token);

        var responses = await Task.WhenAll(getTask1, getTask2, postTask1, postTask2, getTask3);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 60000)]
    public async Task Concurrent_requests_to_different_endpoints_should_succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var endpoints = new[] { "/hello", "/ping", "/h3/settings", "/hello", "/ping", "/h3/settings", "/hello", "/ping" };
        var tasks = endpoints
            .Select(path =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, path);
                return _helper!.Client.SendAsync(request, cts.Token);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(8, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 60000)]
    public async Task Concurrent_heavy_posts_should_complete_over_quic_streams()
    {
        var payload = new byte[10 * 1024];
        await using var ownHelper = ClientHelper.CreateClient(_server.HttpsPort, new Version(3, 0), scheme: "https");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));

        for (var batch = 0; batch < 4; batch++)
        {
            var tasks = new Task<HttpResponseMessage>[16];
            for (var i = 0; i < 16; i++)
            {
                tasks[i] = ownHelper.Client.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, "/h3/echo-binary")
                    {
                        Content = new ByteArrayContent(payload)
                    }, cts.Token);
            }

            var responses = await Task.WhenAll(tasks);
            Assert.Equal(16, responses.Length);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }
    }
}
