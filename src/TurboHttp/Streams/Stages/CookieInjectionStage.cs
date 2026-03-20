using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC6265;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Injects cookies from a <see cref="CookieJar"/> into outgoing HTTP requests (RFC 6265 §5.4).
/// When no <see cref="CookieJar"/> is provided the stage is a pass-through.
/// </summary>
internal sealed class CookieInjectionStage : GraphStage<FlowShape<HttpRequestMessage, HttpRequestMessage>>
{
    private readonly CookieJar? _cookieJar;

    private readonly Inlet<HttpRequestMessage> _in = new("CookieInjection.In");
    private readonly Outlet<HttpRequestMessage> _out = new("CookieInjection.Out");

    public override FlowShape<HttpRequestMessage, HttpRequestMessage> Shape { get; }


    public CookieInjectionStage(CookieJar? cookieJar)
    {
        _cookieJar = cookieJar;
        Shape = new FlowShape<HttpRequestMessage, HttpRequestMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(CookieInjectionStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);

                    if (stage._cookieJar is not null && request.RequestUri is not null)
                    {
                        stage._cookieJar.AddCookiesToRequest(request.RequestUri, ref request);
                    }

                    Push(stage._out, request);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("CookieInjectionStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out,
                onPull: () => Pull(stage._in),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}