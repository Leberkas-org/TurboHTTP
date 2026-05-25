using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Routing;
using TurboHTTP.Server;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Tests.Server;

public sealed class TurboWebApplicationSpec
{
    [Fact(Timeout = 5000)]
    public void AddTurboKestrel_with_instance_should_register_same_instance()
    {
        var options = new TurboServerOptions();
        options.HandlerTimeout = TimeSpan.FromSeconds(99);

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
        var urls = new TurboUrlCollection(options);

        urls.Add("http://localhost:5000");
        urls.Add("https://localhost:5001");

        Assert.Equal(2, urls.Count);
        Assert.Contains("http://localhost:5000", options.Urls);
        Assert.Contains("https://localhost:5001", options.Urls);
    }

    [Fact(Timeout = 5000)]
    public void TurboUrlCollection_should_implement_ICollection()
    {
        var options = new TurboServerOptions();
        var urls = new TurboUrlCollection(options);
        urls.Add("http://localhost:5000");

        Assert.False(urls.IsReadOnly);
        Assert.True(urls.Contains("http://localhost:5000"));
        Assert.False(urls.Contains("http://localhost:9999"));
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
        var builder = new TurboWebApplicationBuilder(null);
        builder.Server.HandlerTimeout = TimeSpan.FromSeconds(99);
        var app = builder.Build();

        var resolved = app.Services.GetRequiredService<TurboServerOptions>();
        Assert.Same(builder.Server, resolved);
        Assert.Equal(TimeSpan.FromSeconds(99), resolved.HandlerTimeout);
    }
}
