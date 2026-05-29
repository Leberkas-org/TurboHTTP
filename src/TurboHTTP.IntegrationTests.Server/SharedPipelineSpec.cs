using System.Net;
using TurboHTTP.IntegrationTests.Server.Shared;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SharedPipelineBasicSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 10000)]
    public async Task Single_request_should_succeed()
    {
        var response = await _client.GetAsync(
            new Uri($"http://127.0.0.1:{server.Port}/ping"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Sequential_requests_should_succeed()
    {
        var uri = new Uri($"http://127.0.0.1:{server.Port}/ping");

        var r1 = await _client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await _client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }
}

public sealed class SharedPipelineConcurrencySpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 30000)]
    public async Task Should_handle_50_concurrent_get_requests()
    {
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 50 };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var uri = new Uri($"http://127.0.0.1:{server.Port}/ping");

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => client.GetAsync(uri, CancellationToken));

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}

public sealed class SharedPipelineResilienceSpec(TurboServerFixture server) : IDisposable
{
    private readonly HttpClient _client = server.CreateClient();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => _client.Dispose();

    [Fact(Timeout = 30000)]
    public async Task Connection_after_tcp_abort_should_still_work()
    {
        var uri = new Uri($"http://127.0.0.1:{server.Port}/ping");

        using (var socket = new System.Net.Sockets.TcpClient())
        {
            await socket.ConnectAsync("127.0.0.1", server.Port, TestContext.Current.CancellationToken);
            socket.LingerState = new System.Net.Sockets.LingerOption(true, 0);
        }

        await Task.Delay(2000, CancellationToken);

        using var freshClient = new HttpClient(new SocketsHttpHandler());
        freshClient.Timeout = TimeSpan.FromSeconds(10);
        var response = await freshClient.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}