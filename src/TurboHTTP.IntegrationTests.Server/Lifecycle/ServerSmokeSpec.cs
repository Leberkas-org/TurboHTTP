using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.IntegrationTests.Server.Lifecycle;

public sealed class ServerSmokeSpec : IAsyncLifetime
{
    private WebApplication? _app;
    private int _port;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _port = GetFreePort();
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddTurboKestrel(options =>
        {
            options.Bind(new TcpListenerOptions { Host = "127.0.0.1", Port = (ushort)_port });
        });
        _app = builder.Build();
        RegisterRoutes();
        await _app.StartAsync();
        _client = new HttpClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            _client.Dispose();
        }

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_get_request()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_port}/hello"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello from TurboHTTP Server", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_echo_post_body()
    {
        var payload = "test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/echo")
        {
            Content = new StringContent(payload)
        };

        var response = await _client!.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal(payload, value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_port}/nonexistent"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_expose_remote_ip()
    {
        var response = await _client!.GetAsync(
            new Uri($"http://127.0.0.1:{_port}/connection-info"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("127.0.0.1", value);
    }

    private void RegisterRoutes()
    {
        _app!.MapTurboGet("/hello", () => Results.Ok("Hello from TurboHTTP Server"));
        _app!.MapTurboPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            return Results.Ok(body);
        });
        _app!.MapTurboGet("/connection-info", (HttpContext ctx) =>
        {
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Results.Ok(remoteIp);
        });
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
