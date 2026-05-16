using System.Net;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Server;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Tests.Server.Binding;

public sealed class DelegateRoutingIntegrationSpec
{
    [Fact(Timeout = 5000)]
    public void MapTurboGet_with_delegate_should_register_route()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();

        host.MapTurboGet("/health", () => "ok");

        var table = host.Services.GetRequiredService<TurboRouteTable>();
        var result = table.Freeze().Match("GET", "/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public async Task MapTurboGet_with_delegate_should_invoke_handler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();

        host.MapTurboGet("/health", () => "healthy");

        var table = host.Services.GetRequiredService<TurboRouteTable>();
        var result = table.Freeze().Match("GET", "/health");
        Assert.True(result.IsMatch);

        var ctx = CreateContext("/health");
        ctx.RequestServices = host.Services;
        var response = await result.Handler!(ctx);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGroup_with_delegate_should_register_prefixed_route()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();

        var api = host.MapTurboGroup("/api");
        api.MapGet("/users", () => "users");

        var table = host.Services.GetRequiredService<TurboRouteTable>();
        Assert.True(table.Freeze().Match("GET", "/api/users").IsMatch);
    }

    private static TurboHttpContext CreateContext(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost" + path);
        var connection = new TurboConnectionInfo("test", null, 0, null, 0);
        return new TurboHttpContext(request, connection,
            Source.Empty<ReadOnlyMemory<byte>>(), CancellationToken.None);
    }
}