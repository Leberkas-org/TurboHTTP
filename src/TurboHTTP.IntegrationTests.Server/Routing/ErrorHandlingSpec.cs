using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ErrorHandlingSpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/throw-sync", () =>
        {
            throw new InvalidOperationException("sync boom");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });

        routeTable.Add("GET", "/throw-async", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
#pragma warning disable CS0162
            return Results.Ok();
#pragma warning restore CS0162
        });

        routeTable.Add("GET", "/ok", () => Results.Ok("fine"));
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
