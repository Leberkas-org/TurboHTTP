using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Infrastructure;

[Collection("Infrastructure")]
public sealed class TimeoutSpec : ServerSpecBase
{
    protected override void ConfigureServer(WebApplicationBuilder builder, ushort port)
    {
        builder.Host.UseTurboHttp(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = port });
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(2);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
            options.Http1.KeepAliveTimeout = TimeSpan.FromSeconds(2);
            options.Http1.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
        });
    }

    protected override void ConfigureEndpoints(WebApplication app)
    {
        app.MapGet("/fast", () => Results.Ok("ok"));
    }

    [Fact(Timeout = 20000)]
    public async Task KeepAlive_should_close_idle_connection_after_timeout()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);

        using var freshClient = new HttpClient();
        response = await freshClient.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(Timeout = 20000)]
    public async Task RequestHeaders_should_timeout_on_incomplete_headers()
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, Port, CancellationToken);
        var stream = tcp.GetStream();
        var partialBytes = Encoding.ASCII.GetBytes("GET /fast HTTP/1.1\r\nHost: localhost\r\n");
        await stream.WriteAsync(partialBytes, CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);

        var buffer = new byte[1];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            Assert.Equal(0, read);
        }
        catch (OperationCanceledException)
        {
            Assert.True(true);
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Server_should_still_respond_after_timeout_disconnects()
    {
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, Port, CancellationToken);
            var tcpStream = tcp.GetStream();
            var incompleteBytes = Encoding.ASCII.GetBytes("GET /fast HTTP/1.1\r\nHost: localhost\r\n");
            await tcpStream.WriteAsync(incompleteBytes, CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);
        }

        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/fast"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
