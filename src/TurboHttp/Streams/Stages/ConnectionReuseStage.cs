using System;
using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9112 §9 — Evaluates whether the TCP connection can be reused after each HTTP response.
/// <para>
/// On each response the stage calls <see cref="ConnectionReuseEvaluator.Evaluate"/> and
/// invokes <paramref name="onDecision"/> with the result so the connection pool can
/// keep the connection alive or schedule it for close.  The response itself passes through
/// the stage unchanged.
/// </para>
/// </summary>
internal sealed class ConnectionReuseStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Version _httpVersion;
    private readonly Action<ConnectionReuseDecision> _onDecision;
    private readonly bool _bodyFullyConsumed;

    private readonly Inlet<HttpResponseMessage> _inlet = new("connectionReuse.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("connectionReuse.out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }

    /// <summary>
    /// Creates a new <see cref="ConnectionReuseStage"/>.
    /// </summary>
    /// <param name="httpVersion">
    ///     Negotiated HTTP version for this connection
    ///     (<see cref="System.Net.HttpVersion.Version10"/>,
    ///     <see cref="System.Net.HttpVersion.Version11"/>, or
    ///     <see cref="System.Net.HttpVersion.Version20"/>).
    /// </param>
    /// <param name="onDecision">
    ///     Callback invoked with the reuse decision after each response.
    ///     Use this to signal the connection pool: keep the connection
    ///     open when <see cref="ConnectionReuseDecision.CanReuse"/> is true,
    ///     or schedule a close when it is false.
    /// </param>
    /// <param name="bodyFullyConsumed">
    ///     Whether the response body was fully consumed before reaching this stage.
    ///     Defaults to <c>true</c> (the normal case in a fully-decoded pipeline).
    /// </param>
    public ConnectionReuseStage(
        Version httpVersion,
        Action<ConnectionReuseDecision> onDecision,
        bool bodyFullyConsumed = true)
    {
        _httpVersion = httpVersion;
        _onDecision = onDecision;
        _bodyFullyConsumed = bodyFullyConsumed;
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionReuseStage _stage;

        public Logic(ConnectionReuseStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var response = Grab(stage._inlet);
                    var decision = ConnectionReuseEvaluator.Evaluate(
                        response,
                        stage._httpVersion,
                        stage._bodyFullyConsumed);
                    stage._onDecision(decision);
                    Push(stage._outlet, response);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}
