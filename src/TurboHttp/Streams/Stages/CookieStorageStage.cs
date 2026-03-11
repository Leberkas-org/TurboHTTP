using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Extracts Set-Cookie headers from responses and stores them in the <see cref="CookieJar"/> (RFC 6265 §5.3).
/// The response is passed through unmodified — this stage is a side-effect-only observer.
/// When no <see cref="CookieJar"/> is provided the stage is a pass-through.
/// </summary>
internal sealed class CookieStorageStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly CookieJar? _cookieJar;

    private readonly Inlet<HttpResponseMessage> _inlet = new("cookieStorage.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("cookieStorage.out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public CookieStorageStage(CookieJar? cookieJar)
    {
        _cookieJar = cookieJar;
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(CookieStorageStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var response = Grab(stage._inlet);

                    if (stage._cookieJar is not null && response.RequestMessage?.RequestUri is not null)
                    {
                        stage._cookieJar.ProcessResponse(response.RequestMessage.RequestUri, response);
                    }

                    Push(stage._outlet, response);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet, onPull: () => Pull(stage._inlet), onDownstreamFinish: _ => CompleteStage());
        }
    }
}