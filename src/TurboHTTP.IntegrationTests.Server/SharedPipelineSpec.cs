using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server;

public abstract class SharedPipelineBase(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/ping", () => Results.Content("pong", "text/plain"));
    }
}

public sealed class SharedPipelineBasicSpec(ActorSystemFixture systemFixture) : SharedPipelineBase(systemFixture)
{
    [Fact(Timeout = 10000)]
    public async Task Single_request_should_succeed()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/ping"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Sequential_requests_should_succeed()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        var r1 = await Client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await Client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }
}

public sealed class SharedPipelineConcurrencySpec(ActorSystemFixture systemFixture) : SharedPipelineBase(systemFixture)
{
    [Fact(Timeout = 30000)]
    public async Task Should_handle_50_concurrent_get_requests()
    {
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 50 };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => client.GetAsync(uri, CancellationToken));

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}

public sealed class SharedPipelineResilienceSpec(ActorSystemFixture systemFixture) : SharedPipelineBase(systemFixture)
{
    [Fact(Timeout = 30000)]
    public async Task Connection_after_tcp_abort_should_still_work()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        using (var socket = new System.Net.Sockets.TcpClient())
        {
            await socket.ConnectAsync("127.0.0.1", Port);
            socket.LingerState = new System.Net.Sockets.LingerOption(true, 0);
        }

        await Task.Delay(2000, CancellationToken);

        using var client = new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        var response = await client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
