using System;
using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHttp.Client;
using TurboHttp.Middleware;

namespace TurboHttp.Hosting;

/// <summary>
/// Extension methods for registering TurboHttp services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TurboClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ITurboHttpClientFactory"/> as a singleton in the DI container.
    /// If an <see cref="ActorSystem"/> is already registered it will be reused;
    /// otherwise a new actor system named <c>turbohttp</c> is created automatically.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Delegate to configure <see cref="TurboClientOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTurboHttpClientFactory(this IServiceCollection services,
        Action<TurboClientOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ITurboHttpClientFactory>(provider =>
        {
            var system = provider.GetService<ActorSystem>();
            if (system is null)
            {
                // start our own local ActorSystem
                system = ActorSystem.Create("turbohttp");
                system.Log.Info("Created new Akka.NET ActorSystem {0} - none found in IServiceCollection", system.Name);
            }

            var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
            return new TurboHttpClientFactory(options, system);
        });

        return services;
    }

    /// <summary>
    /// Registers a named TurboHttp client and returns an <see cref="ITurboHttpClientBuilder"/>
    /// for further configuration. <see cref="ITurboHttpClientFactory"/> is registered as a
    /// singleton the first time this method is called — subsequent calls are idempotent.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="name">The logical name of the client.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this named client.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient(this IServiceCollection services,
        string name, Action<TurboClientOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure<TurboClientOptions>(name, configure);
        }

        services.TryAddSingleton<ITurboHttpClientFactory>(provider =>
        {
            var system = provider.GetService<ActorSystem>();
            if (system is null)
            {
                // start our own local ActorSystem
                system = ActorSystem.Create("turbohttp");
                system.Log.Info("Created new Akka.NET ActorSystem {0} - none found in IServiceCollection", system.Name);
            }

            var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
            return new TurboHttpClientFactory(options, system);
        });

        return new TurboHttpClientBuilder(name, services);
    }

    /// <summary>
    /// Registers the default (unnamed) TurboHttp client. Delegates to
    /// <see cref="AddTurboHttpClient(IServiceCollection, string, Action{TurboClientOptions}?)"/>
    /// with <see cref="string.Empty"/> as the name.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/>.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        => services.AddTurboHttpClient(string.Empty, configure);
}