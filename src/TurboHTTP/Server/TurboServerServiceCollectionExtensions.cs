using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TurboHTTP.Routing;
using TurboHTTP.Server.Hosting;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public static class TurboServerServiceCollectionExtensions
{
    public static IServiceCollection AddTurboServer(
        this IServiceCollection services,
        Action<TurboServerOptions>? configure = null)
    {
        services.AddSingleton<IServer, TurboServer>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return services;
    }

    public static IServiceCollection AddTurboKestrel(
        this IServiceCollection services,
        Action<TurboServerOptions>? configure = null)
    {
        var options = new TurboServerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<TurboRouteTable>();
        services.TryAddSingleton<TurboPipelineBuilder>();
        services.AddTurboServer();

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
        services.AddTurboServer();

        return services;
    }

    internal static IServiceCollection AddTurboKestrel(
        this IServiceCollection services,
        TurboServerOptions options)
    {
        services.TryAddSingleton(options);
        services.TryAddSingleton<TurboRouteTable>();
        services.TryAddSingleton<TurboPipelineBuilder>();
        services.AddTurboServer();

        return services;
    }
}
