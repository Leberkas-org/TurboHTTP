using System.Diagnostics;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Outermost bidirectional stage that creates and manages the root "TurboHTTP.Request"
/// <see cref="Activity"/> for each request flowing through the pipeline.
/// <para>
/// Request direction (In1→Out1): starts a root activity via
/// <see cref="TurboHttpInstrumentation.StartRequest"/> and stores it in
/// <see cref="HttpRequestMessage.Options"/> so downstream stages can parent child activities.
/// </para>
/// <para>
/// Response direction (In2→Out2): retrieves the root activity from the response's
/// request message, sets <c>http.response.status_code</c>, and stops the activity.
/// </para>
/// <para>
/// When no <see cref="ActivityListener"/> is subscribed, <see cref="ActivitySource"/> returns
/// <c>null</c> and the stage is a zero-overhead pass-through.
/// </para>
/// </summary>
internal sealed class TracingBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpRequestMessage> _inRequest = new("Tracing.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Tracing.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Tracing.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Tracing.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape
    {
        get;
    }

    public TracingBidiStage()
    {
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new TracingBidiLogic(this);

    private sealed class TracingBidiLogic : GraphStageLogic, IFeatureStageOperations
    {
        private readonly TracingBidiStage _stage;
        private readonly TracingBidiProcessor _processor;

        public TracingBidiLogic(TracingBidiStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _processor = new TracingBidiProcessor(this);

            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    _processor.OnRequestPush(request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    _processor.OnRequestUpstreamFailure(ex);
                    Fail(_stage._outRequest, ex);
                });

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    _processor.OnResponsePush(response);
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    _processor.OnResponseUpstreamFailure(ex);
                    Fail(_stage._outResponse, ex);
                });

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        public override void PostStop() => _processor.PostStop();

        void IFeatureStageOperations.OnPushRequest(HttpRequestMessage request)
        {
            Push(_stage._outRequest, request);
        }

        void IFeatureStageOperations.OnPushResponse(HttpResponseMessage response)
        {
            Push(_stage._outResponse, response);
        }

        void IFeatureStageOperations.OnSignalPullRequest()
        {
        }

        void IFeatureStageOperations.OnSignalPullResponse()
        {
        }

        void IFeatureStageOperations.OnCompleteStage()
        {
        }

        void IFeatureStageOperations.OnScheduleTimer(string key, TimeSpan delay)
        {
        }

        void IFeatureStageOperations.OnCancelTimer(string key)
        {
        }

        ILoggingAdapter IFeatureStageOperations.Log => Log;
    }
}

internal sealed class TracingBidiProcessor
{
    private static readonly HttpRequestOptionsKey<long> RequestTimestampKey = new("TurboHTTP.RequestTimestamp");

    private readonly IFeatureStageOperations _ops;
    private Activity? _currentActivity;
    private HttpRequestMessage? _currentRequest;

    public TracingBidiProcessor(IFeatureStageOperations ops)
    {
        _ops = ops;
    }

    public void OnRequestPush(HttpRequestMessage request)
    {
        var activity = TurboHttpInstrumentation.StartRequest(request);
        if (activity is not null)
        {
            request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);
            TurboHttpInstrumentation.InjectTraceContext(activity, request);
            _currentActivity = activity;
        }

        var method = request.Method.Method;
        var uri = request.RequestUri?.OriginalString ?? "";
        TurboTrace.Request.Info(_ops, "Request started: {0} {1}", method, uri);

        _currentRequest = request;

        RecordActiveRequestStart(request);

        if (TurboHttpInstrumentation.Source.HasListeners()
            || TurboTrace.ShouldTrace(TurboTraceCategory.Request, TurboTraceLevel.Info)
            || TurboHttpMetrics.RequestCount.Enabled
            || TurboHttpMetrics.RequestDuration.Enabled)
        {
            request.Options.Set(RequestTimestampKey, Stopwatch.GetTimestamp());
        }

