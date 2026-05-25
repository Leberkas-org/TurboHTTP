using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TurboHTTP.Routing;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public sealed class TurboWebApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;

    internal TurboWebApplicationBuilder(string[]? args)
    {
        _hostBuilder = Host.CreateApplicationBuilder(args ?? []);
    }

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
