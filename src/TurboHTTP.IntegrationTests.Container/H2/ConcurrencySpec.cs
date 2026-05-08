using System.Net;
using System.Text;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H2;

[Collection("H2")]
public sealed class ConcurrencySpec : IAsyncLifetime
{
    private readonly ServerContainerFixture _server;
    private readonly ActorSystemFixture _systemFixture;
    private ClientHelper? _helper;

    public ConcurrencySpec(ServerContainerFixture server, ActorSystemFixture systemFixture)
    {
        _server = server;
        _systemFixture = systemFixture;
    }

    public ValueTask InitializeAsync()
    {
        if (!_server.IsDockerAvailable)
        {
            Assert.Skip("Docker is not available.");
        }

        if (_server.HttpsPort == 0)
        {
            Assert.Skip("Nginx TLS proxy is not available.");
        }

        _helper = ClientHelper.CreateClient(
            _server.HttpsPort,
            new Version(2, 0),
            scheme: "https",
            system: _systemFixture.System,
            host: "localhost");
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
    public async Task Concurrency_should_multiplex_parallel_gets()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_mixed_methods_multiplexed()
    {
        var ct = TestContext.Current.CancellationToken;

        var getTasks = Enumerable.Range(0, 5).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct));

        var postTasks = Enumerable.Range(0, 5).Select(i =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/post")
                {
                    Content = new StringContent($"body-{i}", Encoding.UTF8)
                }, ct));

        var responses = await Task.WhenAll(getTasks.Concat(postTasks));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_parallel_different_endpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoints = new[] { "/get", "/headers", "/bytes/64", "/status/200", "/gzip", "/deflate" };

        var tasks = endpoints.Select(e =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, e), ct));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_20_parallel_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 20).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_parallel_large_bodies()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/bytes/16384"), ct));

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsByteArrayAsync(ct);
            Assert.Equal(16384, content.Length);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_succeed_with_sequential_burst()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 20; i++)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
