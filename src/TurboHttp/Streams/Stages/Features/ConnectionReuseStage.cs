using System;
using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Streams.Stages.Features;

internal sealed class
    ConnectionReuseStage : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, IOutputItem>>
{
    private readonly bool _bodyFullyConsumed;

    private readonly Inlet<HttpResponseMessage> _in = new("ConnectionReuse.In");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ConnectionReuse.Out.Response");
    private readonly Outlet<IOutputItem> _outSignal = new("ConnectionReuse.Out.Signal");

    public override FanOutShape<HttpResponseMessage, HttpResponseMessage, IOutputItem> Shape { get; }


    public ConnectionReuseStage(bool bodyFullyConsumed = true)
    {
        _bodyFullyConsumed = bodyFullyConsumed;
        Shape = new FanOutShape<HttpResponseMessage, HttpResponseMessage, IOutputItem>(
            _in, _outResponse, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionReuseStage _stage;
        private HttpResponseMessage? _pendingResponse;
        private ConnectionReuseItem? _pendingSignal;
        private bool _responseOutletDemand;
        private bool _signalOutletDemand;

        public Logic(ConnectionReuseStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);
                    var decision = EvaluateReuse(response, stage._bodyFullyConsumed);

                    var endpoint = response.RequestMessage is { RequestUri: not null, Version: not null }
                        ? RequestEndpoint.FromRequest(response.RequestMessage)
                        : RequestEndpoint.Default;

                    _pendingResponse = response;
                    _pendingSignal = new ConnectionReuseItem(endpoint, decision);

                    TryPushResponse();
                    TryPushSignal();
                },
                onUpstreamFinish: () =>
                {
                    if (_pendingResponse is null && _pendingSignal is null)
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex => Log.Warning("ConnectionReuseStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    _responseOutletDemand = true;
                    TryPushResponse();
                    TryPullIfReady();
                },
                onDownstreamFinish: _ => CompleteStage());

            SetHandler(stage._outSignal,
                onPull: () =>
                {
                    _signalOutletDemand = true;
                    TryPushSignal();
                    TryPullIfReady();
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        /// <summary>
        /// Dispatches to the appropriate reuse evaluator based on HTTP version.
        /// HTTP/3+ responses use <see cref="Http3ConnectionReuseEvaluator"/> (RFC 9114 §3.3);
        /// all others use <see cref="ConnectionReuseEvaluator"/> (RFC 9112 §9).
        /// </summary>
        private static ConnectionReuseDecision EvaluateReuse(
            HttpResponseMessage response, bool bodyFullyConsumed)
        {
            if (response.Version.Major >= 3)
            {
                return EvaluateHttp3Reuse(response);
            }

            return ConnectionReuseEvaluator.Evaluate(
                response, response.Version, bodyFullyConsumed);
        }

        /// <summary>
        /// Evaluates HTTP/3 connection reuse via <see cref="Http3ConnectionReuseEvaluator"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>serverCertificate</b>: The server's TLS certificate is not available in the
        /// <see cref="HttpResponseMessage"/> context. It lives in the QUIC connection state
        /// managed by <c>QuicClientProvider</c>. To enable cross-origin coalescing, the
        /// certificate would need to be threaded through the pipeline (e.g. via
        /// <see cref="HttpRequestOptions"/> or a custom property on the response).
        /// Currently passed as <c>null</c>, which means cross-origin reuse is conservatively
        /// denied — same-origin reuse still works correctly.
        /// </para>
        /// <para>
        /// <b>isGoingAway</b>: The GOAWAY state is tracked by <c>Http30ConnectionStage</c>
        /// and is not propagated to downstream stages. To support GOAWAY-aware reuse
        /// decisions, <c>Http30ConnectionStage</c> would need to signal going-away state
        /// via the response (e.g. a custom header or <see cref="HttpRequestOptions"/> entry).
        /// Currently passed as <c>false</c>; GOAWAY handling remains at the connection stage level.
        /// </para>
        /// </remarks>
        private static ConnectionReuseDecision EvaluateHttp3Reuse(HttpResponseMessage response)
        {
            var uri = response.RequestMessage?.RequestUri;

            var scheme = uri?.Scheme ?? "https";
            var host = uri?.Host ?? string.Empty;
            var port = uri?.Port ?? 443;

            // serverCertificate: not available in response context — see remarks.
            // isGoingAway: not available in response context — see remarks.
            var h3Decision = Http3ConnectionReuseEvaluator.Evaluate(
                connectionScheme: scheme,
                connectionHost: host,
                connectionPort: port,
                targetScheme: scheme,
                targetHost: host,
                targetPort: port,
                serverCertificate: null,
                isGoingAway: false);

            // Bridge Http3ConnectionReuseDecision → ConnectionReuseDecision
            return h3Decision.CanReuse
                ? ConnectionReuseDecision.KeepAlive(h3Decision.Reason)
                : ConnectionReuseDecision.Close(h3Decision.Reason);
        }

        private void TryPushResponse()
        {
            if (_pendingResponse is null || !_responseOutletDemand)
            {
                return;
            }

            var response = _pendingResponse;
            _pendingResponse = null;
            _responseOutletDemand = false;

            Push(_stage._outResponse, response);
            TryPullIfReady();
        }

        private void TryPushSignal()
        {
            if (_pendingSignal is null || !_signalOutletDemand)
            {
                return;
            }

            var signal = _pendingSignal;
            _pendingSignal = null;
            _signalOutletDemand = false;

            Push(_stage._outSignal, signal);
            TryPullIfReady();
        }

        private void TryPullIfReady()
        {
            // Pull next element only once both outlets have been served
            if (_pendingResponse is not null || _pendingSignal is not null)
            {
                return;
            }

            // Both outlets need demand before we pull upstream
            if (!_responseOutletDemand || !_signalOutletDemand)
            {
                return;
            }

            if (IsClosed(_stage._in))
            {
                CompleteStage();
            }
            else if (!HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}