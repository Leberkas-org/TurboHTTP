using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Servus.Core.Diagnostics;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Extension methods for registering TurboTrace services with <see cref="IServiceCollection"/>.
/// </summary>
public static class TurboTraceExtensions
{
    public static IServiceCollection AddTurboLoggerTracing(
        this IServiceCollection services,
        TraceLevel minimumLevel = TraceLevel.Debug,
        Func<string, bool>? categoryFilter = null)
    {
        services.AddSingleton<IServusTraceListener>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var listener = new LoggerTraceListener(loggerFactory);
            Servus.Core.Servus.Tracing.Configure(listener, minimumLevel, categoryFilter);
            return listener;
        });
        return services;
    }

    public static IServiceCollection AddTurboTracing(
        this IServiceCollection services,
        IServusTraceListener listener,
        TraceLevel minimumLevel = TraceLevel.Debug,
        Func<string, bool>? categoryFilter = null)
    {
        ArgumentNullException.ThrowIfNull(listener);
        Servus.Core.Servus.Tracing.Configure(listener, minimumLevel, categoryFilter);
        services.AddSingleton(listener);
        return services;
    }

    public static TracerProviderBuilder AddTurboHttpInstrumentation(this TracerProviderBuilder builder)
    {
        return builder
            .AddSource(Servus.Core.Servus.Tracing.Source.Name);
    }

    public static MeterProviderBuilder AddTurboHttpInstrumentation(this MeterProviderBuilder builder)
    {
        return builder
            .AddMeter(Servus.Core.Servus.Metrics.Meter.Name);
    }

    public static TracerProviderBuilder AddTurboServerInstrumentation(this TracerProviderBuilder builder)
    {
        return builder
            .AddSource(Servus.Core.Servus.Tracing.Source.Name);
    }

    public static MeterProviderBuilder AddTurboServerInstrumentation(this MeterProviderBuilder builder)
    {
        return builder
            .AddMeter(Servus.Core.Servus.Metrics.Meter.Name);
    }
}