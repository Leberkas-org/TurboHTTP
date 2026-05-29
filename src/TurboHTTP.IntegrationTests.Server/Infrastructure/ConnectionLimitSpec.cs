using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Infrastructure;

[Collection("Infrastructure")]
public sealed class ConnectionLimitSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
{
    private readonly TaskCompletionSource _slot1Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _slot2Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.Limits.MaxConcurrentConnections = 2;
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/block-slot-1", async () =>
        {
            await _slot1Gate.Task;
            return Results.Ok("slot1-done");
        });

        app.MapGet("/block-slot-2", async () =>
        {
            await _slot2Gate.Task;
            return Results.Ok("slot2-done");
        });

        app.MapGet("/fast", () => Results.Ok("ok"));
    }

    public override async ValueTask DisposeAsync()
    {
        _slot1Gate.TrySetResult();
        _slot2Gate.TrySetResult();
        await base.DisposeAsync();
    }

    [Fact(Timeout = 20000)]
    public async Task Server_should_accept_connections_within_limit()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task Server_should_reject_connections_beyond_limit()
    {
        var client1 = new HttpClient();
        var client2 = new HttpClient();
        var client3 = new HttpClient();

        var request1 = client1.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/block-slot-1"),
            CancellationToken);
        await Task.Delay(200, CancellationToken);

        var request2 = client2.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/block-slot-2"),
            CancellationToken);
        await Task.Delay(200, CancellationToken);

        bool request3Failed;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await client3.GetAsync(
                new Uri($"http://127.0.0.1:{Port}/fast"),
                cts.Token);
            request3Failed = !response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            request3Failed = true;
        }
        catch (HttpRequestException)
        {
            request3Failed = true;
        }

        _slot1Gate.TrySetResult();
        _slot2Gate.TrySetResult();

        try
        {
            await request1;
            await request2;
        }
        catch
        {
            // noop
        }

        Assert.True(request3Failed, "Third connection should have been rejected");
    }

    [Fact(Timeout = 20000)]
    public async Task Server_should_accept_after_connection_closes()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
