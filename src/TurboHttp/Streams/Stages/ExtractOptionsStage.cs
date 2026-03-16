using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Client;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams.Stages;

internal sealed class ExtractOptionsStage : GraphStage<FanOutShape<HttpRequestMessage, IControlItem, HttpRequestMessage>>
{
    private readonly TurboClientOptions _clientOptions;

    private readonly Inlet<HttpRequestMessage> _inletRequest = new("options.in.request");
    private readonly Outlet<IControlItem> _outletSignal = new("options.out.signal");
    private readonly Outlet<HttpRequestMessage> _outletRequest = new("options.out.request");

    public override FanOutShape<HttpRequestMessage, IControlItem, HttpRequestMessage> Shape { get; }

    public ExtractOptionsStage(TurboClientOptions? clientOptions = null)
    {
        _clientOptions = clientOptions ?? new TurboClientOptions();
        Shape = new FanOutShape<HttpRequestMessage, IControlItem, HttpRequestMessage>(
            _inletRequest, _outletSignal, _outletRequest);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _initialSent;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            SetHandler(stage._inletRequest,
                onPush: () =>
                {
                    var request = Grab(stage._inletRequest);

                    if (!_initialSent)
                    {
                        var tcpOptions = TcpOptionsFactory.Build(request.RequestUri!, stage._clientOptions);
                        _initialSent = true;
                        Push(stage._outletSignal, new ConnectItem(tcpOptions, request.Version));
                        Push(stage._outletRequest, request);
                        Complete(stage._outletSignal);
                    }
                    else
                    {
                        Push(stage._outletRequest, request);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outletSignal,
                onPull: () =>
                {
                    if (!_initialSent)
                    {
                        Pull(stage._inletRequest);
                    }
                },
                onDownstreamFinish: _ => { });

            SetHandler(stage._outletRequest,
                onPull: () => Pull(stage._inletRequest),
                onDownstreamFinish: _ => CompleteStage());
        }
    }
}