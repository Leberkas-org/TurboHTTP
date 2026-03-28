using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Outermost bidirectional stage that creates and manages the root "TurboHttp.Request"
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
        private static readonly HttpRequestOptionsKey<long> RequestTimestampKey = new("TurboHttp.RequestTimestamp");

        /// <summary>
        /// Tracks the most recent in-flight root activity so that upstream failures
        /// on the response path can be attributed to the correct span.
        /// </summary>
        private Activity? _currentActivity;

        public Logic(TracingBidiStage stage) : base(stage.Shape)
        {
            // ── Request direction ──────────────────────────────────────
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    var activity = TurboHttpInstrumentation.StartRequest(request);
                    if (activity is not null)
                    {
                        request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);
                        _currentActivity = activity;
                    }

                    // Emit EventSource + DiagnosticListener request start
                    var method = request.Method.Method;
                    var uri = request.RequestUri?.OriginalString ?? "";
                    TurboHttpEventSource.Log.RequestStart(method, uri);
                    TurboHttpDiagnosticListener.OnRequestStart(request);

                    // Record request start timestamp for duration calculation
                    request.Options.Set(RequestTimestampKey, Stopwatch.GetTimestamp());

                    Push(stage._outRequest, request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    TurboHttpEventSource.Log.RequestFailed(ex.GetType().Name, ex.Message);
                    TurboHttpDiagnosticListener.OnRequestFailed(ex);

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

            // ── Response direction ─────────────────────────────────────
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

                    // Calculate duration and emit EventSource + DiagnosticListener request stop
                    var durationMs = 0.0;
                    if (request is not null && request.Options.TryGetValue(RequestTimestampKey, out var timestamp))
                    {
                        durationMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
                    }

                    var statusCode = (int)response.StatusCode;
                    TurboHttpEventSource.Log.RequestStop(statusCode, durationMs);
                    TurboHttpDiagnosticListener.OnRequestStop(response, TimeSpan.FromMilliseconds(durationMs));

                    // Record request metrics
                    RecordRequestMetrics(response, durationMs);

                    Push(stage._outResponse, response);
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    TurboHttpEventSource.Log.RequestFailed(ex.GetType().Name, ex.Message);
                    TurboHttpDiagnosticListener.OnRequestFailed(ex);

                    if (_currentActivity is not null)
                    {
                        TurboHttpInstrumentation.SetError(_currentActivity, ex);
                        _currentActivity.Stop();
                        _currentActivity = null;
                    }

                    Log.Debug("TracingBidiStage: Propagating response upstream failure to outResponse: {0}", ex.Message);
                    Fail(stage._outResponse, ex);
                });

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }

        private static void RecordRequestMetrics(HttpResponseMessage response, double durationMs)
        {
            var request = response.RequestMessage;
            if (request is null)
            {
                return;
            }

            var method = request.Method.Method;
            var statusCode = (int)response.StatusCode;
            var host = request.RequestUri?.Host ?? "unknown";

            TurboHttpMetrics.RequestCount.Add(1,
                new KeyValuePair<string, object?>("http.request.method", method),
                new KeyValuePair<string, object?>("http.response.status_code", statusCode),
                new KeyValuePair<string, object?>("server.address", host));

            TurboHttpMetrics.RequestDuration.Record(durationMs / 1000.0,
                new KeyValuePair<string, object?>("http.request.method", method),
                new KeyValuePair<string, object?>("http.response.status_code", statusCode));
        }
    }
}
