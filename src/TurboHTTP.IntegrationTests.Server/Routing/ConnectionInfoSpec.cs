using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Servus.Akka.Transport;
using TurboHTTP.IntegrationTests.Server.Shared;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.IntegrationTests.Server.Routing;

public sealed class ConnectionInfoSpec(ActorSystemFixture systemFixture) : ServerSpecBase(systemFixture)
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
        app.MapGet("/connection", (HttpContext ctx) => Results.Ok(new
        {
            remoteIp = ctx.Connection.RemoteIpAddress?.ToString(),
            remotePort = ctx.Connection.RemotePort,
            localIp = ctx.Connection.LocalIpAddress?.ToString(),
            localPort = ctx.Connection.LocalPort
        }));

        app.MapGet("/protocol", (HttpContext ctx) => Results.Ok(new { protocol = ctx.Request.Protocol }));
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_expose_local_ip_and_port()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/connection"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal("127.0.0.1", json.RootElement.GetProperty("localIp").GetString());
        Assert.Equal(Port, json.RootElement.GetProperty("localPort").GetInt32());
    }

    [Fact(Timeout = 15000)]
    public async Task Connection_should_expose_remote_ip()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/connection"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal("127.0.0.1", json.RootElement.GetProperty("remoteIp").GetString());
        Assert.True(json.RootElement.GetProperty("remotePort").GetInt32() > 0);
    }

    [Fact(Timeout = 15000)]
    public async Task Request_should_expose_protocol_version()
    {
        var response = await Client.GetAsync(
            new Uri($"http://127.0.0.1:{Port}/protocol"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken));

        var protocol = json.RootElement.GetProperty("protocol").GetString();
        Assert.Contains("HTTP/1", protocol);
    }
}
