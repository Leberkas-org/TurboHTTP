using System.Text;
using System.Threading.Channels;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit.Xunit;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.StreamTests;

/// <summary>
/// Fake TCP connection stage for HTTP/1.x engine tests.
/// Intercepts outbound serialised bytes and injects a synthetic response produced by a caller-supplied factory.
/// </summary>
/// <remarks>
/// Exposes <see cref="EngineFakeConnectionStage.OutboundChannel"/> so tests can inspect the raw bytes sent by the encoder.
/// </remarks>
public sealed class EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly Func<byte[]> _responseFactory;

    public Channel<NetworkBuffer> OutboundChannel { get; } = Channel.CreateUnbounded<NetworkBuffer>();

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
        private readonly Queue<NetworkBuffer> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(EngineFakeConnectionStage stage) : base(stage.Shape)
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
                        stage.OutboundChannel.Writer.TryWrite(NetworkBuffer.FromArray(copy));
                        dataChunk.Dispose();

                        var responseBytes = _stage._responseFactory();

                        if (_downstreamWaiting)
                        {
                            _downstreamWaiting = false;
                            Push(stage.Out, NetworkBuffer.FromArray(responseBytes));
                        }
                        else
                        {
                            _buffer.Enqueue(NetworkBuffer.FromArray(responseBytes));
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
/// Fake TCP connection stage for HTTP/2 engine tests.
/// Intercepts outbound H2 frames (skipping the preface) and injects pre-queued server frames one per request.
/// </summary>
/// <remarks>
/// Exposes <see cref="H2EngineFakeConnectionStage.OutboundChannel"/> so tests can decode and inspect outbound H2 frames.
/// </remarks>
public sealed class H2EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<NetworkBuffer> OutboundChannel { get; } =
        Channel.CreateUnbounded<NetworkBuffer>();

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
                    else if (item is NetworkBuffer dataChunk)
                    {
                        var span = dataChunk.Span;
                        if (span.Length >= 24 && span[..24].SequenceEqual(H2Preface))
                        {
                            var remainder = span[24..];
                            if (remainder.Length > 0)
                            {
                                stage.OutboundChannel.Writer.TryWrite(NetworkBuffer.FromArray(remainder.ToArray()));
                            }
                        }
                        else
                        {
                            stage.OutboundChannel.Writer.TryWrite(NetworkBuffer.FromArray(span.ToArray()));
                        }

                        dataChunk.Dispose();
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
            Push(_stage.Out, NetworkBuffer.FromArray(frameBytes));
        }

        public override void PreStart() => Pull(_stage.In);
    }
}

