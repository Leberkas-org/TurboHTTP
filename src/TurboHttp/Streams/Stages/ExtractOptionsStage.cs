using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams.Stages;

internal sealed class ExtractOptionsStage : GraphStage<FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>>
{
    private readonly TurboClientOptions _clientOptions;
    private readonly Inlet<HttpRequestMessage> _inletRequest = new("options.in.request");
    private readonly Outlet<IOutputItem> _outletSignal = new("options.out.signal");
    private readonly Outlet<HttpRequestMessage> _outletRequest = new("options.out.request");
    public override FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem> Shape { get; }


    public ExtractOptionsStage(TurboClientOptions? clientOptions = null)
    {
        _clientOptions = clientOptions ?? new TurboClientOptions();
        Shape = new FanOutShape<HttpRequestMessage, HttpRequestMessage, IOutputItem>(
            _inletRequest, _outletRequest, _outletSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _initialSent;
        private HttpRequestMessage? _pending;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inletRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inletRequest);

                    if (!_initialSent)
                    {
                        var options = TcpOptionsFactory.Build(request.RequestUri!, stage._clientOptions);
                        _pending = request;
                        _initialSent = true;
                        Push(stage._outletSignal, new ConnectItem(options) { Key = RequestEndpoint.FromRequest(request) });
                        Complete(stage._outletSignal);
                    }
                    else
                    {
                        Push(stage._outletRequest, request);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: ex => Log.Warning("ExtractOptionsStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outletSignal,
                onPull: () =>
                {
                    if (!_initialSent)
                    {
                        Pull(stage._inletRequest);
                    }
                }, onDownstreamFinish: _ => { });

            SetHandler(stage._outletRequest,
                onPull: () =>
                {
                    if (_pending is not null)
                    {
                        Push(stage._outletRequest, _pending);
                        _pending = null;
                    }
                    else
                    {
                        Pull(stage._inletRequest);
                    }
                }, onDownstreamFinish: _ => CompleteStage());
        }
    }
}