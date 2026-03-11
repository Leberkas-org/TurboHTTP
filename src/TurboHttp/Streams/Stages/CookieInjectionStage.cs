using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Injects cookies from a <see cref="CookieJar"/> into outgoing HTTP requests (RFC 6265 §5.4).
/// When no <see cref="CookieJar"/> is provided the stage is a pass-through.
/// </summary>
internal sealed class CookieInjectionStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly CookieJar? _cookieJar;

    private readonly Inlet<HttpRequestMessage> _inlet = new("cookieInjection.in");
    private readonly Outlet<HttpRequestMessage> _outlet = new("cookieInjection.out");

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }

    public CookieInjectionStage(CookieJar? cookieJar)
    {
        _cookieJar = cookieJar;
        Shape = new FlowShape<HttpRequestMessage, HttpRequestMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly CookieInjectionStage _stage;

        public Logic(CookieInjectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var request = Grab(stage._inlet);

                    if (_stage._cookieJar is not null && request.RequestUri is not null)
                    {
                        _stage._cookieJar.AddCookiesToRequest(request.RequestUri, ref request);
                    }

                    Push(stage._outlet, request);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}