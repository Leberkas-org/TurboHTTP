using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit.Xunit2;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.StreamTests;

public sealed class
    EngineFakeConnectionStage : GraphStage<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>>
{
    private readonly Func<byte[]> _responseFactory;

    public Channel<(IMemoryOwner<byte>, int)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    public Inlet<ITransportItem> In { get; } = new("fake-tcp.in");
    public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("fake-tcp.out");

    public override FlowShape<ITransportItem, (IMemoryOwner<byte>, int)> Shape { get; }

    public EngineFakeConnectionStage(Func<byte[]> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly EngineFakeConnectionStage _stage;
        private readonly Queue<(IMemoryOwner<byte>, int)> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is not DataItem(var owner, var length, _)) return;

                    var copy = new byte[length];
                    owner.Memory.Span[..length].CopyTo(copy);
                    stage.OutboundChannel.Writer.TryWrite((new SimpleMemoryOwner(copy), length));
                    owner.Dispose();

                    var responseBytes = _stage._responseFactory();
                    IMemoryOwner<byte> responseOwner = new SimpleMemoryOwner(responseBytes);

                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(stage.Out, (responseOwner, responseBytes.Length));
                    }
                    else
                    {
                        _buffer.Enqueue((responseOwner, responseBytes.Length));
                    }

                    Pull(stage.In);
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

/// <summary>
/// H2-aware fake TCP stage with pre-queued server frames.
/// Inbound (In): captures all outbound engine bytes for inspection, always pulls more.
/// Outbound (Out): serves server frames from a pre-built queue when downstream pulls.
/// The two sides are completely independent — avoids Emit/pull-twice conflicts in Http2ConnectionStage.
/// </summary>
public sealed class H2FakeConnectionStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<(IMemoryOwner<byte>, int)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    public Inlet<(IMemoryOwner<byte>, int)> In { get; } = new("h2-fake.in");
    public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("h2-fake.out");

    public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape { get; }

    public H2FakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly H2FakeConnectionStage _stage;
        private int _serverFrameIndex;

        public Logic(H2FakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            // Inbound: capture outbound engine bytes, always pull more.
            SetHandler(stage.In,
                onPush: () =>
                {
                    var (owner, length) = Grab(stage.In);
                    var copy = new byte[length];
                    owner.Memory.Span[..length].CopyTo(copy);
                    stage.OutboundChannel.Writer.TryWrite((new SimpleMemoryOwner(copy), length));
                    owner.Dispose();
                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            // Outbound: serve pre-queued server frames on demand.
            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_serverFrameIndex < _stage._serverFrames.Count)
                    {
                        var frameBytes = _stage._serverFrames[_serverFrameIndex++];
                        IMemoryOwner<byte> owner = new SimpleMemoryOwner(frameBytes);
                        Push(stage.Out, (owner, frameBytes.Length));
                    }
                    // If queue exhausted, downstream waits (stream stays open).
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}

public abstract class EngineTestBase : TestKit
{
    protected readonly IMaterializer Materializer;

    protected EngineTestBase() : base(ActorSystem.Create("engine-test-" + Guid.NewGuid()))
    {
        Materializer = Sys.Materializer();
    }

    protected async Task<(HttpResponseMessage Response, string RawRequest)> SendAsync(
        BidiFlow<HttpRequestMessage, ITransportItem,
            (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        Func<byte[]> responseFactory)
    {
        var fake = new EngineFakeConnectionStage(responseFactory);
        var flow = engine.Join(Flow.FromGraph<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Item1.Memory.Span[..chunk.Item2]));
        }

        return (response, rawBuilder.ToString());
    }

    protected async Task<(List<HttpResponseMessage> Responses, string RawRequests)> SendManyAsync(
        BidiFlow<HttpRequestMessage, ITransportItem,
            (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> engine,
        IEnumerable<HttpRequestMessage> requests,
        Func<byte[]> responseFactory,
        int expectedCount)
    {
        var fake = new EngineFakeConnectionStage(responseFactory);
        var flow = engine.Join(Flow.FromGraph<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == expectedCount)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Item1.Memory.Span[..chunk.Item2]));
        }

        return (results, rawBuilder.ToString());
    }

    /// <summary>
    /// Runs the H2 engine against pre-queued server frames, sending multiple requests.
    /// Returns all decoded responses and all outbound H2 frames (excluding the client preface chunk).
    /// </summary>
    protected async Task<(List<HttpResponseMessage> Responses, IReadOnlyList<Http2Frame> OutboundFrames)>
        SendH2ManyAsync(
            BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int),
                (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> engine,
            IEnumerable<HttpRequestMessage> requests,
            int expectedCount,
            params byte[][] serverFrames)
    {
        var fake = new H2FakeConnectionStage(serverFrames);
        var flow = engine.Join(Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == expectedCount)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var outboundBytes = new List<byte>();
        var skippedPreface = false;
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            var bytes = chunk.Item1.Memory.Span[..chunk.Item2].ToArray();
            if (!skippedPreface)
            {
                skippedPreface = true;
                continue;
            }

            outboundBytes.AddRange(bytes);
        }

        var frames = outboundBytes.Count > 0
            ? new Http2FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (results, frames);
    }

    /// <summary>
    /// Runs the H2 engine against pre-queued server frames. Returns the decoded response and
    /// all outbound H2 frames (excluding the client preface chunk).
    /// serverFrames: byte arrays served to the engine's inbound decoder in order.
    ///   Typically: [serverSettingsFrame, responseHeadersAndDataBytes]
    /// </summary>
    protected async Task<(HttpResponseMessage Response, IReadOnlyList<Http2Frame> OutboundFrames)> SendH2Async(
        BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int),
            (IMemoryOwner<byte>, int), HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var fake = new H2FakeConnectionStage(serverFrames);
        var flow = engine.Join(Flow.FromGraph<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Skip the first outbound chunk (client preface: PRI magic + client SETTINGS).
        // Collect and parse all subsequent outbound bytes as H2 frames.
        var outboundBytes = new List<byte>();
        var skippedPreface = false;
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            var bytes = chunk.Item1.Memory.Span[..chunk.Item2].ToArray();
            if (!skippedPreface)
            {
                skippedPreface = true;
                continue;
            }

            outboundBytes.AddRange(bytes);
        }

        var frames = outboundBytes.Count > 0
            ? new Http2FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (response, frames);
    }
}