using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Streams.Stages.Routing;

internal sealed class ExtractOptionsStage : GraphStage<FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>>
{
    private readonly TurboClientOptions _clientOptions;
    private readonly Inlet<HttpRequestMessage> _in = new("ExtractOptions.In");
    private readonly Outlet<IOutputItem> _outSignal = new("ExtractOptions.Out.Signal");
    private readonly Outlet<HttpRequestMessage> _outRequest = new("ExtractOptions.Out.Request");
    public override FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem> Shape { get; }


    public ExtractOptionsStage(TurboClientOptions? clientOptions = null)
    {
        _clientOptions = clientOptions ?? new TurboClientOptions();
        Shape = new FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>(
            _in, _outRequest, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _initialSent;
        private HttpRequestMessage? _pending;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            SetHandler(stage._in,
                onPush: () =>
                {
                    var request = Grab(stage._in);

                    if (!_initialSent)
                    {
                        var options = TcpOptionsFactory.Build(request.RequestUri!, stage._clientOptions, request.Version);
                        _pending = request;
                        _initialSent = true;
                        Push(stage._outSignal, new ConnectItem(options) { Key = RequestEndpoint.FromRequest(request) });
                        Complete(stage._outSignal);
                    }
                    else
                    {
                        Push(stage._outRequest, request);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("ExtractOptionsStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outSignal,
                onPull: () =>
                {
                    if (!_initialSent)
                    {
                        Pull(stage._in);
                    }
                }, onDownstreamFinish: _ => { });

            SetHandler(stage._outRequest,
                onPull: () =>
                {
                    if (_pending is not null)
                    {
                        Push(stage._outRequest, _pending);
                        _pending = null;
                    }
                    else
                    {
                        Pull(stage._in);
                    }
                }, onDownstreamFinish: _ => CompleteStage());
        }
    }
}