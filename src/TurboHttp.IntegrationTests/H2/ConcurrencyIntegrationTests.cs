using System.Net;
using System.Text;
using TurboHttp.IntegrationTests.Shared;

namespace TurboHttp.IntegrationTests.H2;

[Collection("H2")]
public sealed class ConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ServerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public ConcurrencyIntegrationTests(ServerFixture server, ActorSystemFixture systemFixture)
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

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H2-001: 10 parallel GET requests multiplexed over single connection all succeed")]
    public async Task Ten_Parallel_Gets_Multiplexed_All_Succeed()
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

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H2-002: 20 parallel requests stress test all succeed")]
    public async Task Twenty_Parallel_Requests_All_Succeed()
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

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H2-003: Mixed GET and POST multiplexed requests all succeed")]
    public async Task Mixed_Get_And_Post_Multiplexed_All_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var getTask1 = _helper!.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/hello"), cts.Token);
        var getTask2 = _helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"), cts.Token);
        var postTask1 = _helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/h2/echo-binary")
            {
                Content = new ByteArrayContent(new byte[512])
            }, cts.Token);
        var postTask2 = _helper.Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, "/echo")
            {
                Content = new StringContent("h2-concurrent-post", Encoding.UTF8, "text/plain")
            }, cts.Token);
        var getTask3 = _helper.Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/h2/settings"), cts.Token);

        var responses = await Task.WhenAll(getTask1, getTask2, postTask1, postTask2, getTask3);

        Assert.Equal(5, responses.Length);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 60000, DisplayName = "Concurrency-H2-004: Concurrent requests to different endpoints all succeed")]
    public async Task Concurrent_Different_Endpoints_All_Succeed()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        var endpoints = new[] { "/hello", "/ping", "/h2/settings", "/hello", "/ping", "/h2/settings", "/hello", "/ping" };
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
}
