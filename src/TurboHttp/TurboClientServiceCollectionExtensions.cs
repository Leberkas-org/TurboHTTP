using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Logger.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TurboHttp;

/// <summary>
/// Extension methods for registering TurboHttp services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TurboClientServiceCollectionExtensions
{
    private static readonly Config LoggingHocon = ConfigurationFactory.ParseString(
        """akka.loggers = ["Akka.Logger.Extensions.Logging.LoggingLogger, Akka.Logger.Extensions.Logging"]""");

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
        services.AddOptions();

        if (configure is not null)
        {
            services.Configure(name, configure);
        }

        services.TryAddSingleton<ITurboHttpClientFactory>(provider =>
        {
            var system = provider.GetService<ActorSystem>();
            if (system is null)
            {
                var loggerFactory = provider.GetService<ILoggerFactory>();
                if (loggerFactory is not null)
                {
                    // Bridge Akka logging to Microsoft.Extensions.Logging
                    LoggingLogger.LoggerFactory = loggerFactory;
                    system = ActorSystem.Create("turbohttp", LoggingHocon);
                }
                else
                {
                    // Standalone usage — fallback to Akka's default logger
                    system = ActorSystem.Create("turbohttp");
                }

                system.Log.Info("Created new Akka.NET ActorSystem {0} - none found in IServiceCollection", system.Name);
            }

            var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
            var descriptors = provider.GetRequiredService<IOptionsMonitor<TurboClientDescriptor>>();
            return new TurboHttpClientFactory(options, descriptors, provider, system);
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

    /// <summary>
    /// Registers a typed TurboHttp client where <typeparamref name="TClient"/> is both the service
    /// and implementation type. The client name is <c>typeof(TClient).Name</c>.
    /// <typeparamref name="TClient"/> is registered as a Transient service resolved via
    /// <see cref="ITurboHttpClientFactory"/>.
    /// </summary>
    /// <typeparam name="TClient">The typed client type.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this client.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient<TClient>(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        where TClient : class
    {
        var name = typeof(TClient).Name;
        services.AddTransient<TClient>(sp => (TClient)sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(name));
        return services.AddTurboHttpClient(name, configure);
    }

    /// <summary>
    /// Registers a typed TurboHttp client with a separate interface and implementation.
    /// The client name is <c>typeof(TClient).Name</c>.
    /// Both <typeparamref name="TClient"/> and <typeparamref name="TImpl"/> are registered as
    /// Transient services resolved via <see cref="ITurboHttpClientFactory"/>.
    /// </summary>
    /// <typeparam name="TClient">The service/interface type.</typeparam>
    /// <typeparam name="TImpl">The implementation type; must implement <typeparamref name="TClient"/>.</typeparam>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TurboClientOptions"/> for this client.</param>
    /// <returns>An <see cref="ITurboHttpClientBuilder"/> for further configuration.</returns>
    public static ITurboHttpClientBuilder AddTurboHttpClient<TClient, TImpl>(this IServiceCollection services,
        Action<TurboClientOptions>? configure = null)
        where TClient : class
        where TImpl : class, TClient
    {
        var name = typeof(TClient).Name;
        services.AddTransient<TClient>(sp => (TClient)sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(name));
        services.AddTransient<TImpl>(sp => (TImpl)sp.GetRequiredService<ITurboHttpClientFactory>().CreateClient(name));
        return services.AddTurboHttpClient(name, configure);
    }
    
    public static ITurboHttpClient CreateClient(this ITurboHttpClientFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateClient(string.Empty);
    }
}