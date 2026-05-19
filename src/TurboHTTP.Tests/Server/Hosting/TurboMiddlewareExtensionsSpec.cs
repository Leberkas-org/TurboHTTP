using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Tests.Server.Hosting;

public sealed class TurboMiddlewareExtensionsSpec
{
    [Fact(Timeout = 5000)]
    public async Task UseTurbo_should_register_middleware()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var called = false;
        app.UseTurbo(async (ctx, next) => { called = true; await next(ctx); });

        var pipeline = app.Services.GetRequiredService<TurboPipelineBuilder>().Build();
        await pipeline(TurboTestContextFactory.Create());

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public async Task MapTurboWhen_should_register_conditional_middleware()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var called = false;
        app.MapTurboWhen(
            _ => true,
            branch => branch.Use((ctx, next) => { called = true; return next(ctx); }));

        var pipeline = app.Services.GetRequiredService<TurboPipelineBuilder>().Build();
        await pipeline(TurboTestContextFactory.Create());

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public async Task MapTurbo_should_register_path_branch()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();

        var called = false;
        app.MapTurbo("/admin", branch =>
        {
            branch.Use((ctx, next) => { called = true; return next(ctx); });
        });

        var pipeline = app.Services.GetRequiredService<TurboPipelineBuilder>().Build();
        var ctx = TurboTestContextFactory.Create(uri: "http://localhost/admin/dashboard");
        await pipeline(ctx);

        Assert.True(called);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_should_register_pipeline_builder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTurboKestrel();
        var app = builder.Build();
        Assert.NotNull(app.Services.GetRequiredService<TurboPipelineBuilder>());
    }
}