        _ops.OnPushRequest(request);
    }

    public void OnRequestUpstreamFailure(Exception ex)
    {
        TurboTrace.Request.Warning(_ops, $"Request failed: {ex.GetType().Name} — {ex.Message}");

        if (_currentActivity is not null)
        {
            TurboHttpInstrumentation.SetError(_currentActivity, ex);
            _currentActivity.Stop();
            _currentActivity = null;
        }

        RecordFailedRequestMetrics(ex);
    }

    public void OnResponsePush(HttpResponseMessage response)
    {
        var request = response.RequestMessage;

        if (request?.Options
                .TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var activity) == true)
        {
            TurboHttpInstrumentation.SetResponse(activity, response);
            activity.Stop();
            _currentActivity = null;
        }

        var durationMs = 0.0;
        if (request is not null && request.Options.TryGetValue(RequestTimestampKey, out var timestamp))
        {
            durationMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        }

        var statusCode = (int)response.StatusCode;
        TurboTrace.Request.Info(_ops, "Request completed: {0} ({1:F1}ms)", statusCode, durationMs);


        RecordActiveRequestEnd(request);
        _currentRequest = null;

        RecordRequestMetrics(response, durationMs);

        _ops.OnPushResponse(response);
    }

    public void OnResponseUpstreamFailure(Exception ex)
    {
        TurboTrace.Request.Warning(_ops, $"Request failed: {ex.GetType().Name} — {ex.Message}");

        if (_currentActivity is not null)
        {
            TurboHttpInstrumentation.SetError(_currentActivity, ex);
            _currentActivity.Stop();
            _currentActivity = null;
        }

        RecordActiveRequestEnd(_currentRequest);
        RecordFailedRequestMetrics(ex);
    }

    public void PostStop()
    {
        if (_currentActivity is { } activity)
        {
            activity.SetStatus(ActivityStatusCode.Error, "Stage terminated with in-flight request");
            activity.Stop();
            _currentActivity = null;
        }
    }

    private static void RecordActiveRequestStart(HttpRequestMessage request)
    {
        if (!TurboHttpMetrics.ActiveRequests.Enabled)
        {
            return;
        }

        var method = TurboHttpInstrumentation.NormalizeMethod(request.Method.Method);
        var host = request.RequestUri?.Host ?? "unknown";
        var port = request.RequestUri?.Port ?? 0;
        var scheme = request.RequestUri?.Scheme ?? "https";

        TurboHttpMetrics.ActiveRequests.Add(1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("server.address", host),
            new KeyValuePair<string, object?>("server.port", port),
            new KeyValuePair<string, object?>("url.scheme", scheme));
    }

    private static void RecordActiveRequestEnd(HttpRequestMessage? request)
    {
        if (!TurboHttpMetrics.ActiveRequests.Enabled || request is null)
        {
            return;
        }

        var method = TurboHttpInstrumentation.NormalizeMethod(request.Method.Method);
        var host = request.RequestUri?.Host ?? "unknown";
        var port = request.RequestUri?.Port ?? 0;
        var scheme = request.RequestUri?.Scheme ?? "https";

        TurboHttpMetrics.ActiveRequests.Add(-1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("server.address", host),
            new KeyValuePair<string, object?>("server.port", port),
            new KeyValuePair<string, object?>("url.scheme", scheme));
    }

    private static void RecordRequestMetrics(HttpResponseMessage response, double durationMs)
    {
        if (!TurboHttpMetrics.RequestCount.Enabled && !TurboHttpMetrics.RequestDuration.Enabled)
        {
            return;
        }

        var request = response.RequestMessage;
        if (request is null)
        {
            return;
        }

        var method = TurboHttpInstrumentation.NormalizeMethod(request.Method.Method);
        var statusCode = (int)response.StatusCode;
        var host = request.RequestUri?.Host ?? "unknown";
        var port = request.RequestUri?.Port ?? 0;
        var scheme = request.RequestUri?.Scheme ?? "https";
        var protocolVersion = TurboHttpInstrumentation.FormatProtocolVersion(response.Version);

        TurboHttpMetrics.RequestCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("http.response.status_code", statusCode),
            new KeyValuePair<string, object?>("server.address", host));

        var durationTags = new List<KeyValuePair<string, object?>>
        {
            new("http.request.method", method),
            new("http.response.status_code", statusCode),
            new("network.protocol.version", protocolVersion),
            new("server.address", host),
            new("server.port", port),
            new("url.scheme", scheme),
        };

        if (statusCode >= 400)
        {
            durationTags.Add(new("error.type", statusCode.ToString()));
        }

        TurboHttpMetrics.RequestDuration.Record(durationMs / 1000.0,
            durationTags.ToArray().AsSpan());
    }

    private void RecordFailedRequestMetrics(Exception ex)
    {
        if (!TurboHttpMetrics.RequestCount.Enabled && !TurboHttpMetrics.RequestDuration.Enabled)
        {
            return;
        }

        var request = _currentRequest;
        if (request is null)
        {
            return;
        }

        var method = TurboHttpInstrumentation.NormalizeMethod(request.Method.Method);
        var host = request.RequestUri?.Host ?? "unknown";
        var port = request.RequestUri?.Port ?? 0;
        var scheme = request.RequestUri?.Scheme ?? "https";
        var errorType = ex.GetType().FullName ?? "unknown";

        TurboHttpMetrics.RequestCount.Add(1,
            new KeyValuePair<string, object?>("http.request.method", method),
            new KeyValuePair<string, object?>("error.type", errorType),
            new KeyValuePair<string, object?>("server.address", host));

        if (request.Options.TryGetValue(RequestTimestampKey, out var timestamp))
        {
            var durationSeconds = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds / 1000.0;
            TurboHttpMetrics.RequestDuration.Record(durationSeconds,
                new KeyValuePair<string, object?>("http.request.method", method),
                new KeyValuePair<string, object?>("error.type", errorType),
                new KeyValuePair<string, object?>("server.address", host),
                new KeyValuePair<string, object?>("server.port", port),
                new KeyValuePair<string, object?>("url.scheme", scheme));
        }

        _currentRequest = null;
    }
}