using System.Net.Http;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Streams.Stages;

internal sealed class
    ConnectionReuseStage : GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, IOutputItem>>
{
    private readonly bool _bodyFullyConsumed;

    private readonly Inlet<HttpResponseMessage> _in = new("ConnectionReuse.In");
    private readonly Outlet<HttpResponseMessage> _outResponse = new("ConnectionReuse.Out.Response");
    private readonly Outlet<IOutputItem> _outSignal = new("ConnectionReuse.Out.Signal");

    public override FanOutShape<HttpResponseMessage, HttpResponseMessage, IOutputItem> Shape { get; }


    public ConnectionReuseStage(bool bodyFullyConsumed = true)
    {
        _bodyFullyConsumed = bodyFullyConsumed;
        Shape = new FanOutShape<HttpResponseMessage, HttpResponseMessage, IOutputItem>(
            _in, _outResponse, _outSignal);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionReuseStage _stage;
        private HttpResponseMessage? _pendingResponse;
        private ConnectionReuseItem? _pendingSignal;
        private bool _responseOutletDemand;
        private bool _signalOutletDemand;

        public Logic(ConnectionReuseStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: () =>
                {
                    var response = Grab(stage._in);
                    var decision = ConnectionReuseEvaluator.Evaluate(
                        response, response.Version, stage._bodyFullyConsumed);

                    var endpoint = response.RequestMessage is { RequestUri: not null, Version: not null }
                        ? RequestEndpoint.FromRequest(response.RequestMessage)
                        : RequestEndpoint.Default;

                    _pendingResponse = response;
                    _pendingSignal = new ConnectionReuseItem(endpoint, decision);

                    TryPushResponse();
                    TryPushSignal();
                },
                onUpstreamFinish: () =>
                {
                    if (_pendingResponse is null && _pendingSignal is null)
                    {
                        CompleteStage();
                    }
                },
                onUpstreamFailure: ex => Log.Warning("ConnectionReuseStage: Upstream failure absorbed: {0}", ex.Message));

            SetHandler(stage._outResponse,
                onPull: () =>
                {
                    _responseOutletDemand = true;
                    TryPushResponse();
                    TryPullIfReady();
                },
                onDownstreamFinish: _ => CompleteStage());

            SetHandler(stage._outSignal,
                onPull: () =>
                {
                    _signalOutletDemand = true;
                    TryPushSignal();
                    TryPullIfReady();
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        private void TryPushResponse()
        {
            if (_pendingResponse is null || !_responseOutletDemand)
            {
                return;
            }

            var response = _pendingResponse;
            _pendingResponse = null;
            _responseOutletDemand = false;

            Push(_stage._outResponse, response);
            TryPullIfReady();
        }

        private void TryPushSignal()
        {
            if (_pendingSignal is null || !_signalOutletDemand)
            {
                return;
            }

            var signal = _pendingSignal;
            _pendingSignal = null;
            _signalOutletDemand = false;

            Push(_stage._outSignal, signal);
            TryPullIfReady();
        }

        private void TryPullIfReady()
        {
            // Pull next element only once both outlets have been served
            if (_pendingResponse is not null || _pendingSignal is not null)
            {
                return;
            }

            // Both outlets need demand before we pull upstream
            if (!_responseOutletDemand || !_signalOutletDemand)
            {
                return;
            }

            if (IsClosed(_stage._in))
            {
                CompleteStage();
            }
            else if (!HasBeenPulled(_stage._in))
            {
                Pull(_stage._in);
            }
        }
    }
}