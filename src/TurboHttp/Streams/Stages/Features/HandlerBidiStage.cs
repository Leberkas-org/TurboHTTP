using System;
using System.Diagnostics;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Diagnostics;

namespace TurboHttp.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that wraps a <see cref="TurboHandler"/> instance,
/// calling <see cref="TurboHandler.ProcessRequest"/> on outbound requests
/// and <see cref="TurboHandler.ProcessResponse"/> on inbound responses.
/// Composes via <c>BidiFlow.Atop</c> alongside built-in feature stages.
/// </summary>
internal sealed class HandlerBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly TurboHandler _handler;
    private readonly bool _isEntry;

    private readonly Inlet<HttpRequestMessage> _inRequest;
    private readonly Outlet<HttpRequestMessage> _outRequest;
    private readonly Inlet<HttpResponseMessage> _inResponse;
    private readonly Outlet<HttpResponseMessage> _outResponse;

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public HandlerBidiStage(TurboHandler handler, int index)
    {
        _handler = handler;
        _isEntry = index == 0;

        var name = handler.GetType().Name;
        var prefix = $"{name}{index}";

        _inRequest = new Inlet<HttpRequestMessage>($"{prefix}.In.Request");
        _outRequest = new Outlet<HttpRequestMessage>($"{prefix}.Out.Request");
        _inResponse = new Inlet<HttpResponseMessage>($"{prefix}.In.Response");
        _outResponse = new Outlet<HttpResponseMessage>($"{prefix}.Out.Response");

        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(HandlerBidiStage stage) : base(stage.Shape)
        {
            if (stage._isEntry)
            {
                // ── Entry stage: emit request lifecycle events ─────────
                SetHandler(stage._inRequest,
                    onPush: () =>
                    {
                        var request = Grab(stage._inRequest);

                        // Start root activity and store it on the request
                        var activity = TurboHttpInstrumentation.StartRequest(request);
                        if (activity is not null)
                        {
                            request.Options.Set(TurboHttpInstrumentation.RequestActivityKey, activity);
                        }

                        // Emit EventSource + DiagnosticListener request start
                        var method = request.Method.Method;
                        var uri = request.RequestUri?.OriginalString ?? "";
                        TurboHttpEventSource.Log.RequestStart(method, uri);
                        TurboHttpDiagnosticListener.OnRequestStart(request);

                        // Store timestamp for duration calculation
                        request.Options.Set(RequestTimestampKey, Stopwatch.GetTimestamp());

                        Push(stage._outRequest, stage._handler.ProcessRequest(request));
                    },
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex =>
                    {
                        TurboHttpEventSource.Log.RequestFailed(ex.GetType().Name, ex.Message);
                        TurboHttpDiagnosticListener.OnRequestFailed(ex);
                        Log.Warning("HandlerBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    });

                SetHandler(stage._outRequest,
                    onPull: () => Pull(stage._inRequest),
                    onDownstreamFinish: _ => Cancel(stage._inRequest));

                SetHandler(stage._inResponse,
                    onPush: () =>
                    {
                        var resp = Grab(stage._inResponse);
                        var request = resp.RequestMessage;

                        // Calculate duration and emit request stop events
                        var durationMs = 0.0;
                        if (request is not null && request.Options.TryGetValue(RequestTimestampKey, out var timestamp))
                        {
                            durationMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
                        }

                        var statusCode = (int)resp.StatusCode;
                        TurboHttpEventSource.Log.RequestStop(statusCode, durationMs);
                        TurboHttpDiagnosticListener.OnRequestStop(resp, TimeSpan.FromMilliseconds(durationMs));

                        // Record request metrics
                        TurboHttpMetrics.RequestCount.Add(1,
                            new System.Collections.Generic.KeyValuePair<string, object?>("http.request.method", request?.Method.Method ?? "UNKNOWN"),
                            new System.Collections.Generic.KeyValuePair<string, object?>("http.response.status_code", statusCode),
                            new System.Collections.Generic.KeyValuePair<string, object?>("server.address", request?.RequestUri?.Host ?? "unknown"));
                        TurboHttpMetrics.RequestDuration.Record(durationMs / 1000.0,
                            new System.Collections.Generic.KeyValuePair<string, object?>("http.request.method", request?.Method.Method ?? "UNKNOWN"),
                            new System.Collections.Generic.KeyValuePair<string, object?>("http.response.status_code", statusCode));

                        // Stop root activity
                        if (request is not null && request.Options.TryGetValue(TurboHttpInstrumentation.RequestActivityKey, out var activity))
                        {
                            TurboHttpInstrumentation.SetResponse(activity, resp);
                            activity.Stop();
                        }

                        Push(stage._outResponse, stage._handler.ProcessResponse(request!, resp));
                    },
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex =>
                    {
                        TurboHttpEventSource.Log.RequestFailed(ex.GetType().Name, ex.Message);
                        TurboHttpDiagnosticListener.OnRequestFailed(ex);
                        Log.Warning("HandlerBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    });

                SetHandler(stage._outResponse,
                    onPull: () => Pull(stage._inResponse),
                    onDownstreamFinish: _ => Cancel(stage._inResponse));
            }
            else
            {
                // ── Non-entry handler: pure pass-through with handler ──
                SetHandler(stage._inRequest,
                    onPush: () => Push(stage._outRequest, stage._handler.ProcessRequest(Grab(stage._inRequest))),
                    onUpstreamFinish: () => Complete(stage._outRequest),
                    onUpstreamFailure: ex => Log.Warning("HandlerBidiStage: Request upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._outRequest,
                    onPull: () => Pull(stage._inRequest),
                    onDownstreamFinish: _ => Cancel(stage._inRequest));

                SetHandler(stage._inResponse,
                    onPush: () =>
                    {
                        var resp = Grab(stage._inResponse);
                        Push(stage._outResponse, stage._handler.ProcessResponse(resp.RequestMessage!, resp));
                    },
                    onUpstreamFinish: () => Complete(stage._outResponse),
                    onUpstreamFailure: ex => Log.Warning("HandlerBidiStage: Response upstream failure absorbed: {0}", ex.Message));

                SetHandler(stage._outResponse,
                    onPull: () => Pull(stage._inResponse),
                    onDownstreamFinish: _ => Cancel(stage._inResponse));
            }
        }

        private static readonly HttpRequestOptionsKey<long> RequestTimestampKey = new("TurboHttp.RequestTimestamp");
    }
}
