using System.Net.Http;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams.Stages;

public record HttpRequestOptions();

public record RequestItem(HttpRequestOptions Options, HttpRequestMessage RequestMessage);

internal sealed class ExtractOptionsStage : GraphStage<FanOutShape<RequestItem, IOutputItem, HttpRequestMessage>>
{
    private readonly Outlet<IOutputItem> _outletOptions = new("");
    private readonly Outlet<HttpRequestMessage> _outletRequest = new("");
    private readonly Inlet<RequestItem> _inletRequest = new("");
    public override FanOutShape<RequestItem, IOutputItem, HttpRequestMessage> Shape { get; }

    public ExtractOptionsStage()
    {
        Shape = new FanOutShape<RequestItem, IOutputItem, HttpRequestMessage>(_inletRequest, _outletOptions,
            _outletRequest);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _initialSent;
        private RequestItem? _pending;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            SetHandler(stage.Shape.In,
                onPush: () =>
                {
                    var request = Grab(stage.Shape.In);

                    if (!_initialSent)
                    {
                        var options = new TcpOptions { Host = string.Empty, Port = 0 };
                        _pending = request;
                        _initialSent = true;
                        Push(stage.Shape.Out0, new ConnectItem(options, request.RequestMessage.Version));
                    }
                    else
                    {
                        Push(stage.Shape.Out1, request.RequestMessage);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Shape.Out0,
                onPull: () =>
                {
                    if (!_initialSent)
                    {
                        Pull(stage.Shape.In);
                    }
                }, onDownstreamFinish: _ => { });

            SetHandler(stage.Shape.Out1,
                onPull: () =>
                {
                    if (_pending is not null)
                    {
                        Push(stage.Shape.Out1, _pending.RequestMessage);
                        _pending = null;
                    }
                    else
                    {
                        Pull(stage.Shape.In);
                    }
                }, onDownstreamFinish: _ => CompleteStage());
        }
    }
}