using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol.RFC6265;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Extracts Set-Cookie headers from responses and stores them in the <see cref="CookieJar"/> (RFC 6265 §5.3).
/// The response is passed through unmodified — this stage is a side-effect-only observer.
/// When no <see cref="CookieJar"/> is provided the stage is a pass-through.
/// </summary>
internal sealed class CookieStorageStage : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly CookieJar? _cookieJar;

    private readonly Inlet<HttpResponseMessage> _in = new("CookieStorage.In");
    private readonly Outlet<HttpResponseMessage> _out = new("CookieStorage.Out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }


    public CookieStorageStage(CookieJar? cookieJar)
    {
        _cookieJar = cookieJar;
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        public Logic(CookieStorageStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);

                    if (stage._cookieJar is not null && response.RequestMessage?.RequestUri is not null)
                    {
                        stage._cookieJar.ProcessResponse(response.RequestMessage.RequestUri, response);
                    }

                    Push(stage._out, response);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("CookieStorageStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._out, onPull: () => Pull(stage._in), onDownstreamFinish: _ => CompleteStage());
        }
    }
}