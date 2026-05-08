using System.Net;
using TurboHTTP.IntegrationTests.Container.Shared;

namespace TurboHTTP.IntegrationTests.Container.H10;

[Collection("H10")]
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

        _helper = ClientHelper.CreateClient(
            _server.HttpPort,
            new Version(1, 0),
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
    public async Task Concurrency_should_succeed_with_parallel_gets()
    {
        var ct = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_succeed_with_sequential_burst()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 10; i++)
        {
            var response = await _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_mixed_methods()
    {
        var ct = TestContext.Current.CancellationToken;

        var getTasks = Enumerable.Range(0, 3).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, "/get"), ct));

        var postTasks = Enumerable.Range(0, 3).Select(_ =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/post")
                {
                    Content = new StringContent("test")
                }, ct));

        var responses = await Task.WhenAll(getTasks.Concat(postTasks));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000)]
    public async Task Concurrency_should_handle_parallel_different_endpoints()
    {
        var ct = TestContext.Current.CancellationToken;
        var endpoints = new[] { "/get", "/headers", "/bytes/64", "/status/200" };

        var tasks = endpoints.Select(e =>
            _helper!.Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, e), ct));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