/// <summary>
/// Abstract base class for engine round-trip tests.
/// Provides SendAsync/SendManyAsync helpers that pipe requests through an engine and a fake connection stage.
/// </summary>
/// <remarks>
/// Inherits from TestKit; uses <see cref="EngineFakeConnectionStage"/> and <see cref="H2EngineFakeConnectionStage"/> to simulate TCP connections.
/// </remarks>
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

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
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

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            rawBuilder.Append(Encoding.Latin1.GetString(chunk.Span));
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

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var outboundBytes = await DrainOutboundH2Async(fake);

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

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var outboundBytes = await DrainOutboundH2Async(fake);

        var frames = outboundBytes.Count > 0
            ? new Http2FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (results, frames);
    }

    /// <summary>
    /// Runs Http30Engine against pre-queued server frames.
    /// Returns the decoded response and all outbound H3 frames.
    /// </summary>
    protected async Task<(HttpResponseMessage Response, IReadOnlyList<Http3Frame> OutboundFrames)> SendH3EngineAsync(
        BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var fake = new H3EngineFakeConnectionStage(serverFrames);
        var flow = engine.Join(Flow.FromGraph<IOutputItem, IInputItem, NotUsed>(fake));

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var requestBytes = new List<byte>();
        var controlBytes = new List<byte>();
        while (fake.OutboundChannel.Reader.TryRead(out var chunk))
        {
            var bytes = chunk.Buffer.Span.ToArray();
            switch (chunk.StreamType)
            {
                case OutputStreamType.Control:
                    controlBytes.AddRange(bytes);
                    break;
                case OutputStreamType.QpackEncoder:
                    // QPACK encoder instructions — not HTTP/3 frames, skip.
                    break;
                default:
                    requestBytes.AddRange(bytes);
                    break;
            }
        }

        var frames = new List<Http3Frame>();

        if (requestBytes.Count > 0)
        {
            frames.AddRange(new Http3FrameDecoder().DecodeAll(requestBytes.ToArray(), out _));
        }

        if (controlBytes.Count > 0)
        {
            // Control stream bytes start with stream type VarInt(0x00); skip it.
            var controlSpan = controlBytes.ToArray().AsSpan();
            if (controlSpan.Length > 0 && controlSpan[0] == 0x00)
            {
                controlSpan = controlSpan[1..];
            }

            if (controlSpan.Length > 0)
            {
                frames.AddRange(new Http3FrameDecoder().DecodeAll(controlSpan.ToArray(), out _));
            }
        }

        return (response, frames);
    }

    /// <summary>
    /// Drains the H2 fake stage outbound channel, waiting briefly for in-flight frames
    /// that may still be traversing the outbound pipeline after the response has arrived.
    /// </summary>
    private static async Task<List<byte>> DrainOutboundH2Async(H2EngineFakeConnectionStage fake)
    {
        var outboundBytes = new List<byte>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        try
        {
            while (await fake.OutboundChannel.Reader.WaitToReadAsync(cts.Token))
            {
                while (fake.OutboundChannel.Reader.TryRead(out var chunk))
                {
                    outboundBytes.AddRange(chunk.Span.ToArray());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout — drain any remaining items synchronously.
            while (fake.OutboundChannel.Reader.TryRead(out var chunk))
            {
                outboundBytes.AddRange(chunk.Span.ToArray());
            }
        }

        return outboundBytes;
    }
}

/// <summary>
/// Fake TCP connection stage for HTTP/3 engine tests.
/// Intercepts outbound H3 frames and injects pre-queued server frames one per outbound push.
/// </summary>
public sealed class H3EngineFakeConnectionStage : GraphStage<FlowShape<IOutputItem, IInputItem>>
{
    private readonly IReadOnlyList<byte[]> _serverFrames;

    public Channel<(NetworkBuffer Buffer, OutputStreamType? StreamType)> OutboundChannel { get; } =
        Channel.CreateUnbounded<(NetworkBuffer, OutputStreamType?)>();

    public Inlet<IOutputItem> In { get; } = new("h3-engine-fake.in");
    public Outlet<IInputItem> Out { get; } = new("h3-engine-fake.out");

    public override FlowShape<IOutputItem, IInputItem> Shape { get; }

    public H3EngineFakeConnectionStage(params byte[][] serverFrames)
    {
        _serverFrames = serverFrames;
        Shape = new FlowShape<IOutputItem, IInputItem>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly H3EngineFakeConnectionStage _stage;
        private int _serverFrameIndex;
        private int _unlockedFrames;
        private bool _downstreamWaiting;

        public Logic(H3EngineFakeConnectionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var item = Grab(stage.In);

                    // Unwrap tagged items (control preface, QPACK encoder, etc.)
                    OutputStreamType? streamType = null;
                    var inner = item;
                    if (item is Http3OutputTaggedItem tagged)
                    {
                        streamType = tagged.StreamType;
                        inner = tagged.Inner;
                    }

                    if (inner is NetworkBuffer dataChunk)
                    {
                        stage.OutboundChannel.Writer.TryWrite((NetworkBuffer.FromArray(dataChunk.Span.ToArray()), streamType));
                        dataChunk.Dispose();
                    }

                    // Every outbound push (tagged or not) unlocks a server frame.
                    Unlock();

                    if (!IsClosed(stage.In))
                    {
                        Pull(stage.In);
                    }
                },
                onUpstreamFinish: () =>
                {
                    if (!IsClosed(stage.Out))
                    {
                        Complete(stage.Out);
                    }
                },
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
                onDownstreamFinish: _ =>
                {
                    if (!IsClosed(stage.In))
                    {
                        Cancel(stage.In);
                    }
                });
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
            Push(_stage.Out, NetworkBuffer.FromArray(frameBytes));

            // HTTP/3 relies on QUIC FIN (upstream completion) to signal stream end.
            // After all server frames are delivered, complete the output to propagate
            // through the decoder → connection → stream pipeline.
            if (_serverFrameIndex >= _stage._serverFrames.Count)
            {
                Complete(_stage.Out);
            }
        }

        public override void PreStart() => Pull(_stage.In);
    }
}