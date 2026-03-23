using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;

namespace TurboHttp.Diagnostics;

/// <summary>
/// Central instrumentation class for TurboHttp distributed tracing.
/// Uses <see cref="ActivitySource"/> to emit OpenTelemetry-compatible spans
/// following the HTTP semantic conventions.
/// Consumers subscribe via <c>AddSource("TurboHttp")</c> in the OTel SDK.
/// </summary>
public static class TurboHttpInstrumentation
{
    /// <summary>
    /// The ActivitySource name. Use this value with <c>AddSource</c> to subscribe.
    /// </summary>
    public const string SourceName = "TurboHttp";

    private static readonly string _version =
        typeof(TurboHttpInstrumentation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TurboHttpInstrumentation).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>
    /// The single <see cref="ActivitySource"/> for all TurboHttp spans.
    /// </summary>
    public static ActivitySource Source { get; } = new(SourceName, _version);

    /// <summary>
    /// Starts a root "HTTP {method}" activity for an outgoing HTTP request.
    /// Returns <c>null</c> when no listener is attached (zero overhead).
    /// </summary>
    /// <remarks>
    /// Tags follow OTel HTTP semantic conventions:
    /// <c>http.request.method</c>, <c>url.full</c>, <c>server.address</c>, <c>server.port</c>.
    /// </remarks>
    public static Activity? StartRequest(HttpRequestMessage request)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var uri = request.RequestUri;
        var method = request.Method.Method;

        var activity = Source.StartActivity(
            $"HTTP {method}",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("http.request.method", method);

        if (uri is not null)
        {
            activity.SetTag("url.full", uri.OriginalString);
            activity.SetTag("server.address", uri.Host);
            activity.SetTag("server.port", uri.Port);
        }

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHttp.Connect" activity for a connection attempt.
    /// </summary>
    public static Activity? StartConnect(Uri uri)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            "TurboHttp.Connect",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("server.address", uri.Host);
        activity.SetTag("server.port", uri.Port);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHttp.Redirect" activity for a redirect hop.
    /// </summary>
    public static Activity? StartRedirect(Uri uri, int statusCode)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            "TurboHttp.Redirect",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("http.response.status_code", statusCode);
        activity.SetTag("url.full", uri.OriginalString);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHttp.Retry" activity for a retry attempt.
    /// </summary>
    public static Activity? StartRetry(int attemptNumber)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            "TurboHttp.Retry",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("http.resend_count", attemptNumber);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHttp.CacheLookup" activity for a cache lookup.
    /// </summary>
    public static Activity? StartCacheLookup(Uri uri)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            "TurboHttp.CacheLookup",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("url.full", uri.OriginalString);

        return activity;
    }

    /// <summary>
    /// Enriches an activity with HTTP response information.
    /// Sets <c>http.response.status_code</c>.
    /// </summary>
    public static void SetResponse(Activity activity, HttpResponseMessage response)
    {
        activity.SetTag("http.response.status_code", (int)response.StatusCode);
    }

    /// <summary>
    /// Marks an activity as failed with error details.
    /// Sets <c>otel.status_code</c> to ERROR and records exception attributes.
    /// </summary>
    public static void SetError(Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("otel.status_code", "ERROR");
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
    }
}
