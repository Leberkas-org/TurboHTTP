using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// RFC 9110 §15.4 — Intercepts redirect responses (301/302/303/307/308) and emits
/// new redirect requests on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out1"/> for
/// re-injection into the HTTP engine, while forwarding final (non-redirect) responses
/// on <see cref="FanOutShape{TIn,TOut0,TOut1}.Out0"/>.
/// <para>
/// Loop detection and max-redirect enforcement are handled by the
/// <see cref="RedirectHandler"/>. When either limit is reached (or the response has no
/// <c>RequestMessage</c>), the redirect response is forwarded unchanged on Out0.
/// </para>
/// <para>
/// Both downstream outlets must have demand before the stage pulls the inlet,
/// matching the same demand contract used by <see cref="CacheLookupStage"/>.
/// </para>
/// </summary>
internal sealed class RedirectStage
    : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage>>
{
    private readonly RedirectHandler _handler;
    private readonly CookieJar? _cookieJar;

    private readonly Inlet<HttpResponseMessage> _in
        = new("redirect.in");

    private readonly Outlet<HttpResponseMessage> _outFinal
        = new("redirect.out0.final");

    private readonly Outlet<HttpRequestMessage> _outRedirect
        = new("redirect.out1.redirect");

    public override FanOutShape<HttpResponseMessage, HttpResponseMessage, HttpRequestMessage> Shape { get; }

    /// <summary>
    /// Creates a new <see cref="RedirectStage"/> with the given handler and optional cookie jar.
    /// </summary>
    /// <param name="handler">Redirect handler (with policy and state). Defaults to <see cref="RedirectHandler"/> with <see cref="RedirectPolicy.Default"/>.</param>
    /// <param name="cookieJar">Optional cookie jar for cross-redirect cookie re-evaluation (RFC 6265).</param>
    public RedirectStage(RedirectHandler? handler = null, CookieJar? cookieJar = null)
    {
        _handler = handler ?? new RedirectHandler();
        _cookieJar = cookieJar;
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
                        HttpRequestMessage newRequest;
                        if (_stage._cookieJar is not null)
                        {
                            newRequest = _stage._handler.BuildRedirectRequest(original, response, _stage._cookieJar);
                        }
                        else
                        {
                            newRequest = _stage._handler.BuildRedirectRequest(original, response);
                        }

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
                onUpstreamFailure: FailStage);

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
