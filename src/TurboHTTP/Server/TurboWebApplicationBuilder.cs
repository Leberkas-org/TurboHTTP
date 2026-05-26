using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Routing;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public sealed class TurboHostBuilder
{
    private readonly HostApplicationBuilder _inner;

    internal TurboHostBuilder(HostApplicationBuilder inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public TurboHostBuilder ConfigureHostOptions(Action<HostOptions> configure)
    {
        _inner.Services.Configure(configure);
        return this;
    }

    public TurboHostBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
    {
        configure(_inner.Configuration);
        return this;
    }

    public TurboHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(_inner.Services);
        return this;
    }
}

public sealed class TurboWebApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;
    private readonly TurboHostBuilder _hostBuilderFacade;

    internal TurboWebApplicationBuilder(string[]? args)
    {
        _hostBuilder = global::Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args ?? []);
        _hostBuilderFacade = new TurboHostBuilder(_hostBuilder);
    }

    public TurboHostBuilder Host => _hostBuilderFacade;

    public IServiceCollection Services => _hostBuilder.Services;

    public ConfigurationManager Configuration => _hostBuilder.Configuration;

    public ILoggingBuilder Logging => _hostBuilder.Logging;

    public IHostEnvironment Environment => _hostBuilder.Environment;

    public TurboServerOptions Server { get; } = new();

    public TurboWebApplication Build()
    {
        Services.AddTurboKestrel(Server);

        var host = _hostBuilder.Build();
        var routeTable = host.Services.GetRequiredService<TurboRouteTable>();
        var pipelineBuilder = host.Services.GetRequiredService<TurboPipelineBuilder>();

        return new TurboWebApplication(host, routeTable, pipelineBuilder);
    }
}
