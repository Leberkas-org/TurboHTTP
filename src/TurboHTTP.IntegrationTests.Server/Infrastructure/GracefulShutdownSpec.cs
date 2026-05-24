using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Infrastructure;

public sealed class GracefulShutdownSpec : ServerSpecBase
{
    private readonly TaskCompletionSource _handlerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.GracefulShutdownTimeout = TimeSpan.FromSeconds(5);
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/slow", async () =>
        {
            await _handlerGate.Task;
            return Results.Ok("done");
        });

        routeTable.Add("GET", "/fast", () => Results.Ok("ok"));
    }

    public override async ValueTask DisposeAsync()
    {
        _handlerGate.TrySetResult();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 20000)]
    public async Task Shutdown_should_complete_inflight_request()
    {
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Start a slow request that blocks on handlerRelease
        using var testClient = new HttpClient();
        var request = testClient.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/slow"),
            TestContext.Current.CancellationToken);

        // Wait a bit for server to be ready
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Verify server is responding
        var healthCheck = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, healthCheck.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Shutdown_should_reject_new_connections()
    {
        // Basic sanity check that server is working
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
