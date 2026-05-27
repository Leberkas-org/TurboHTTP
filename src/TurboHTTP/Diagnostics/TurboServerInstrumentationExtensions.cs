using System.Diagnostics;
using Servus.Core.Diagnostics;

namespace TurboHTTP.Diagnostics;

internal static class TurboServerInstrumentationExtensions
{
    private static readonly HashSet<string> StandardMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "POST", "PUT", "DELETE", "CONNECT", "OPTIONS", "TRACE", "PATCH"
    };

    public static bool IsServerTracingActive(this ServusTrace trace)
    {
        return trace.Source.HasListeners()
            || Servus.Core.Servus.Metrics.ActiveConnections().Enabled
            || Servus.Core.Servus.Metrics.ServerActiveRequests().Enabled
            || Servus.Core.Servus.Metrics.ServerRequestDuration().Enabled;
    }

    public static Activity? StartConnectionActivity(this ServusTrace trace, string serverAddress, int serverPort, string networkTransport)
    {
        if (!trace.Source.HasListeners())
        {
            return null;
        }

        var activity = trace.Source.StartActivity(
            "TurboHTTP.Connection",
            ActivityKind.Server);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("server.address", serverAddress);
        activity.SetTag("server.port", serverPort);
        activity.SetTag("network.transport", networkTransport);

        return activity;
    }

    public static void StopConnectionActivity(this ServusTrace _, Activity activity, Exception? error)
    {
        if (error is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error.Message);
            activity.SetTag("error.type", error.GetType().FullName);
        }

        activity.Stop();
    }

    public static Activity? StartRequestActivity(this ServusTrace trace, string method, string path, string scheme)
    {
        if (!trace.Source.HasListeners())
        {
            return null;
        }

        var activity = trace.Source.StartActivity(
            "TurboHTTP.ServerRequest",
            ActivityKind.Server);

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

        activity.SetTag("url.path", path);
        activity.SetTag("url.scheme", scheme);

        return activity;
    }

    public static void SetServerResponse(this ServusTrace _, Activity activity, int statusCode)
    {
        activity.SetTag("http.response.status_code", statusCode);

        if (statusCode >= 400)
        {
            activity.SetTag("error.type", statusCode.ToString());
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    public static void SetServerError(this ServusTrace _, Activity activity, Exception exception)
    {
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
    }

    public static void AddBackpressureEvent(this ServusTrace _, Activity activity, int inflight, int max)
    {
        activity.AddEvent(new ActivityEvent("turbo.backpressure",
            tags: new ActivityTagsCollection
            {
                { "turbo.pipeline.inflight", inflight },
                { "turbo.pipeline.max", max }
            }));
    }

    public static void InjectConnectionTags(ref TagList tags, string serverAddress, int serverPort)
    {
        tags.Add("server.address", serverAddress);
        tags.Add("server.port", serverPort);
    }

    private static string NormalizeMethod(string method)
    {
        return StandardMethods.Contains(method) ? method.ToUpperInvariant() : "_OTHER";
    }
}
