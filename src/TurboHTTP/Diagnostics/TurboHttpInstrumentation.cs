using System.Diagnostics;
using System.Reflection;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Central instrumentation class for TurboHttp distributed tracing.
/// Uses <see cref="ActivitySource"/> to emit OpenTelemetry-compatible spans
/// following the HTTP semantic conventions.
/// Consumers subscribe via <c>AddSource("TurboHTTP")</c> in the OTel SDK.
/// </summary>
internal static class TurboHttpInstrumentation
{
    public const string SourceName = "TurboHTTP";

    /// <summary>
    /// Key for storing the root <see cref="Activity"/> in <see cref="HttpRequestMessage.Options"/>
    /// so that downstream stages can parent child activities under the request root span.
    /// </summary>
    internal static readonly HttpRequestOptionsKey<Activity> RequestActivityKey
        = new("TurboHTTP.RequestActivity");

    private static readonly string Version =
        typeof(TurboHttpInstrumentation).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TurboHttpInstrumentation).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    private static readonly HashSet<string> StandardMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "POST", "PUT", "DELETE", "CONNECT", "OPTIONS", "TRACE", "PATCH"
    };

    /// <summary>
    /// The single <see cref="ActivitySource"/> for all TurboHttp spans.
    /// </summary>
    public static ActivitySource Source { get; } = new(SourceName, Version);

    /// <summary>
    /// Returns <c>true</c> when any tracing or metrics listener is active.
    /// Checked once at stream materialization time — if no listener is subscribed,
    /// the tracing stage is omitted entirely (zero overhead, no graph node).
    /// </summary>
    public static bool IsTracingActive =>
        Source.HasListeners()
        || TurboTrace.ShouldTrace(TurboTraceCategory.Request, TurboTraceLevel.Info)
        || TurboHttpMetrics.RequestCount.Enabled
        || TurboHttpMetrics.RequestDuration.Enabled;

    /// <summary>
    /// Redacts the query string from a URI for safe inclusion in telemetry.
    /// Returns <c>{scheme}://{authority}{path}?*</c> when a query is present,
    /// or <c>{scheme}://{authority}{path}</c> otherwise. Fragments are always stripped.
    /// </summary>
    public static string RedactUrl(Uri uri)
    {
        var scheme = uri.Scheme;
        var authority = uri.Authority;
        var path = uri.AbsolutePath;

        return string.IsNullOrEmpty(uri.Query)
            ? $"{scheme}://{authority}{path}"
            : $"{scheme}://{authority}{path}?*";
    }

    /// <summary>
    /// Returns the normalized HTTP method per OTel conventions.
    /// Standard methods (GET, HEAD, POST, PUT, DELETE, CONNECT, OPTIONS, TRACE, PATCH)
    /// are returned as-is (uppercased). Non-standard methods return <c>_OTHER</c>.
    /// </summary>
    public static string NormalizeMethod(string method)
    {
        return StandardMethods.Contains(method) ? method.ToUpperInvariant() : "_OTHER";
    }

    /// <summary>
    /// Formats the HTTP protocol version for the <c>network.protocol.version</c> tag.
    /// Returns <c>"1.0"</c>, <c>"1.1"</c>, <c>"2"</c>, or <c>"3"</c>.
    /// </summary>
    public static string FormatProtocolVersion(Version version)
    {
        if (version.Major >= 2)
        {
            return version.Major.ToString();
        }

        return $"{version.Major}.{version.Minor}";
    }

    /// <summary>
    /// Starts a root "TurboHTTP.Request" activity for an outgoing HTTP request.
    /// Returns <c>null</c> when no listener is attached (zero overhead).
    /// </summary>
    /// <remarks>
    /// Tags follow OTel HTTP semantic conventions:
    /// <c>http.request.method</c>, <c>http.request.method_original</c> (if non-standard),
    /// <c>url.full</c> (query redacted), <c>url.scheme</c>,
    /// <c>server.address</c>, <c>server.port</c>.
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
            $"{SourceName}.Request",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        var normalizedMethod = NormalizeMethod(method);
        activity.SetTag("http.request.method", normalizedMethod);
        if (normalizedMethod == "_OTHER")
        {
            activity.SetTag("http.request.method_original", method);
        }

        if (uri is not null)
        {
            activity.SetTag("url.full", RedactUrl(uri));
            activity.SetTag("url.scheme", uri.Scheme);
            activity.SetTag("server.address", uri.Host);
            activity.SetTag("server.port", uri.Port);
        }

        return activity;
    }

    public static void AddRedirectEvent(Activity activity, Uri uri, int statusCode)
    {
        activity.AddEvent(new ActivityEvent("http.redirect",
            tags: new ActivityTagsCollection
            {
                { "http.response.status_code", statusCode },
                { "url.full", RedactUrl(uri) }
            }));
    }

    public static void AddRetryEvent(Activity activity, int attemptNumber)
    {
        activity.AddEvent(new ActivityEvent("http.retry",
            tags: new ActivityTagsCollection
            {
                { "http.resend_count", attemptNumber }
            }));
    }

    public static void AddCacheLookupEvent(Activity activity, Uri uri, bool isHit)
    {
        activity.AddEvent(new ActivityEvent("http.cache_lookup",
            tags: new ActivityTagsCollection
            {
                { "url.full", RedactUrl(uri) },
                { "cache.hit", isHit }
            }));
    }

    /// <summary>
    /// Injects W3C distributed trace context (<c>traceparent</c>, <c>tracestate</c>,
    /// and <c>baggage</c>) into the outgoing request headers using the current
    /// <see cref="DistributedContextPropagator"/>.
    /// This is the same mechanism <see cref="HttpClient"/> uses internally.
    /// </summary>
    public static void InjectTraceContext(Activity activity, HttpRequestMessage request)
    {
        DistributedContextPropagator.Current.Inject(activity, request, static (carrier, name, value) =>
        {
            if (carrier is HttpRequestMessage msg && !string.IsNullOrEmpty(value))
            {
                msg.Headers.TryAddWithoutValidation(name, value);
            }
        });
    }

    /// <summary>
    /// Enriches an activity with HTTP response information.
    /// Sets <c>http.response.status_code</c>, <c>network.protocol.version</c>,
    /// and <c>error.type</c> (for 4xx/5xx responses).
    /// </summary>
    public static void SetResponse(Activity activity, HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        activity.SetTag("http.response.status_code", statusCode);
        activity.SetTag("network.protocol.version", FormatProtocolVersion(response.Version));

        if (statusCode >= 400)
        {
            activity.SetTag("error.type", statusCode.ToString());
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    public static void SetError(Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
    }
}