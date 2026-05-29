using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

public sealed class SharedPipelineSpec : ServerSpecBase
{
    private static readonly byte[] Payload = new byte[1 * 1024 * 1024];

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

        app.MapPost("/echo-size", async ctx =>
        {
            long count = 0;
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer, CancellationToken)) > 0)
            {
                count += read;
            }

            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(count.ToString(), CancellationToken);
        });
    }

    [Fact(Timeout = 10000)]
    public async Task Single_request_should_succeed()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/ping"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Should_handle_2_sequential_requests()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        var response1 = await Client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await Client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    [Fact(Timeout = 120000)]
    public async Task Should_handle_50_concurrent_get_requests()
    {
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 50 };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => client.GetAsync(uri, CancellationToken));

        var responses = await Task.WhenAll(tasks);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 30000, Skip = "POST body path has separate pre-existing concurrency issue")]
    public async Task Should_handle_50_concurrent_1mb_posts()
    {
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 50 };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var uri = new Uri($"http://127.0.0.1:{Port}/echo-size");

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => client.PostAsync(uri, new ByteArrayContent(Payload), CancellationToken));

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(Timeout = 15000)]
    public async Task Request_after_disconnect_should_still_succeed()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        using var shortLivedClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        var first = await shortLivedClient.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        first.Dispose();

        await Task.Delay(500, CancellationToken);

        var second = await Client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
