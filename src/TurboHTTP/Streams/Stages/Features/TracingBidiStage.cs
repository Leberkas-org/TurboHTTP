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

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public TracingBidiStage()
    {
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private static readonly HttpRequestOptionsKey<long> RequestTimestampKey = new("TurboHTTP.RequestTimestamp");

        /// <summary>
        /// Tracks the most recent in-flight root activity so that upstream failures
        /// on the response path can be attributed to the correct span.
        /// </summary>
        private Activity? _currentActivity;

        public Logic(TracingBidiStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    var activity = TurboHttpInstrumentation.StartRequest(request);
                    if (activity is not null)
                    {
                        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);
                        TurboHttpInstrumentation.InjectTraceContext(activity, request);
                        _currentActivity = activity;
                    }

                    // Emit diagnostic events
                    TurboHttpDiagnosticSource.OnRequestStart(request);
                    TurboHttpEventSource.Instance.RequestStart(
                        request.Method.Method,
                        request.RequestUri?.OriginalString ?? "");

                    // Emit request start trace event
                    var method = request.Method.Method;
                    var uri = request.RequestUri?.OriginalString ?? "";
                    TurboTrace.Request.Info(this, "Request started: {0} {1}", method, uri);

                    // Track active requests
                    RecordActiveRequestStart(request);

                    // Record request start timestamp for duration calculation
                    // Only store when tracing or metrics is active to avoid unnecessary allocation
                    if (TurboHttpInstrumentation.Source.HasListeners()
                        || TurboTrace.ShouldTrace(TurboTraceCategory.Request, TurboTraceLevel.Info)
                        || TurboHttpMetrics.RequestCount.Enabled
                        || TurboHttpMetrics.RequestDuration.Enabled)
                    {
                        request.Options.Set(RequestTimestampKey, Stopwatch.GetTimestamp());
                    }

                    Push(stage._outRequest, request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    TurboTrace.Request.Warning(this, $"Request failed: {ex.GetType().Name} — {ex.Message}");
                    TurboHttpEventSource.Instance.RequestFailed("UNKNOWN", "", ex.GetType().Name);

                    if (_currentActivity is not null)
                    {
                        TurboHttpInstrumentation.SetError(_currentActivity, ex);
                        _currentActivity.Stop();
                        _currentActivity = null;
                    }

                    Fail(stage._outRequest, ex);
                });

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    var request = response.RequestMessage;

                    if (request?.Options
                            .TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var activity) == true)
                    {
                        TurboHttpInstrumentation.SetResponse(activity, response);
                        activity.Stop();
                        _currentActivity = null;
                    }

                    // Calculate duration and emit request stop trace event
                    var durationMs = 0.0;
                    if (request is not null && request.Options.TryGetValue(RequestTimestampKey, out var timestamp))
                    {
                        durationMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
                    }

                    var statusCode = (int)response.StatusCode;
                    TurboTrace.Request.Info(this, "Request completed: {0} ({1:F1}ms)", statusCode, durationMs);

                    // Emit diagnostic/ETW events
                    if (request is not null)
                    {
                        TurboHttpDiagnosticSource.OnRequestStop(request, response, TaskStatus.RanToCompletion);
                    }
                    TurboHttpEventSource.Instance.RequestStop(
                        request?.Method.Method ?? "UNKNOWN", statusCode, durationMs);

                    // Track active requests
                    RecordActiveRequestEnd(request);

                    // Record request metrics
                    RecordRequestMetrics(response, durationMs);

                    Push(stage._outResponse, response);
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    TurboTrace.Request.Warning(this, $"Request failed: {ex.GetType().Name} — {ex.Message}");
                    TurboHttpEventSource.Instance.RequestFailed("UNKNOWN", "", ex.GetType().Name);

                    if (_currentActivity is not null)
                    {
                        TurboHttpInstrumentation.SetError(_currentActivity, ex);
                        _currentActivity.Stop();
                        _currentActivity = null;
                    }

                    // Decrement active requests on failure (no request available for tags)
                    TurboHttpMetrics.ActiveRequests.Add(-1);

                    Log.Debug("TracingBidiStage: Propagating response upstream failure to outResponse: {0}", ex.Message);
                    Fail(stage._outResponse, ex);
                });

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
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
    }
}
