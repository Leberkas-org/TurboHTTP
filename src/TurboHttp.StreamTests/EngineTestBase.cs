using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit.Xunit2;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.StreamTests;

public sealed class EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Func<byte[]> _responseFactory;

    public Channel<DataItem> OutboundChannel { get; } = Channel.CreateUnbounded<DataItem>();

    public Inlet<IOutputItem> In { get; } = new("fake-tcp.in");
    public Outlet<IInputItem> Out { get; } = new("fake-tcp.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public EngineFakeConnectionStage(Func<byte[]> responseFactory)
    {
        _responseFactory = responseFactory;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
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
                    if (item is DataItem(var owner, var length))
                    {
                        var copy = new byte[length];
                        owner.Memory.Span[..length].CopyTo(copy);
                        stage.OutboundChannel.Writer.TryWrite(new DataItem(new SimpleMemoryOwner(copy), length)
                            { Key = RequestEndpoint.Default });
                        owner.Dispose();

                        var responseBytes = _stage._responseFactory();
                        IMemoryOwner<byte> responseOwner = new SimpleMemoryOwner(responseBytes);

                        if (_downstreamWaiting)
                        {
                            _downstreamWaiting = false;
                            Push(stage.Out,
                                new DataItem(responseOwner, responseBytes.Length) { Key = RequestEndpoint.Default });
                        }
                        else
                        {
                            _buffer.Enqueue((responseOwner, responseBytes.Length));
                        }
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
                        Push(stage.Out, new DataItem(chunk.Item1, chunk.Item2) { Key = RequestEndpoint.Default });
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
/// H2-aware fake TCP stage that accepts <see cref="IOutputItem"/> input (as produced by Http20Engine).
/// Inbound (In): captures outbound DataItem bytes for inspection, always pulls more.
/// Outbound (Out): serves pre-queued server frames when downstream pulls.
/// </summary>
public sealed class H2EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<(IMemoryOwner<byte>, int)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    public Inlet<IOutputItem> In { get; } = new("h2-engine-fake.in");
    public Outlet<IInputItem> Out { get; } = new("h2-engine-fake.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public H2EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private static ReadOnlySpan<byte> H2Preface => "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        private readonly H2EngineFakeConnectionStage _stage;
        private int _serverFrameIndex;
        private int _unlockedFrames;
        private bool _downstreamWaiting;

        public Logic(H2EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);
                    if (item is ConnectItem)
                    {
                        Unlock();
                    }
                    else if (item is DataItem(var owner, var length))
                    {
                        var span = owner.Memory.Span[..length];
                        if (length >= 24 && span[..24].SequenceEqual(H2Preface))
                        {
                            var remainder = span[24..];
                            if (remainder.Length > 0)
                            {
                                var copy = remainder.ToArray();
                                stage.OutboundChannel.Writer.TryWrite((new SimpleMemoryOwner(copy), copy.Length));
                            }
                        }
                        else
                        {
                            var copy = new byte[length];
                            span.CopyTo(copy);
                            stage.OutboundChannel.Writer.TryWrite((new SimpleMemoryOwner(copy), length));
                        }

                        owner.Dispose();
                        Unlock();
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_unlockedFrames > 0 && _serverFrameIndex < _stage._serverFrames.Count)
                    {
                        _unlockedFrames--;
                        PushNextFrame();
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        private void Unlock()
        {
            if (_downstreamWaiting && _serverFrameIndex < _stage._serverFrames.Count)
            {
                _downstreamWaiting = false;
                PushNextFrame();
            }
            else
            {
                _unlockedFrames++;
            }
        }

        private void PushNextFrame()
        {
            var frameBytes = _stage._serverFrames[_serverFrameIndex++];
            IMemoryOwner<byte> frameOwner = new SimpleMemoryOwner(frameBytes);
            Push(_stage.Out, new DataItem(frameOwner, frameBytes.Length) { Key = RequestEndpoint.Default });
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
        BidiFlow<HttpRequestMessage, IOutputItem,
            IInputItem, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        Func<byte[]> responseFactory)
    {
        var fake = new EngineFakeConnectionStage(responseFactory);
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Memory.Memory.Span[..chunk.Length]));
        }

        return (response, rawBuilder.ToString());
    }

    protected async Task<(List<HttpResponseMessage> Responses, string RawRequests)> SendManyAsync(
        BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> engine,
        IEnumerable<HttpRequestMessage> requests,
        Func<byte[]> responseFactory,
        int expectedCount)
    {
        var fake = new EngineFakeConnectionStage(responseFactory);
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

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
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Memory.Memory.Span[..chunk.Length]));
        }

        return (results, rawBuilder.ToString());
    }

    /// <summary>
    /// Runs Http20Engine (ITransportItem variant) against pre-queued server frames.
    /// Returns the decoded response and all outbound H2 frames.
    /// </summary>
    protected async Task<(HttpResponseMessage Response, IReadOnlyList<Http2Frame> OutboundFrames)> SendH2EngineAsync(
        BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var fake = new H2EngineFakeConnectionStage(serverFrames);
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var outboundBytes = new List<byte>();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            outboundBytes.AddRange(chunk.Item1.Memory.Span[..chunk.Item2].ToArray());
        }

        var frames = outboundBytes.Count > 0
            ? new Http2FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (response, frames);
    }

    /// <summary>
    /// Runs Http20Engine (ITransportItem variant) with multiple requests against pre-queued server frames.
    /// Returns all decoded responses and all outbound H2 frames.
    /// </summary>
    protected async Task<(List<HttpResponseMessage> Responses, IReadOnlyList<Http2Frame> OutboundFrames)>
        SendH2EngineAsyncMany(
            BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> engine,
            IEnumerable<HttpRequestMessage> requests,
            int expectedCount,
            params byte[][] serverFrames)
    {
        var fake = new H2EngineFakeConnectionStage(serverFrames);
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

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
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            outboundBytes.AddRange(chunk.Item1.Memory.Span[..chunk.Item2].ToArray());
        }

        var frames = outboundBytes.Count > 0
            ? new Http2FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (results, frames);
    }

}