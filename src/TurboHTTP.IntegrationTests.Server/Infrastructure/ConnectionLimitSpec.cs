using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Infrastructure;

public sealed class ConnectionLimitSpec : ServerSpecBase
{
    private readonly TaskCompletionSource _slot1Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _slot2Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.MaxConcurrentConnections = 2;
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/block-slot-1", async () =>
        {
            await _slot1Gate.Task;
            return Results.Ok("slot1-done");
        });

        routeTable.Add("GET", "/block-slot-2", async () =>
        {
            await _slot2Gate.Task;
            return Results.Ok("slot2-done");
        });

        routeTable.Add("GET", "/fast", () => Results.Ok("ok"));
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
        // Fill up 2 connection slots with blocking handlers
        var client1 = new HttpClient();
        var client2 = new HttpClient();
        var client3 = new HttpClient();

        // Start first request to occupy slot 1
        var request1 = client1.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/block-slot-1"),
            CancellationToken);
        await Task.Delay(200, CancellationToken);

        // Start second request to occupy slot 2
        var request2 = client2.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/block-slot-2"),
            CancellationToken);
        await Task.Delay(200, CancellationToken);

        // Third request should be rejected or timeout (no slots available)
        bool request3Failed;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await client3.GetAsync(
                new Uri($"http://127.0.0.1:{Port}/fast"),
                cts.Token);
            // If we got a response, check if it's an error
            request3Failed = !response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            // Timed out = effectively rejected
            request3Failed = true;
        }
        catch (HttpRequestException)
        {
            // Connection refused = rejected
            request3Failed = true;
        }

        // Clean up
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
        // Use first connection (within limit)
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Server should still accept subsequent connections
        response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}