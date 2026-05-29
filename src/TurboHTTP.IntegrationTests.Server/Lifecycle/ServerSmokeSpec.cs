using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Lifecycle;

public sealed class ServerSmokeSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapGet("/hello", () => Results.Ok("Hello from TurboHTTP Server"));
        app.MapPost("/echo", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(CancellationToken);
            return Results.Ok(body);
        });
        app.MapGet("/connection-info", (HttpContext ctx) =>
        {
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return Results.Ok(remoteIp);
        });
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_respond_to_get_request()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/hello"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("Hello from TurboHTTP Server", value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_echo_post_body()
    {
        var payload = "test payload";
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/echo")
        {
            Content = new StringContent(payload)
        };

        var response = await Client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal(payload, value);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_return_404_for_unregistered_route()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/nonexistent"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(Timeout = 15000)]
    public async Task Server_should_expose_remote_ip()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/connection-info"),
            CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        var value = JsonSerializer.Deserialize<string>(body);
        Assert.Equal("127.0.0.1", value);
    }
}
