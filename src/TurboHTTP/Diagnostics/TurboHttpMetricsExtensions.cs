using System.Diagnostics.Metrics;
using Servus.Core.Diagnostics;

namespace TurboHTTP.Diagnostics;

internal static class TurboHttpMetricsExtensions
{
    private static Counter<long>? _requestCount;
    private static Histogram<double>? _requestDuration;
    private static Counter<long>? _cacheLookup;
    private static Counter<long>? _retryCount;
    private static Counter<long>? _redirectCount;
    private static UpDownCounter<long>? _activeRequests;

    public static Counter<long> RequestCount(this ServusMetrics metrics)
    {
        return _requestCount ??= metrics.Meter.CreateCounter<long>(
            "http.client.request.count",
            unit: "{request}",
            description: "Total number of HTTP requests sent");
    }

    public static Histogram<double> RequestDuration(this ServusMetrics metrics)
    {
        return _requestDuration ??= metrics.Meter.CreateHistogram<double>(
            "http.client.request.duration",
            unit: "s",
            description: "Duration of HTTP requests in seconds");
    }

    public static Counter<long> CacheLookup(this ServusMetrics metrics)
    {
        return _cacheLookup ??= metrics.Meter.CreateCounter<long>(
            "http.client.cache.lookup",
            unit: "{lookup}",
            description: "Number of HTTP cache lookups");
    }

    public static Counter<long> RetryCount(this ServusMetrics metrics)
    {
        return _retryCount ??= metrics.Meter.CreateCounter<long>(
            "http.client.retry.count",
            unit: "{retry}",
            description: "Number of HTTP retry attempts");
    }

    public static Counter<long> RedirectCount(this ServusMetrics metrics)
    {
        return _redirectCount ??= metrics.Meter.CreateCounter<long>(
            "http.client.redirect.count",
            unit: "{redirect}",
            description: "Number of HTTP redirect hops");
    }

    public static UpDownCounter<long> ActiveRequests(this ServusMetrics metrics)
    {
        return _activeRequests ??= metrics.Meter.CreateUpDownCounter<long>(
            "http.client.active_requests",
            unit: "{request}",
            description: "Number of currently active HTTP requests");
    }
}
