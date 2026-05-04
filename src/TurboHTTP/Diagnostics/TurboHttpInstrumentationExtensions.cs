using System.Diagnostics;
using Servus.Core.Diagnostics;

namespace TurboHTTP.Diagnostics;

internal static class TurboHttpInstrumentationExtensions
{
    internal static readonly HttpRequestOptionsKey<Activity> RequestActivityKey
        = new("TurboHTTP.RequestActivity");

    private static readonly HashSet<string> StandardMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "POST", "PUT", "DELETE", "CONNECT", "OPTIONS", "TRACE", "PATCH"
    };

    public static bool IsHttpTracingActive(this ServusTrace trace)
    {
        return trace.Source.HasListeners()
            || Servus.Core.Servus.Metrics.RequestCount().Enabled
            || Servus.Core.Servus.Metrics.RequestDuration().Enabled;
    }

    public static Activity? StartRequest(this ServusTrace trace, HttpRequestMessage request)
    {
        if (!trace.Source.HasListeners())
        {
            return null;
        }

        var uri = request.RequestUri;
        var method = request.Method.Method;

        var activity = trace.Source.StartActivity(
            "TurboHTTP.Request",
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

    public static void AddRedirectEvent(this ServusTrace _, Activity activity, Uri uri, int statusCode)
    {
        activity.AddEvent(new ActivityEvent("http.redirect",
            tags: new ActivityTagsCollection
            {
                { "http.response.status_code", statusCode },
                { "url.full", RedactUrl(uri) }
            }));
    }

    public static void AddRetryEvent(this ServusTrace _, Activity activity, int attemptNumber)
    {
        activity.AddEvent(new ActivityEvent("http.retry",
            tags: new ActivityTagsCollection
            {
                { "http.resend_count", attemptNumber }
            }));
    }

    public static void AddCacheLookupEvent(this ServusTrace _, Activity activity, Uri uri, bool isHit)
    {
        activity.AddEvent(new ActivityEvent("http.cache_lookup",
            tags: new ActivityTagsCollection
            {
                { "url.full", RedactUrl(uri) },
                { "cache.hit", isHit }
            }));
    }

    public static void InjectTraceContext(this ServusTrace _, Activity activity, HttpRequestMessage request)
    {
        DistributedContextPropagator.Current.Inject(activity, request, static (carrier, name, value) =>
        {
            if (carrier is HttpRequestMessage msg && !string.IsNullOrEmpty(value))
            {
                msg.Headers.TryAddWithoutValidation(name, value);
            }
        });
    }

    public static void SetHttpResponse(this ServusTrace _, Activity activity, HttpResponseMessage response)
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

    public static void SetHttpError(this ServusTrace _, Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
    }

    public static string RedactUrl(Uri uri)
    {
        var scheme = uri.Scheme;
        var authority = uri.Authority;
        var path = uri.AbsolutePath;

        return string.IsNullOrEmpty(uri.Query)
            ? string.Concat(scheme, "://", authority, path)
            : string.Concat(scheme, "://", authority, path, "?*");
    }

    public static string NormalizeMethod(string method)
    {
        return StandardMethods.Contains(method) ? method.ToUpperInvariant() : "_OTHER";
    }

    public static string FormatProtocolVersion(Version version)
    {
        if (version.Major >= 2)
        {
            return version.Major.ToString();
        }

        return string.Concat(version.Major.ToString(), ".", version.Minor.ToString());
    }
}
