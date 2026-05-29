using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ErrorHandlingSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapGet("/throw-sync", () =>
        {
            throw new InvalidOperationException("sync boom");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });

        app.MapGet("/throw-async", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });

        app.MapGet("/ok", () => Results.Ok("fine"));
    }

    [Fact(Timeout = 15000)]
    public async Task Sync_handler_exception_should_return_500()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/throw-sync"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Async_handler_exception_should_return_500()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/throw-async"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_recover_after_handler_exception()
    {
        await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/throw-sync"),
            CancellationToken);

        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/ok"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
