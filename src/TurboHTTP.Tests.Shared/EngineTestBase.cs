using System.Text;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http3;
using Xunit;
using FrameDecoder = TurboHTTP.Protocol.Http3.FrameDecoder;

namespace TurboHTTP.Tests.Shared;

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

    internal async Task<(HttpResponseMessage Response, string RawRequest)> SendAsync(
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

    internal async Task<(List<HttpResponseMessage> Responses, string RawRequests)> SendManyAsync(
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
    internal async Task<(HttpResponseMessage Response, IReadOnlyList<Http2Frame> OutboundFrames)> SendH2EngineAsync(
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
            ? new Protocol.Http2.FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (response, frames);
    }

    /// <summary>
    /// Runs Http20Engine (ITransportItem variant) with multiple requests against pre-queued server frames.
    /// Returns all decoded responses and all outbound H2 frames.
    /// </summary>
    internal async Task<(List<HttpResponseMessage> Responses, IReadOnlyList<Http2Frame> OutboundFrames)>
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
            ? new Protocol.Http2.FrameDecoder().Decode(outboundBytes.ToArray().AsMemory())
            : [];

        return (results, frames);
    }

    /// <summary>
    /// Runs Http30Engine against pre-queued server frames.
    /// Returns the decoded response and all outbound H3 frames.
    /// </summary>
    internal async Task<(HttpResponseMessage Response, IReadOnlyList<Http3Frame> OutboundFrames)> SendH3EngineAsync(
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
                case Http3StreamType.Control:
                    controlBytes.AddRange(bytes);
                    break;
                case Http3StreamType.QpackEncoder:
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
            frames.AddRange(new FrameDecoder().DecodeAll(requestBytes.ToArray(), out _));
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
                frames.AddRange(new FrameDecoder().DecodeAll(controlSpan.ToArray(), out _));
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