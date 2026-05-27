using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Infrastructure;

public sealed class GracefulShutdownSpec : ServerSpecBase
{
    private readonly TaskCompletionSource _handlerGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.GracefulShutdownTimeout = TimeSpan.FromSeconds(5);
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/slow", async () =>
        {
            await _handlerGate.Task;
            return Results.Ok("done");
        });

        app.MapGet("/fast", () => Results.Ok("ok"));
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

        using var testClient = new HttpClient();
        var request = testClient.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/slow"),
            TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        var healthCheck = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, healthCheck.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Shutdown_should_reject_new_connections()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
