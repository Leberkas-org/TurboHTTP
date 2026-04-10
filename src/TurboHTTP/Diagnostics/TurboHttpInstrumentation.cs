using System.Diagnostics;
using System.Reflection;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Central instrumentation class for TurboHttp distributed tracing.
/// Uses <see cref="ActivitySource"/> to emit OpenTelemetry-compatible spans
/// following the HTTP semantic conventions.
/// Consumers subscribe via <c>AddSource("TurboHTTP")</c> in the OTel SDK.
/// </summary>
public static class TurboHttpInstrumentation
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
    /// Returns <c>true</c> when any tracing or metrics listener is active and the
    /// <see cref="TracingBidiStage"/> should be materialized into the pipeline.
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

    /// <summary>
    /// Starts a "TurboHTTP.Connect" activity for a connection attempt.
    /// </summary>
    public static Activity? StartConnect(Uri uri)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.Connect",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("server.address", uri.Host);
        activity.SetTag("server.port", uri.Port);
        activity.SetTag("url.scheme", uri.Scheme);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHTTP.DnsLookup" activity for a DNS resolution.
    /// </summary>
    public static Activity? StartDnsLookup(string hostname)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.DnsLookup",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("dns.question.name", hostname);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHTTP.SocketConnect" activity for a TCP socket connection.
    /// </summary>
    /// <param name="address">The peer IP address (e.g., "93.184.216.34").</param>
    /// <param name="port">The peer port number.</param>
    /// <param name="transport">The transport protocol: <c>"tcp"</c>, <c>"udp"</c>, or <c>"unix"</c>.</param>
    /// <param name="networkType">The network type: <c>"ipv4"</c> or <c>"ipv6"</c>. Null for non-IP transports.</param>
    public static Activity? StartSocketConnect(string address, int port,
        string transport = "tcp", string? networkType = null)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.SocketConnect",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("network.peer.address", address);
        activity.SetTag("network.peer.port", port);
        activity.SetTag("network.transport", transport);
        if (networkType is not null)
        {
            activity.SetTag("network.type", networkType);
        }

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHTTP.TlsHandshake" activity for a TLS negotiation.
    /// After the handshake completes, call <see cref="SetTlsInfo"/> to record
    /// <c>tls.protocol.name</c> and <c>tls.protocol.version</c>.
    /// </summary>
    public static Activity? StartTlsHandshake(string host)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.TlsHandshake",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("server.address", host);

        return activity;
    }

    /// <summary>
    /// Enriches a TLS handshake activity with protocol information after negotiation completes.
    /// </summary>
    /// <param name="activity">The TLS handshake activity.</param>
    /// <param name="protocolName">The protocol name, e.g. <c>"tls"</c> or <c>"ssl"</c>.</param>
    /// <param name="protocolVersion">The protocol version, e.g. <c>"1.2"</c> or <c>"1.3"</c>.</param>
    public static void SetTlsInfo(Activity activity, string protocolName, string protocolVersion)
    {
        activity.SetTag("tls.protocol.name", protocolName);
        activity.SetTag("tls.protocol.version", protocolVersion);
    }

    /// <summary>
    /// Enriches a DNS lookup activity with resolved addresses after resolution completes.
    /// </summary>
    /// <param name="activity">The DNS lookup activity.</param>
    /// <param name="answers">The resolved IP addresses.</param>
    public static void SetDnsAnswers(Activity activity, string[] answers)
    {
        activity.SetTag("dns.answers", answers);
    }

    /// <summary>
    /// Enriches a Connect activity with the resolved peer address after connection is established.
    /// </summary>
    /// <param name="activity">The connect activity.</param>
    /// <param name="address">The peer IP address, e.g. <c>"93.184.216.34"</c>.</param>
    public static void SetNetworkPeerAddress(Activity activity, string address)
    {
        activity.SetTag("network.peer.address", address);
    }

    /// <summary>
    /// Starts a "TurboHTTP.WaitForConnection" activity for time spent waiting
    /// for an available connection from the pool.
    /// </summary>
    public static Activity? StartWaitForConnection(string address, int port)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.WaitForConnection",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("server.address", address);
        activity.SetTag("server.port", port);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHTTP.Redirect" activity for a redirect hop.
    /// </summary>
    public static Activity? StartRedirect(Uri uri, int statusCode)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.Redirect",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("http.response.status_code", statusCode);
        activity.SetTag("url.full", RedactUrl(uri));

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHTTP.Retry" activity for a retry attempt.
    /// </summary>
    public static Activity? StartRetry(int attemptNumber)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.Retry",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("http.resend_count", attemptNumber);

        return activity;
    }

    /// <summary>
    /// Starts a "TurboHTTP.CacheLookup" activity for a cache lookup.
    /// </summary>
    public static Activity? StartCacheLookup(Uri uri)
    {
        if (!Source.HasListeners())
        {
            return null;
        }

        var activity = Source.StartActivity(
            $"{SourceName}.CacheLookup",
            ActivityKind.Client);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("url.full", RedactUrl(uri));

        return activity;
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

    /// <summary>
    /// Marks an activity as failed with error details.
    /// Sets <c>otel.status_code</c> to ERROR, <c>error.type</c> (OTel convention),
    /// and records exception attributes.
    /// </summary>
    public static void SetError(Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("otel.status_code", "ERROR");
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
    }
}