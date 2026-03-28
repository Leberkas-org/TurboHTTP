using System;
using System.Net.Http;
using System.Threading.Tasks;
using Akka;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9112;
using TurboHttp.Transport;

namespace TurboHttp.Streams.Stages.Routing;

/// <summary>
/// Wraps the encoder+transport+decoder triplet and manages the connection lifecycle
/// via <see cref="IConnectionScope"/>. Acquires connections before sending requests
/// and returns them after receiving responses, with protocol-specific reuse semantics:
/// HTTP/1.0 acquires a new connection per request; HTTP/1.1+ reuses connections
/// until <c>Connection: close</c>.
/// </summary>
/// <remarks>
/// Internal wiring: request → (inner flow: encoder → transport → decoder) → response.
/// The inner flow is materialized via <see cref="GraphStageLogic.SubFusingMaterializer"/>
/// and bridged to the stage's inlet/outlet via <see cref="GraphStageLogic.GetAsyncCallback{T}"/>.
/// </remarks>
internal sealed class ConnectionScopeStage : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
{
    private readonly IConnectionScope _scope;
    private readonly Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> _innerFlow;

    private readonly Inlet<HttpRequestMessage> _in = new("ConnectionScope.In");
    private readonly Outlet<HttpResponseMessage> _out = new("ConnectionScope.Out");

    public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

    /// <param name="scope">Connection lifecycle manager (HTTP/1.0 or HTTP/1.1+ semantics).</param>
    /// <param name="innerFlow">
    /// The encoder → transport → decoder pipeline. In production this is
    /// <c>engine.CreateFlow().Join(transport)</c>; in tests a mock flow.
    /// </param>
    public ConnectionScopeStage(
        IConnectionScope scope,
        Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> innerFlow)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(innerFlow);
        _scope = scope;
        _innerFlow = innerFlow;
        Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionScopeStage _stage;
        private readonly IConnectionScope _scope;

        // ── Inner-flow bridge ──
        private ISourceQueueWithComplete<HttpRequestMessage>? _innerQueue;

        // ── State ──
        private bool _acquiring;
        private bool _returning;
        private bool _cleaning;
        private bool _upstreamFinished;
        private HttpRequestMessage? _pendingRequest;
        private HttpResponseMessage? _pendingResponse;
        private bool _downstreamDemand;

        // ── Async callbacks ──
        private Action<ConnectionLease>? _onAcquired;
        private Action<Exception>? _onAsyncError;
        private Action<HttpResponseMessage>? _onResponse;
        private Action? _onInnerComplete;
        private Action<Exception>? _onInnerFailed;
        private Action? _onReturnComplete;
        private Action? _onCleanupComplete;

        public Logic(ConnectionScopeStage stage) : base(stage.Shape)
        {
            _stage = stage;
            _scope = stage._scope;

            SetHandler(stage._in,
                onPush: OnPush,
                onUpstreamFinish: OnUpstreamFinish,
                onUpstreamFailure: OnUpstreamFailure);

            SetHandler(stage._out,
                onPull: OnPull,
                onDownstreamFinish: OnDownstreamCancel);
        }

        public override void PreStart()
        {
            _onAcquired = GetAsyncCallback<ConnectionLease>(OnConnectionAcquired);
            _onAsyncError = GetAsyncCallback<Exception>(OnAsyncError);
            _onResponse = GetAsyncCallback<HttpResponseMessage>(OnResponseReceived);
            _onInnerComplete = GetAsyncCallback(OnInnerFlowComplete);
            _onInnerFailed = GetAsyncCallback<Exception>(OnInnerFlowFailed);
            _onReturnComplete = GetAsyncCallback(OnReturnComplete);
            _onCleanupComplete = GetAsyncCallback(OnCleanupComplete);

            MaterializeInnerFlow();
        }

