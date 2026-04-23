using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Servus.Akka.Diagnostics;

/// <summary>
/// Extension methods for registering <see cref="ServusTrace"/> services with
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ServusTraceExtensions
{
    /// <summary>
    /// Registers a <see cref="LoggerServusTraceListener"/> as a singleton
    /// <see cref="IServusTraceListener"/> and configures <see cref="ServusTrace"/>
    /// when the listener is first resolved.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="categories">Bitwise combination of categories to enable.</param>
    /// <param name="minimumLevel">Minimum trace level to accept.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddServusLoggerTracing(
        this IServiceCollection services,
        ServusTraceCategory categories = ServusTraceCategory.All,
        ServusTraceLevel minimumLevel = ServusTraceLevel.Debug)
    {
        services.AddSingleton<IServusTraceListener>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var listener = new LoggerServusTraceListener(loggerFactory, categories, minimumLevel);
            ServusTrace.Configure(listener, categories, minimumLevel);
            return listener;
        });
        return services;
    }

    /// <summary>
    /// Registers a custom <see cref="IServusTraceListener"/> as a singleton and
    /// configures <see cref="ServusTrace"/> immediately.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="listener">The custom trace listener to register.</param>
    /// <param name="categories">Bitwise combination of categories to enable.</param>
    /// <param name="minimumLevel">Minimum trace level to accept.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddServusTraceListener(
        this IServiceCollection services,
        IServusTraceListener listener,
        ServusTraceCategory categories = ServusTraceCategory.All,
        ServusTraceLevel minimumLevel = ServusTraceLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ServusTrace.Configure(listener, categories, minimumLevel);
        services.AddSingleton(listener);
        return services;
    }
}
