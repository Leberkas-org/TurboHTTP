using System.Diagnostics;
using TurboHTTP.Diagnostics;
using static Servus.Core.Servus;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboServerInstrumentationSpec : IDisposable
{
    private readonly List<Activity> _activities = [];
    private readonly ActivityListener _listener;

    public TurboServerInstrumentationSpec()
    {
        var sourceName = Tracing.Source.Name;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        foreach (var activity in _activities)
        {
            if (!activity.IsStopped)
            {
                activity.Stop();
            }
        }
    }

    [Fact(Timeout = 5000)]
    public void IsServerTracingActive_should_return_true_when_listener_present()
    {
        Assert.True(Tracing.IsServerTracingActive());
    }

    [Fact(Timeout = 5000)]
    public void StartConnectionActivity_should_create_server_activity()
    {
        var activity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp");

        Assert.NotNull(activity);
        Assert.Equal("TurboHTTP.Connection", activity.OperationName);
        Assert.Equal(ActivityKind.Server, activity.Kind);
    }

    [Fact(Timeout = 5000)]
    public void StartConnectionActivity_should_set_server_tags()
    {
        var activity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;

        Assert.Equal("127.0.0.1", activity.GetTagItem("server.address"));
        Assert.Equal(8080, activity.GetTagItem("server.port"));
        Assert.Equal("tcp", activity.GetTagItem("network.transport"));
    }

    [Fact(Timeout = 5000)]
    public void StopConnectionActivity_should_stop_activity()
    {
        var activity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;

        Tracing.StopConnectionActivity(activity, null);

        Assert.True(activity.IsStopped);
    }

    [Fact(Timeout = 5000)]
    public void StopConnectionActivity_should_set_error_on_exception()
    {
        var activity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;

        Tracing.StopConnectionActivity(activity, new IOException("Connection reset"));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(IOException).FullName, activity.GetTagItem("error.type"));
    }

    [Fact(Timeout = 5000)]
    public void StartRequestActivity_should_create_child_of_connection()
    {
        var connActivity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;

        var reqActivity = Tracing.StartRequestActivity("GET", "/api/data", "https")!;

        Assert.Equal("TurboHTTP.ServerRequest", reqActivity.OperationName);
        Assert.Equal(ActivityKind.Server, reqActivity.Kind);
        Assert.Equal(connActivity.TraceId, reqActivity.TraceId);

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void StartRequestActivity_should_set_http_tags()
    {
        _ = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("POST", "/api/submit", "https")!;

        Assert.Equal("POST", reqActivity.GetTagItem("http.request.method"));
        Assert.Equal("/api/submit", reqActivity.GetTagItem("url.path"));
        Assert.Equal("https", reqActivity.GetTagItem("url.scheme"));

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void SetServerResponse_should_set_status_code_tag()
    {
        _ = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("GET", "/", "http")!;

        Tracing.SetServerResponse(reqActivity, 200);

        Assert.Equal(200, reqActivity.GetTagItem("http.response.status_code"));
        Assert.NotEqual(ActivityStatusCode.Error, reqActivity.Status);

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void SetServerResponse_should_set_error_for_5xx()
    {
        _ = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("GET", "/", "http")!;

        Tracing.SetServerResponse(reqActivity, 500);

        Assert.Equal(500, reqActivity.GetTagItem("http.response.status_code"));
        Assert.Equal("500", reqActivity.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, reqActivity.Status);

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void SetServerResponse_should_set_error_for_4xx()
    {
        _ = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("GET", "/", "http")!;

        Tracing.SetServerResponse(reqActivity, 404);

        Assert.Equal(ActivityStatusCode.Error, reqActivity.Status);

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void SetServerError_should_set_exception_details()
    {
        _ = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("GET", "/", "http")!;

        Tracing.SetServerError(reqActivity, new InvalidOperationException("Pipeline broken"));

        Assert.Equal(ActivityStatusCode.Error, reqActivity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, reqActivity.GetTagItem("error.type"));
        Assert.Equal(typeof(InvalidOperationException).FullName, reqActivity.GetTagItem("exception.type"));
        Assert.Equal("Pipeline broken", reqActivity.GetTagItem("exception.message"));

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void AddBackpressureEvent_should_add_event_with_tags()
    {
        var connActivity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;

        Tracing.AddBackpressureEvent(connActivity, 82, 100);

        var evt = Assert.Single(connActivity.Events, e => e.Name == "turbo.backpressure");
        Assert.Equal(82, evt.Tags.First(t => t.Key == "turbo.pipeline.inflight").Value);
        Assert.Equal(100, evt.Tags.First(t => t.Key == "turbo.pipeline.max").Value);
    }

    [Fact(Timeout = 5000)]
    public void InjectConnectionTags_should_set_server_address_and_port()
    {
        var tags = new TagList();
        TurboServerInstrumentationExtensions.InjectConnectionTags(ref tags, "10.0.0.1", 443);

        Assert.Equal("10.0.0.1", tags.Single(t => t.Key == "server.address").Value);
        Assert.Equal(443, tags.Single(t => t.Key == "server.port").Value);
    }

    [Fact(Timeout = 5000)]
    public void FullLifecycle_connection_with_request()
    {
        _activities.Clear();

        var connActivity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("GET", "/health", "http")!;

        Tracing.SetServerResponse(reqActivity, 200);
        reqActivity.Stop();

        Tracing.StopConnectionActivity(connActivity, null);

        Assert.Equal(2, _activities.Count);
        Assert.True(connActivity.IsStopped);
        Assert.True(reqActivity.IsStopped);
        Assert.Equal(connActivity.TraceId, reqActivity.TraceId);
    }

    [Fact(Timeout = 5000)]
    public void FullLifecycle_connection_with_error()
    {
        _activities.Clear();

        var connActivity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("POST", "/api", "https")!;

        Tracing.SetServerError(reqActivity, new TimeoutException("Handler timed out"));
        reqActivity.Stop();

        Tracing.StopConnectionActivity(connActivity, null);

        Assert.Equal(2, _activities.Count);
        Assert.Equal(ActivityStatusCode.Error, reqActivity.Status);
        Assert.NotEqual(ActivityStatusCode.Error, connActivity.Status);
    }

    [Fact(Timeout = 5000)]
    public void StartRequestActivity_should_normalize_nonstandard_method()
    {
        _ = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp")!;
        var reqActivity = Tracing.StartRequestActivity("PURGE", "/cache", "http")!;

        Assert.Equal("_OTHER", reqActivity.GetTagItem("http.request.method"));
        Assert.Equal("PURGE", reqActivity.GetTagItem("http.request.method_original"));

        reqActivity.Stop();
    }

    [Fact(Timeout = 5000)]
    public void StartConnectionActivity_should_return_null_when_no_listener()
    {
        _listener.Dispose();

        var activity = Tracing.StartConnectionActivity("127.0.0.1", 8080, "tcp");

        Assert.Null(activity);
    }
}