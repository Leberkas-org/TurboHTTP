using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Fake TCP connection stage that routes responses through a caller-supplied factory
/// receiving the request index and raw outbound bytes.
/// Supports multi-response sequences (connection reuse) and error injection
/// (truncated body, abort, corrupt bytes) via the response factory.
/// Optionally accepts a BehaviorStack to override the factory and an ActivityLog to record events.
/// </summary>
public sealed class ScriptedFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Func<int, byte[], byte[]?> _responseFactory;
    private readonly BehaviorStack<(int Index, byte[] RequestBytes), byte[]?>? _behaviorStack;
    private readonly ActivityLog? _activityLog;

    public Channel<NetworkBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<NetworkBuffer>();

    public Inlet<IOutputItem> In { get; } = new("ScriptedFakeConnection.In");
    public Outlet<IInputItem> Out { get; } = new("ScriptedFakeConnection.Out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    /// <summary>
    /// Creates a scripted fake connection stage.
    /// </summary>
    /// <param name="responseFactory">
    /// Factory that receives (requestIndex, outboundBytes) and returns response bytes.
    /// Return <c>null</c> to abort the connection (simulates server closing mid-stream).
    /// Return a truncated or corrupt byte array to simulate error conditions.
    /// </param>
    public ScriptedFakeConnectionStage(Func<int, byte[], byte[]?> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    /// <summary>
    /// Creates a scripted fake connection stage with optional behavior override and activity observation.
    /// </summary>
    /// <param name="responseFactory">Default factory used when the BehaviorStack is empty.</param>
    /// <param name="behaviorStack">When provided, its topmost behavior handles each request instead of the factory.</param>
    /// <param name="activityLog">When provided, records WriteAttempt, ResponseDelivered, and ConnectionAbort events.</param>
    public ScriptedFakeConnectionStage(
        Func<int, byte[], byte[]?> responseFactory,
        BehaviorStack<(int Index, byte[] RequestBytes), byte[]?>? behaviorStack,
        ActivityLog? activityLog = null)
    {
        _responseFactory = responseFactory;
        _behaviorStack = behaviorStack;
        _activityLog = activityLog;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ScriptedFakeConnectionStage _stage;
        private readonly Queue<NetworkBuffer> _buffer = new();
        private bool _downstreamWaiting;
        private int _requestIndex;

        public Logic(ScriptedFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is NetworkBuffer dataChunk)
                    {
                        var copy = new byte[dataChunk.Length];
                        dataChunk.Span.CopyTo(copy);
                        stage.OutboundChannel.Writer.TryWrite(NetworkBufferTestExtensions.FromArray(copy));
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
                            Push(stage.Out, NetworkBufferTestExtensions.FromArray(responseBytes));
                        }
                        else
                        {
                            _buffer.Enqueue(NetworkBufferTestExtensions.FromArray(responseBytes));
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
