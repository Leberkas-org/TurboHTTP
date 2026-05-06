using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Protocol.Cookies;
using static Servus.Core.Servus;

namespace TurboHTTP.Streams.Stages.Features;

/// <summary>
/// Bidirectional stage that injects cookies into outgoing requests (RFC 6265 §5.4)
/// and stores Set-Cookie headers from incoming responses (RFC 6265 §5.3).
/// When no <see cref="CookieJar"/> is provided the stage is a pass-through in both directions.
/// </summary>
internal sealed class CookieBidiStage
    : GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>
{
    private readonly CookieJar? _cookieJar;

    private readonly Inlet<HttpRequestMessage> _inRequest = new("Cookie.In.Request");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("Cookie.Out.Request");
    private readonly Inlet<HttpResponseMessage> _inResponse = new("Cookie.In.Response");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("Cookie.Out.Response");

    public override BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public CookieBidiStage(CookieJar? cookieJar)
    {
        _cookieJar = cookieJar;
        Shape = new BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>(
            _inRequest, _outRequest, _inResponse, _outResponse);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(CookieBidiStage stage) : base(stage.Shape)
        {
            // Request direction: inject cookies
            SetHandler(stage._inRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inRequest);
                    try
                    {
                        if (stage._cookieJar is not null && request.RequestUri is not null)
                        {
                            var uri = request.RequestUri;
                            stage._cookieJar.AddCookiesToRequest(uri, ref request);
                            Tracing.For("Cookie").Debug(this, "→ injected cookies for {0}", uri.Host);
                        }
                    }
                    catch (Exception ex)
                    {
                        Tracing.For("Cookie").Warning(this, "→ cookie injection failed: {0}", ex.Message);
                    }

                    Push(stage._outRequest, request);
                },
                onUpstreamFinish: () => Complete(stage._outRequest),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("CookieBidiStage: Request upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outRequest);
                });

            SetHandler(stage._outRequest,
                onPull: () => Pull(stage._inRequest),
                onDownstreamFinish: _ => Cancel(stage._inRequest));

            // Response direction: store Set-Cookie headers
            SetHandler(stage._inResponse,
                onPush: () =>
                {
                    var response = Grab(stage._inResponse);
                    try
                    {
                        if (stage._cookieJar is not null && response.RequestMessage?.RequestUri is not null)
                        {
                            var uri = response.RequestMessage.RequestUri;
                            stage._cookieJar.ProcessResponse(uri, response);
                            Tracing.For("Cookie").Debug(this, "← processed Set-Cookie for {0}", uri.Host);
                        }
                    }
                    catch (Exception ex)
                    {
                        Tracing.For("Cookie").Warning(this, "← Set-Cookie processing failed: {0}", ex.Message);
                    }

                    Push(stage._outResponse, response);
                },
                onUpstreamFinish: () => Complete(stage._outResponse),
                onUpstreamFailure: ex =>
                {
                    Log.Warning("CookieBidiStage: Response upstream failure absorbed: {0}", ex.Message);
                    Complete(stage._outResponse);
                });

            SetHandler(stage._outResponse,
                onPull: () => Pull(stage._inResponse),
                onDownstreamFinish: _ => Cancel(stage._inResponse));
        }
    }
}
