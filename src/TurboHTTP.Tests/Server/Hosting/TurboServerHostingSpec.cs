using Akka;
using Akka.Streams.Dsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Server;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Tests.Server.Hosting;

public sealed class TurboServerHostingSpec
{
    [Fact(Timeout = 5000)]
    public void AddTurboServer_should_register_options()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer(options => { options.GracefulShutdownTimeout = TimeSpan.FromSeconds(60); });
        var host = builder.Build();
        var options = host.Services.GetRequiredService<TurboServerOptions>();
        Assert.Equal(TimeSpan.FromSeconds(60), options.GracefulShutdownTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboServer_should_register_route_table()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();
        var table = host.Services.GetRequiredService<TurboRouteTable>();
        Assert.NotNull(table);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboServer_should_register_middleware_registry()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();
        var registry = host.Services.GetRequiredService<TurboMiddlewareRegistry>();
        Assert.NotNull(registry);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGet_should_add_route_to_table()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();
        host.MapTurboGet("/health", _ => Task.FromResult(TurboResults.Ok()));
        var table = host.Services.GetRequiredService<TurboRouteTable>();
        var frozen = table.Freeze();
        var result = frozen.Match("GET", "/health");
        Assert.True(result.IsMatch);
    }

    [Fact(Timeout = 5000)]
    public void UseTurboMiddleware_should_add_to_registry()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();
        host.UseTurboMiddleware<FakeStage>();
        var registry = host.Services.GetRequiredService<TurboMiddlewareRegistry>();
        var stages = registry.Resolve(host.Services);
        Assert.Single(stages);
    }

    [Fact(Timeout = 5000)]
    public void MapTurboGroup_should_prefix_routes()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboServer();
        var host = builder.Build();
        var api = host.MapTurboGroup("/api");
        api.MapGet("/users", _ => Task.FromResult(TurboResults.Ok()));
        var table = host.Services.GetRequiredService<TurboRouteTable>();
        var frozen = table.Freeze();
        Assert.True(frozen.Match("GET", "/api/users").IsMatch);
    }

    private sealed class FakeStage : IServerBidiStage
    {
        public BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>
            Create(IServiceProvider services)
            => BidiFlow.FromFlows(
                Flow.Create<HttpRequestMessage>(),
                Flow.Create<HttpResponseMessage>());
    }
}