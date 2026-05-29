using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Streams.Stages.Server;

internal sealed class ConnectionBridgeStage : GraphStage<FlowShape<IFeatureCollection, IFeatureCollection>>
{
    private readonly int _connectionId;
    private readonly Sink<IFeatureCollection, NotUsed> _requestIngress;
    private readonly Source<IFeatureCollection, NotUsed> _responseFanoutSource;

    private readonly Inlet<IFeatureCollection> _in = new("ConnectionBridge.In");
    private readonly Outlet<IFeatureCollection> _out = new("ConnectionBridge.Out");

    public override FlowShape<IFeatureCollection, IFeatureCollection> Shape { get; }

    public ConnectionBridgeStage(
        int connectionId,
        Sink<IFeatureCollection, NotUsed> requestIngress,
        Source<IFeatureCollection, NotUsed> responseFanoutSource)
    {
        _connectionId = connectionId;
        _requestIngress = requestIngress;
        _responseFanoutSource = responseFanoutSource;
        Shape = new FlowShape<IFeatureCollection, IFeatureCollection>(_in, _out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionBridgeStage _stage;
        private bool _requestUpstreamFinished;
        private bool _responseStreamFinished;
        private ISourceQueueWithComplete<IFeatureCollection>? _requestQueue;
        private Action<IFeatureCollection>? _onResponseCallback;
        private Action<Exception?>? _onResponseCompleteCallback;
        private Action? _onRequestAcceptedCallback;
        private bool _downstreamWantsPull;
        private readonly Queue<IFeatureCollection> _responseBuffer = [];

        public Logic(ConnectionBridgeStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._in,
                onPush: OnRequestPush,
                onUpstreamFinish: () => OnRequestUpstreamFinish());

            SetHandler(stage._out,
                onPull: OnResponsePull,
                onDownstreamFinish: _ => OnResponseDownstreamFinish());
        }

        public override void PreStart()
        {
            _onResponseCallback = GetAsyncCallback<IFeatureCollection>(OnResponseReceived);
            _onResponseCompleteCallback = GetAsyncCallback<Exception?>(OnResponseStreamCompleted);
            _onRequestAcceptedCallback = GetAsyncCallback(() =>
            {
                if (!HasBeenPulled(_stage._in) && !IsClosed(_stage._in))
                {
                    Pull(_stage._in);
                }
            });

            MaterializeStreams();
            Pull(_stage._in);
        }

        private void OnResponseReceived(IFeatureCollection response)
        {
            _responseBuffer.Enqueue(response);
            TryEmitResponse();
        }

        private void OnResponseStreamCompleted(Exception? error)
        {
            _responseStreamFinished = true;
            if (error is not null)
            {
                FailStage(error);
            }
            else if (_requestUpstreamFinished)
            {
                CompleteStage();
            }
        }

        private void MaterializeStreams()
        {
            var requestQueueSource = Source.Queue<IFeatureCollection>(
                bufferSize: 64,
                overflowStrategy: OverflowStrategy.Backpressure);

            _requestQueue = requestQueueSource
                .Select(features =>
                {
                    features.Set(new ConnectionRoutingFeature { ConnectionId = _stage._connectionId });
                    return features;
                })
                .ToMaterialized(_stage._requestIngress, Keep.Left)
                .Run(Materializer);

            _stage._responseFanoutSource
                .ToMaterialized(
                    Sink.ForEach<IFeatureCollection>(response =>
                    {
                        _onResponseCallback!(response);
                    }),
                    Keep.Right)
                .Run(Materializer)
                .ContinueWith(task =>
                {
                    var error = task.IsFaulted ? task.Exception?.GetBaseException() : null;
                    _onResponseCompleteCallback!(error);
                },
                TaskScheduler.Current);
        }

        private void OnRequestPush()
        {
            var features = Grab(_stage._in);

            if (_requestQueue is null)
            {
                FailStage(new InvalidOperationException("Request queue not initialized"));
                return;
            }

            _requestQueue.OfferAsync(features).ContinueWith(
                (Task<IQueueOfferResult> task) =>
                {
                    if (task.IsFaulted)
                    {
                        _onResponseCompleteCallback!(task.Exception?.GetBaseException());
                    }
                    else if (task.IsCompletedSuccessfully)
                    {
                        _onRequestAcceptedCallback!();
                    }
                },
                TaskScheduler.Current);
        }

        private void OnRequestUpstreamFinish()
        {
            _requestUpstreamFinished = true;
            _requestQueue?.Complete();

            if (_responseStreamFinished)
            {
                CompleteStage();
            }
        }

        private void OnResponsePull()
        {
            _downstreamWantsPull = true;
            TryEmitResponse();
        }

        private void TryEmitResponse()
        {
            while (_downstreamWantsPull && _responseBuffer.Count > 0)
            {
                _downstreamWantsPull = false;
                Push(_stage._out, _responseBuffer.Dequeue());
            }
        }

        private void OnResponseDownstreamFinish()
        {
            _responseStreamFinished = true;
            CompleteStage();
        }
    }
}
