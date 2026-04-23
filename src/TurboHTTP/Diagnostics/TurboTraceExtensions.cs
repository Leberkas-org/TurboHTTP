using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Servus.Akka.Diagnostics;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Extension methods for registering TurboTrace services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TurboTraceExtensions
{
    /// <summary>
    /// Registers a <see cref="LoggerTraceListener"/> as a singleton <see cref="ITurboTraceListener"/>
    /// and configures <see cref="TurboTrace"/> when the listener is first resolved.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="categories">Bitwise combination of categories to enable.</param>
    /// <param name="minimumLevel">Minimum trace level to accept.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTurboLoggerTracing(
        this IServiceCollection services,
        TurboTraceCategory categories = TurboTraceCategory.All,
        TurboTraceLevel minimumLevel = TurboTraceLevel.Debug)
    {
        services.AddSingleton<ITurboTraceListener>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var listener = new LoggerTraceListener(loggerFactory, categories, minimumLevel);
            TurboTrace.Configure(listener, categories, minimumLevel);
            return listener;
        });
        return services;
    }

    /// <summary>
    /// Registers a custom <see cref="ITurboTraceListener"/> as a singleton and
    /// configures <see cref="TurboTrace"/> immediately.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="listener">The custom trace listener to register.</param>
    /// <param name="categories">Bitwise combination of categories to enable.</param>
    /// <param name="minimumLevel">Minimum trace level to accept.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddTurboTracing(
        this IServiceCollection services,
        ITurboTraceListener listener,
        TurboTraceCategory categories = TurboTraceCategory.All,
        TurboTraceLevel minimumLevel = TurboTraceLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(listener);
        TurboTrace.Configure(listener, categories, minimumLevel);
        services.AddSingleton(listener);
        return services;
    }
}