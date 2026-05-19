using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Middleware;
using TurboHTTP.Server.Routing;

namespace TurboHTTP.Tests.Server.Hosting;

public sealed class TurboServerHostingSpec
{
    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_should_register_options()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel(opts => opts.GracefulShutdownTimeout = TimeSpan.FromSeconds(60));
        var app = builder.Build();
        var options = app.Services.GetRequiredService<TurboServerOptions>();
        Assert.Equal(TimeSpan.FromSeconds(60), options.GracefulShutdownTimeout);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_should_register_route_table()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();
        Assert.NotNull(app.Services.GetRequiredService<TurboRouteTable>());
    }

    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_should_register_pipeline_builder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();
        Assert.NotNull(app.Services.GetRequiredService<TurboPipelineBuilder>());
    }

    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_should_not_register_separate_entity_route_table()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();
        Assert.NotNull(app.Services.GetRequiredService<TurboRouteTable>());
    }
}