        private void MaterializeInnerFlow()
        {
            var (queue, completion) = Source
                .Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
                .Via(_stage._innerFlow)
                .ToMaterialized(
                    Sink.ForEach<HttpResponseMessage>(r => _onResponse!(r)),
                    Keep.Both)
                .Run(SubFusingMaterializer);

            _innerQueue = queue;

            completion.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _onInnerFailed!(t.Exception!.GetBaseException());
                }
                else
                {
                    _onInnerComplete!();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        // ── External inlet handlers ──

        private void OnPush()
        {
            var request = Grab(_stage._in);

            Log.Debug("ConnectionScopeStage: OnPush request={0} {1}",
                request.Method, request.RequestUri);

            _pendingRequest = request;

            if (_scope.CanReuse())
            {
                // Connection available for reuse (H11+): send immediately
                SendPendingRequest();
            }
            else
            {
                // Need new connection (always for H10, first request or post-close for H11+)
                StartAcquire();
            }
        }

        private void OnUpstreamFinish()
        {
            Log.Debug("ConnectionScopeStage: OnUpstreamFinish");
            _upstreamFinished = true;

            if (_pendingRequest is null && _pendingResponse is null && !_acquiring && !_returning)
            {
                StartCleanup();
            }
        }

        private void OnUpstreamFailure(Exception ex)
        {
            Log.Warning("ConnectionScopeStage: OnUpstreamFailure: {0}", ex.Message);
            _upstreamFinished = true;
            StartCleanup();
        }

        // ── External outlet handlers ──

        private void OnPull()
        {
            _downstreamDemand = true;
            TryPushResponse();

            if (_pendingResponse is null && _pendingRequest is null
                && !_acquiring && !_returning && !_cleaning
                && !_upstreamFinished
                && !HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private void OnDownstreamCancel(Exception? cause)
        {
            Log.Debug("ConnectionScopeStage: OnDownstreamCancel");
            _innerQueue?.Complete();
            StartCleanup();
        }

        // ── Async callbacks: connection lifecycle ──

        private void StartAcquire()
        {
            _acquiring = true;
            Log.Debug("ConnectionScopeStage: StartAcquire");

            _scope.AcquireAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _onAsyncError!(t.Exception!.GetBaseException());
                }
                else
                {
                    _onAcquired!(t.Result);
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void OnConnectionAcquired(ConnectionLease lease)
        {
            _acquiring = false;
            Log.Debug("ConnectionScopeStage: OnConnectionAcquired lease alive={0}", lease.IsAlive);

            if (_cleaning)
            {
                return;
            }

            SendPendingRequest();
        }

        private void OnAsyncError(Exception ex)
        {
            _acquiring = false;
            _returning = false;
            Log.Warning("ConnectionScopeStage: async error: {0}", ex.Message);
            FailStage(ex);
        }

        private void StartReturn(bool canReuse)
        {
            _returning = true;
            Log.Debug("ConnectionScopeStage: StartReturn canReuse={0}", canReuse);

            _scope.ReturnAsync(canReuse).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _onAsyncError!(t.Exception!.GetBaseException());
                }
                else
                {
                    _onReturnComplete!();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void OnReturnComplete()
        {
            _returning = false;
            Log.Debug("ConnectionScopeStage: OnReturnComplete upstreamFinished={0}", _upstreamFinished);

            if (_upstreamFinished && _pendingRequest is null && _pendingResponse is null)
            {
                StartCleanup();
                return;
            }

            TryPullUpstream();
        }

        private void StartCleanup()
        {
            if (_cleaning)
            {
                return;
            }

            _cleaning = true;
            _innerQueue?.Complete();
            Log.Debug("ConnectionScopeStage: StartCleanup");

            _scope.CleanupAsync().ContinueWith(t =>
            {
                // Ignore errors during cleanup
                _onCleanupComplete!();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void OnCleanupComplete()
        {
            Log.Debug("ConnectionScopeStage: OnCleanupComplete");
            CompleteStage();
        }

        // ── Async callbacks: inner flow ──

        private void OnResponseReceived(HttpResponseMessage response)
        {
            Log.Debug("ConnectionScopeStage: OnResponseReceived status={0}", (int)response.StatusCode);

            if (_cleaning)
            {
                return;
            }

            // Evaluate connection reuse based on the response
            var decision = EvaluateReuse(response);
            Log.Debug("ConnectionScopeStage: reuse decision canReuse={0}, reason={1}",
                decision.CanReuse, decision.Reason);

            _pendingResponse = response;

            // Return the connection to the pool (scope decides whether to actually close or reuse)
            StartReturn(decision.CanReuse);

            TryPushResponse();
        }

        private void OnInnerFlowComplete()
        {
            Log.Debug("ConnectionScopeStage: inner flow completed");

            if (!_cleaning && !_upstreamFinished)
            {
                // Inner flow completed unexpectedly
                _upstreamFinished = true;
                StartCleanup();
            }
        }

        private void OnInnerFlowFailed(Exception ex)
        {
            Log.Warning("ConnectionScopeStage: inner flow failed: {0}", ex.Message);

            if (!_cleaning)
            {
                StartCleanup();
            }
        }

        // ── Helpers ──

        private void SendPendingRequest()
        {
            if (_pendingRequest is null || _innerQueue is null)
            {
                return;
            }

            var request = _pendingRequest;
            _pendingRequest = null;

            Log.Debug("ConnectionScopeStage: SendPendingRequest {0} {1}",
                request.Method, request.RequestUri);

            _innerQueue.OfferAsync(request).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _onAsyncError!(t.Exception!.GetBaseException());
                }
                // Response will arrive via _onResponse callback
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private void TryPushResponse()
        {
            if (_pendingResponse is null || !_downstreamDemand)
            {
                return;
            }

            var response = _pendingResponse;
            _pendingResponse = null;
            _downstreamDemand = false;

            Log.Debug("ConnectionScopeStage: TryPushResponse status={0}", (int)response.StatusCode);
            Push(_stage._out, response);
        }

        private void TryPullUpstream()
        {
            if (_upstreamFinished || _pendingRequest is not null || _cleaning)
            {
                return;
            }

            if (!_downstreamDemand)
            {
                return;
            }

            if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
            {
                Pull(_stage._in);
            }
        }

        private static ConnectionReuseDecision EvaluateReuse(HttpResponseMessage response)
        {
            if (response.Version is { Major: >= 3 })
            {
                // HTTP/3+: always reusable at this level (QUIC handles connection lifecycle)
                return ConnectionReuseDecision.KeepAlive("HTTP/3+ always reusable");
            }

            return ConnectionReuseEvaluator.Evaluate(
                response, response.Version, bodyFullyConsumed: true);
        }

        public override void PostStop()
        {
            Log.Debug("ConnectionScopeStage: PostStop");
            _innerQueue?.Complete();

            // Best-effort cleanup: if scope wasn't cleaned up yet, fire-and-forget
            if (!_cleaning)
            {
                _ = _scope.CleanupAsync();
            }
        }
    }
}
