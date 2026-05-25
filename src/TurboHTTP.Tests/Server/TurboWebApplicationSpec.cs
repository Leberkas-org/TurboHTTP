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
}
