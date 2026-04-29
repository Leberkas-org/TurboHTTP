using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal sealed class ScriptedFakeConnectionStage : GraphStage<FlowShape<ITransportOutbound, ITransportInbound>>
{
    private readonly Func<int, byte[], byte[]?> _responseFactory;
    private readonly BehaviorStack<(int Index, byte[] RequestBytes), byte[]?>? _behaviorStack;
    private readonly ActivityLog? _activityLog;

    public Channel<TransportBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<TransportBuffer>();

    public Inlet<ITransportOutbound> In { get; } = new("ScriptedFakeConnection.In");
    public Outlet<ITransportInbound> Out { get; } = new("ScriptedFakeConnection.Out");

    public override FlowShape<ITransportOutbound, ITransportInbound> Shape { get; }

    public ScriptedFakeConnectionStage(Func<int, byte[], byte[]?> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    public ScriptedFakeConnectionStage(
        Func<int, byte[], byte[]?> responseFactory,
        BehaviorStack<(int Index, byte[] RequestBytes), byte[]?>? behaviorStack,
        ActivityLog? activityLog = null)
    {
        _responseFactory = responseFactory;
        _behaviorStack = behaviorStack;
        _activityLog = activityLog;
        Shape = new FlowShape<ITransportOutbound, ITransportInbound>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ScriptedFakeConnectionStage _stage;
        private readonly Queue<ITransportInbound> _buffer = new();
        private bool _downstreamWaiting;
        private int _requestIndex;

        public Logic(ScriptedFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is TransportData { Buffer: var dataChunk })
                    {
                        var copy = new byte[dataChunk.Length];
                        dataChunk.Span.CopyTo(copy);
                        stage.OutboundChannel.Writer.TryWrite(TransportBufferTestExtensions.FromArray(copy));
                        dataChunk.Dispose();

                        var index = _requestIndex++;
                        stage._activityLog?.Record(new WriteAttempt(index, copy));

                        byte[]? responseBytes;
                        if (stage._behaviorStack is not null)
                        {
                            responseBytes = stage._behaviorStack.Apply((index, copy));
                        }
                        else
                        {
                            responseBytes = stage._responseFactory(index, copy);
                        }

                        if (responseBytes is null)
                        {
                            stage._activityLog?.Record(new ConnectionAbort());
                            CompleteStage();
                            return;
                        }

                        stage._activityLog?.Record(new ResponseDelivered(index, responseBytes.Length));

                        if (_downstreamWaiting)
                        {
                            _downstreamWaiting = false;
                            Push(stage.Out, new TransportData(TransportBufferTestExtensions.FromArray(responseBytes)));
                        }
                        else
                        {
                            _buffer.Enqueue(new TransportData(TransportBufferTestExtensions.FromArray(responseBytes)));
                        }
                    }

                    if (!IsClosed(stage.In))
                    {
                        Pull(stage.In);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var chunk))
                    {
                        Push(stage.Out, chunk);
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}
