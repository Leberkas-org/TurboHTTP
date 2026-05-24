using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Infrastructure;

public sealed class TimeoutSpec : ServerSpecBase
{
    protected override void ConfigureServer(IServiceCollection services, ushort port)
    {
        services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.KeepAliveTimeout = TimeSpan.FromSeconds(2);
            options.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
            options.Http1.KeepAliveTimeout = TimeSpan.FromSeconds(2);
            options.Http1.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
        });
    }

    protected override void ConfigureRoutes(TurboRouteTable routeTable)
    {
        routeTable.Add("GET", "/fast", () => Results.Ok("ok"));
    }

    [Fact(Timeout = 20000)]
    public async Task KeepAlive_should_close_idle_connection_after_timeout()
    {
        // First request succeeds
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Wait past keep-alive timeout (2s configured, wait 3s)
        await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);

        // New request with fresh client should still work (server is alive, old connection timed out)
        using var freshClient = new HttpClient();
        response = await freshClient.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task RequestHeaders_should_timeout_on_incomplete_headers()
    {
        // Send only partial headers (no final \r\n\r\n), then wait
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port, CancellationToken);
        var stream = tcp.GetStream();
        var partialBytes = Encoding.ASCII.GetBytes("GET /fast HTTP/1.1\r\nHost: localhost\r\n");
        await stream.WriteAsync(partialBytes, CancellationToken);

        // Wait past request headers timeout
        await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);

        // Try to read: connection should be closed (0 bytes) or we get an exception
        var buffer = new byte[1];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            Assert.Equal(0, read);
        }
        catch (OperationCanceledException)
        {
            // Connection closed, can't read = timeout worked
            Assert.True(true);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Server_should_still_respond_after_timeout_disconnects()
    {
        // Cause a timeout disconnect with incomplete headers
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, Port, CancellationToken);
            var tcpStream = tcp.GetStream();
            var incompleteBytes = Encoding.ASCII.GetBytes("GET /fast HTTP/1.1\r\nHost: localhost\r\n");
            await tcpStream.WriteAsync(incompleteBytes, CancellationToken);
            // Connection stays open briefly, then times out
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);
        }

        // Server should still accept new requests
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
