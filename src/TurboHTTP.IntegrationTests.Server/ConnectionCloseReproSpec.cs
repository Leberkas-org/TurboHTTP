using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server;

[Collection("Infrastructure")]
public sealed class ConnectionCloseReproSpec : ServerSpecBase
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

    [Fact(Timeout = 15000)]
    public async Task New_connection_after_graceful_close_should_succeed()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        using (var client1 = new HttpClient())
        {
            var r1 = await client1.GetAsync(uri, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        }

        await Task.Delay(500, CancellationToken);

        using var client2 = new HttpClient();
        var r2 = await client2.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task New_connection_after_tcp_rst_should_succeed()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync("127.0.0.1", Port, CancellationToken);
            socket.LingerState = new LingerOption(true, 0);
        }

        await Task.Delay(500, CancellationToken);

        using var client = new HttpClient();
        var r = await client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task New_connection_after_request_and_rst_should_succeed()
    {
        var uri = new Uri($"http://127.0.0.1:{Port}/ping");

        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync("127.0.0.1", Port, CancellationToken);
            var stream = socket.GetStream();

            var request = Encoding.ASCII.GetBytes("GET /ping HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(request, CancellationToken);

            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer, CancellationToken);
            Assert.True(read > 0, "Should have received response");

            socket.LingerState = new LingerOption(true, 0);
        }

        await Task.Delay(500, CancellationToken);

        using var client = new HttpClient();
        var r = await client.GetAsync(uri, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
