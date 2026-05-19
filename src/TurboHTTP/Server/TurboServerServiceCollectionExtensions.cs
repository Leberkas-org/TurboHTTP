using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TurboHTTP.Routing;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public static class TurboServerServiceCollectionExtensions
{
    public static IServiceCollection AddTurboKestrel(
        this IServiceCollection services,
        Action<TurboServerOptions>? configure = null)
    {
        var options = new TurboServerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<TurboRouteTable>();
        services.TryAddSingleton<TurboPipelineBuilder>();
        services.TryAddSingleton<IHostedService, TurboServerHostedService>();

        return services;
    }

    public static IServiceCollection AddTurboKestrel(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TurboServerOptions>? configure = null)
    {
        var options = new TurboServerOptions();
        TurboKestrelConfigurationBinder.Bind(options, configuration.GetSection("TurboKestrel"));
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<TurboRouteTable>();
        services.TryAddSingleton<TurboPipelineBuilder>();
        services.TryAddSingleton<IHostedService, TurboServerHostedService>();

        return services;
    }
}
