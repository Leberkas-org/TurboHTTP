using System.Diagnostics.Metrics;
using System.Reflection;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Central metrics class for TurboHttp instrumentation.
/// Uses <see cref="Meter"/> to emit OpenTelemetry-compatible metrics
/// following the HTTP semantic conventions.
/// Consumers subscribe via <c>AddMeter("TurboHTTP")</c> in the OTel SDK.
/// </summary>
public static class TurboHttpMetrics
{
    /// <summary>
    /// The Meter name. Use this value with <c>AddMeter</c> to subscribe.
    /// </summary>
    public const string MeterName = "TurboHTTP";

    private static readonly string Version =
        typeof(TurboHttpMetrics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TurboHttpMetrics).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// The single <see cref="Meter"/> for all TurboHttp metrics.
    /// </summary>
    public static Meter Meter { get; } = new(MeterName, Version);

    /// <summary>
    /// Total requests sent.
    /// Tags: <c>http.request.method</c>, <c>http.response.status_code</c>, <c>server.address</c>.
    /// </summary>
    public static Counter<long> RequestCount { get; } =
        Meter.CreateCounter<long>(
            "http.client.request.count",
            unit: "{request}",
            description: "Total number of HTTP requests sent");

    /// <summary>
    /// Request duration in seconds.
    /// Tags: <c>http.request.method</c>, <c>http.response.status_code</c>.
    /// </summary>
    public static Histogram<double> RequestDuration { get; } =
        Meter.CreateHistogram<double>(
            "http.client.request.duration",
            unit: "s",
            description: "Duration of HTTP requests in seconds");

    /// <summary>
    /// Total cache hits.
    /// </summary>
    public static Counter<long> CacheHit { get; } =
        Meter.CreateCounter<long>(
            "http.client.cache.hit",
            unit: "{hit}",
            description: "Number of HTTP cache hits");

    /// <summary>
    /// Total cache misses.
    /// </summary>
    public static Counter<long> CacheMiss { get; } =
        Meter.CreateCounter<long>(
            "http.client.cache.miss",
            unit: "{miss}",
            description: "Number of HTTP cache misses");

    /// <summary>
    /// Total retry attempts.
    /// Tags: <c>http.request.method</c>, <c>server.address</c>.
    /// </summary>
    public static Counter<long> RetryCount { get; } =
        Meter.CreateCounter<long>(
            "http.client.retry.count",
            unit: "{retry}",
            description: "Number of HTTP retry attempts");

    /// <summary>
    /// Total redirect hops.
    /// Tags: <c>http.response.status_code</c>.
    /// </summary>
    public static Counter<long> RedirectCount { get; } =
        Meter.CreateCounter<long>(
            "http.client.redirect.count",
            unit: "{redirect}",
            description: "Number of HTTP redirect hops");

    /// <summary>
    /// Connection lifetime in seconds.
    /// </summary>
    public static Histogram<double> ConnectionDuration { get; } =
        Meter.CreateHistogram<double>(
            "http.client.connection.duration",
            unit: "s",
            description: "Duration of HTTP connections in seconds");

    /// <summary>
    /// Currently active connections.
    /// </summary>
    public static UpDownCounter<long> ConnectionActive { get; } =
        Meter.CreateUpDownCounter<long>(
            "http.client.connection.active",
            unit: "{connection}",
            description: "Number of currently active HTTP connections");

    /// <summary>
    /// Currently idle connections.
    /// </summary>
    public static UpDownCounter<long> ConnectionIdle { get; } =
        Meter.CreateUpDownCounter<long>(
            "http.client.connection.idle",
            unit: "{connection}",
            description: "Number of currently idle HTTP connections");

    /// <summary>
    /// Pipeline stall events detected by <c>PipelineHealthMonitorStage</c>.
    /// Tags: <c>stage</c>, <c>direction</c>.
    /// </summary>
    public static Counter<long> PipelineStall { get; } =
        Meter.CreateCounter<long>(
            "turbohttp.pipeline.stall",
            unit: "{stall}",
            description: "Number of pipeline stall events detected");
}
