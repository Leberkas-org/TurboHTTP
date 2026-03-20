using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC9110;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9110 §15.4 — Intercepts redirect responses (301/302/303/307/308) and emits
/// new redirect requests on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out1"/> for
/// re-injection into the HTTP engine, while forwarding final (non-redirect) responses
/// on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out0"/>.
/// <para>
/// Each request chain gets its own <see cref="RedirectHandler"/> instance, tracked via
/// <see cref="HttpRequestMessage.Options"/>. This ensures that <c>_visitedUris</c> and
/// <c>_redirectCount</c> are isolated per request chain — concurrent request chains
/// cannot interfere with each other's redirect limits or loop detection.
/// </para>
/// <para>
/// Both downstream outlets must have demand before the stage pulls the inlet,
/// matching the same demand contract used by <see cref="CacheLookupStage"/>.
/// </para>
/// </summary>
internal sealed class
    RedirectStage : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>>
{
    internal static readonly HttpRequestOptionsKey<RedirectHandler> RedirectHandlerKey
        = new("TurboHttp.RedirectHandler");

    internal readonly RedirectPolicy _policy;

    private readonly Inlet<HttpResponseMessage> _in
        = new("redirect.in");

    private readonly Outlet<HttpResponseMessage> _outFinal
        = new("redirect.out0.final");

    private readonly Outlet<HttpRequestMessage> _outRedirect
        = new("redirect.out1.redirect");

    public override FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage> Shape { get; }


    /// <summary>
    /// Creates a new <see cref="RedirectStage"/> with the given policy.
    /// </summary>
    /// <param name="policy">Redirect policy configuration. Defaults to <see cref="RedirectPolicy.Default"/>.</param>
    public RedirectStage(RedirectPolicy? policy = null)
    {
        _policy = policy ?? RedirectPolicy.Default;
        Shape = new FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>(
            _in, _outFinal, _outRedirect);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly RedirectStage _stage;
        private bool _finalHasDemand;
        private bool _redirectHasDemand;

        public Logic(RedirectStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);

                    if (!RedirectHandler.IsRedirect(response))
                    {
                        // Not a redirect — forward as final response
                        _finalHasDemand = false;
                        Push(stage._outFinal, response);
                        return;
                    }

                    var original = response.RequestMessage;
                    if (original is null)
                    {
                        // No original request context — cannot build redirect, pass through
                        _finalHasDemand = false;
                        Push(stage._outFinal, response);
                        return;
                    }

                    try
                    {
                        // Get or create a per-request-chain RedirectHandler via Options
                        if (!original.Options.TryGetValue(RedirectHandlerKey, out var handler))
                        {
                            handler = new RedirectHandler(_stage._policy);
                        }

                        var newRequest = handler.BuildRedirectRequest(original, response);

                        // Carry the handler forward with the redirect request
                        newRequest.Options.Set(RedirectHandlerKey, handler);

                        _redirectHasDemand = false;
                        Push(stage._outRedirect, newRequest);
                    }
                    catch (RedirectDowngradeException)
                    {
                        // HTTPS→HTTP downgrade blocked — forward as final response
                        _finalHasDemand = false;
                        Push(stage._outFinal, response);
                    }
                    catch (RedirectException)
                    {
                        // Max redirects exceeded or loop detected — forward as final response
                        _finalHasDemand = false;
                        Push(stage._outFinal, response);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("RedirectStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outFinal,
                onPull: () =>
                {
                    _finalHasDemand = true;
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());

            SetHandler(stage._outRedirect,
                onPull: () =>
                {
                    _redirectHasDemand = true;
                    TryPullInlet();
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        private void TryPullInlet()
        {
            if (_finalHasDemand && _redirectHasDemand && !HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}
