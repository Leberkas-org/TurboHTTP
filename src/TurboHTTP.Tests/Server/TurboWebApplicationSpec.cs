using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Routing;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Tests.Server;

public sealed class TurboWebApplicationSpec
{
    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_with_instance_should_register_same_instance()
    {
        var options = new TurboServerOptions
        {
            HandlerTimeout = TimeSpan.FromSeconds(99)
        };

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTurboKestrel(options);
        var host = builder.Build();

        var resolved = host.Services.GetRequiredService<TurboServerOptions>();
        Assert.Same(options, resolved);
        Assert.Equal(TimeSpan.FromSeconds(99), resolved.HandlerTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboUrlCollection_Add_should_delegate_to_options_urls()
    {
        var options = new TurboServerOptions();
        var urls = new TurboUrlCollection(options)
        {
            "http://localhost:5000",
            "https://localhost:5001"
        };

        Assert.Equal(2, urls.Count);
        Assert.Contains("http://localhost:5000", options.Urls);
        Assert.Contains("https://localhost:5001", options.Urls);
    }

    [Fact(Timeout = 5000)]
    public void TurboUrlCollection_should_implement_ICollection()
    {
        var options = new TurboServerOptions();
        var urls = new TurboUrlCollection(options) { "http://localhost:5000" };

        Assert.False(urls.IsReadOnly);
        Assert.Contains("http://localhost:5000", urls);
        Assert.DoesNotContain("http://localhost:9999", urls);
    }

    [Fact(Timeout = 5000)]
    public void TurboWebApplicationBuilder_should_expose_services()
    {
        var builder = new TurboWebApplicationBuilder(null);

        Assert.NotNull(builder.Services);
        Assert.NotNull(builder.Configuration);
        Assert.NotNull(builder.Logging);
        Assert.NotNull(builder.Environment);
        Assert.NotNull(builder.Server);
    }

    [Fact(Timeout = 5000)]
    public void TurboWebApplicationBuilder_Build_should_return_app_with_registered_services()
    {
        var builder = new TurboWebApplicationBuilder(null);
        var app = builder.Build();

        Assert.NotNull(app);
        Assert.NotNull(app.Services);
        Assert.NotNull(app.Services.GetService<TurboRouteTable>());
        Assert.NotNull(app.Services.GetService<TurboPipelineBuilder>());
    }

    [Fact(Timeout = 5000)]
    public void TurboWebApplicationBuilder_Server_should_be_same_instance_in_app()
    {
        var builder = new TurboWebApplicationBuilder(null)
        {
            Server =
            {
                HandlerTimeout = TimeSpan.FromSeconds(99)
            }
        };
        var app = builder.Build();

        var resolved = app.Services.GetRequiredService<TurboServerOptions>();
        Assert.Same(builder.Server, resolved);
        Assert.Equal(TimeSpan.FromSeconds(99), resolved.HandlerTimeout);
    }

    [Fact(Timeout = 5000)]
    public void CreateBuilder_should_return_builder()
    {
        var builder = TurboWebApplication.CreateBuilder();
        Assert.NotNull(builder);
        Assert.NotNull(builder.Services);
    }

    [Fact(Timeout = 5000)]
    public void CreateBuilder_with_args_should_return_builder()
    {
        var builder = TurboWebApplication.CreateBuilder(["--environment", "Development"]);
        Assert.NotNull(builder);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_build_app_with_defaults()
    {
        var app = TurboWebApplication.Create();
        Assert.NotNull(app);
        Assert.NotNull(app.Services);
        Assert.NotNull(app.Configuration);
        Assert.NotNull(app.Environment);
    }

    [Fact(Timeout = 5000)]
    public void App_Urls_should_delegate_to_server_options()
    {
        var app = TurboWebApplication.Create();
        app.Urls.Add("http://localhost:5000");

        var options = app.Services.GetRequiredService<TurboServerOptions>();
        Assert.Contains("http://localhost:5000", options.Urls);
    }

    [Fact(Timeout = 5000)]
    public void App_Logger_should_be_available()
    {
        var app = TurboWebApplication.Create();
        Assert.NotNull(app.Logger);
    }

    [Fact(Timeout = 5000)]
    public void MapGet_extension_should_register_route()
    {
        var app = TurboWebApplication.Create();
        var result = app.MapGet("/test", () => Results.Ok());
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public void MapPost_extension_should_register_route()
    {
        var app = TurboWebApplication.Create();
        var result = app.MapPost("/test", () => Results.Ok());
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public void MapGroup_extension_should_return_group_builder()
    {
        var app = TurboWebApplication.Create();
        var group = app.MapGroup("/api");
        Assert.NotNull(group);
    }

    [Fact(Timeout = 5000)]
    public void MapGet_with_context_handler_should_register_route()
    {
        var app = TurboWebApplication.Create();
        var result = app.MapGet("/test", _ => Task.CompletedTask);
        Assert.NotNull(result);
    }

    [Fact(Timeout = 5000)]
    public void Use_should_return_app_for_chaining()
    {
        var app = TurboWebApplication.Create();

        var result = app.Use(async (ctx, next) => await next(ctx));

        Assert.Same(app, result);
    }

    [Fact(Timeout = 5000)]
    public void Run_should_return_app_for_chaining()
    {
        var app = TurboWebApplication.Create();

        var result = app.Run(_ => Task.CompletedTask);

        Assert.Same(app, result);
    }

    [Fact(Timeout = 5000)]
    public void Map_should_return_app_for_chaining()
    {
        var app = TurboWebApplication.Create();

        var result = app.Map("/branch", branch => branch.Run(_ => Task.CompletedTask));

        Assert.Same(app, result);
    }

    [Fact(Timeout = 5000)]
    public void Pipeline_interface_should_also_work()
    {
        var app = TurboWebApplication.Create();
        ITurboApplicationBuilder pipeline = app;

        var result = pipeline.Use(async (ctx, next) => await next(ctx));

        Assert.Same(app, result);
    }
}