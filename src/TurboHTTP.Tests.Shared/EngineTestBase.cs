using System.Text;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http3;
using Xunit;

namespace TurboHTTP.Tests.Shared;

public abstract class EngineTestBase
{
    private static readonly ActorSystem _sharedSystem;
    protected static readonly IMaterializer Materializer;

    static EngineTestBase()
    {
        _sharedSystem = ActorSystem.Create("acceptance-tests");
        Materializer = _sharedSystem.Materializer();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            _sharedSystem.Terminate().Wait(TimeSpan.FromSeconds(10));
    }

    internal static TestConnectionStage CreateFakeConnection(Func<byte[]> responseFactory)
    {
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .Build();

        stage.PushResponse(outbound => outbound is TransportData
            ? new TransportData(responseFactory())
            : null);

        return stage;
    }

    internal static Flow<ITransportOutbound, ITransportInbound, NotUsed> CreateFakeConnectionFlow(
        Func<byte[]> responseFactory)
        => CreateFakeConnection(responseFactory).AsFlow();

    internal static TestConnectionStage CreateScriptedConnection(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<TransportData>((data, ctx) =>
            {
                var bytes = data.Buffer.Span.ToArray();
                var response = responseFactory(index++, bytes);
                if (response is null)
                {
                    ctx.Complete();
                    return;
                }

                ctx.Push(new TransportData(response));
            })
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateProxyConnection(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var tunnelEstablished = false;
        var connectEstablishedBytes = Encoding.Latin1.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
        var stage = new TestConnectionStageBuilder()
            .OnOutbound<ConnectTransport>((_, ctx) =>
            {
                tunnelEstablished = true;
                ctx.Push(new TransportData(connectEstablishedBytes));
            })
            .OnOutbound<TransportData>((data, ctx) =>
            {
                if (!tunnelEstablished)
                {
                    return;
                }

                var bytes = data.Buffer.Span.ToArray();
                var response = responseFactory(index++, bytes);
                if (response is null)
                {
                    ctx.Complete();
                    return;
                }

                ctx.Push(new TransportData(response));
            })
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateH2Connection(params byte[][] serverFrames)
    {
        var frameIndex = 0;

        void PushNextFrame(IStageContext ctx)
        {
            if (frameIndex < serverFrames.Length)
            {
                ctx.Push(new TransportData(serverFrames[frameIndex++]));
            }
        }

        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<ConnectTransport>((_, ctx) => PushNextFrame(ctx))
            .OnOutbound<TransportData>((_, ctx) => PushNextFrame(ctx))
            .Build();
        return stage;
    }

    internal static TestConnectionStage CreateH3Connection(params byte[][] serverFrames)
    {
        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<CompleteWrites>((_, ctx) =>
            {
                for (var i = 0; i < serverFrames.Length; i++)
                {
                    var buf = serverFrames[i];
                    if (i == 0)
                    {
                        ctx.Push(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
                        ctx.Push(new MultiplexedData(buf, 3));
                    }
                    else
                    {
                        ctx.Push(new MultiplexedData(buf, 0));
                    }
                }

                if (serverFrames.Length > 1)
                {
                    ctx.Push(new StreamReadCompleted(0));
                }
            })
            .Build();
        return stage;
    }

    internal async Task<(HttpResponseMessage Response, string RawRequest)> SendAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound,
            ITransportInbound, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        Func<byte[]> responseFactory)
    {
        var stage = CreateFakeConnection(responseFactory);

        var response = await TestPipeline.RunAsync(
            engine.Join(stage.AsFlow()), request, Materializer,
            ct: TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        foreach (var outbound in stage.ReceivedOutbound)
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
        }

        return (response, rawBuilder.ToString());
    }

    internal async Task<(IReadOnlyList<HttpResponseMessage> Responses, string RawRequests)> SendManyAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
        IEnumerable<HttpRequestMessage> requests,
        Func<byte[]> responseFactory,
        int expectedCount)
    {
        var stage = CreateFakeConnection(responseFactory);

        var results = await TestPipeline.RunManyAsync(
            engine.Join(stage.AsFlow()), requests, expectedCount, Materializer, ct:
            TestContext.Current.CancellationToken);

        var rawBuilder = new StringBuilder();
        foreach (var outbound in stage.ReceivedOutbound)
        {
            if (outbound is TransportData { Buffer: var buf })
            {
                rawBuilder.Append(Encoding.Latin1.GetString(buf.Span));
            }
        }

        return (results, rawBuilder.ToString());
    }

    internal async Task<(HttpResponseMessage Response, IReadOnlyList<Http2Frame> OutboundFrames)> SendH2EngineAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var stage = CreateH2Connection(serverFrames);
        var flow = engine.Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var outboundBytes = DrainOutboundBytes(stage, stripH2Preface: true);

        var frames = outboundBytes.Count > 0
            ? new Protocol.Http2.FrameDecoder().Decode(outboundBytes.ToArray())
            : [];

        return (response, frames);
    }

    internal async Task<(List<HttpResponseMessage> Responses, IReadOnlyList<Http2Frame> OutboundFrames)>
        SendH2EngineAsyncMany(
            BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
            IEnumerable<HttpRequestMessage> requests,
            int expectedCount,
            params byte[][] serverFrames)
    {
        var stage = CreateH2Connection(serverFrames);
        var flow = engine.Join(stage.AsFlow());

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

        var outboundBytes = DrainOutboundBytes(stage, stripH2Preface: true);

        var frames = outboundBytes.Count > 0
            ? new Protocol.Http2.FrameDecoder().Decode(outboundBytes.ToArray())
            : [];

        return (results, frames);
    }

    internal async Task<(HttpResponseMessage Response, IReadOnlyList<Http3Frame> OutboundFrames)> SendH3EngineAsync(
        BidiFlow<HttpRequestMessage, ITransportOutbound, ITransportInbound, HttpResponseMessage, NotUsed> engine,
        HttpRequestMessage request,
        params byte[][] serverFrames)
    {
        var stage = CreateH3Connection(serverFrames);
        var flow = engine.Join(stage.AsFlow());

        var tcs = new TaskCompletionSource<HttpResponseMessage>();

        _ = Source.Single(request)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res => tcs.TrySetResult(res)), Materializer);

        var response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var requestBytes = new List<byte>();
        var controlBytes = new List<byte>();
        while (stage.TryGetOutbound(out var outbound))
        {
            switch (outbound)
            {
                case MultiplexedData { Buffer: var buf, StreamId: var streamId }:
                    var bytes = buf.Span.ToArray();
                    switch (streamId)
                    {
                        case -2:
                            controlBytes.AddRange(bytes);
                            break;
                        case -4:
                        case -3:
                            break;
                        default:
                            requestBytes.AddRange(bytes);
                            break;
                    }

                    break;
                case TransportData { Buffer: var dataBuf }:
                    requestBytes.AddRange(dataBuf.Span.ToArray());
                    break;
            }
        }

        var frames = new List<Http3Frame>();

        if (requestBytes.Count > 0)
        {
            frames.AddRange(new Protocol.Http3.FrameDecoder().DecodeAll(requestBytes.ToArray(), out _));
        }

        if (controlBytes.Count > 0)
        {
            var controlSpan = controlBytes.ToArray().AsSpan();
            if (controlSpan.Length > 0 && controlSpan[0] == 0x00)
            {
                controlSpan = controlSpan[1..];
            }

            if (controlSpan.Length > 0)
            {
                frames.AddRange(new Protocol.Http3.FrameDecoder().DecodeAll(controlSpan.ToArray(), out _));
            }
        }

        return (response, frames);
    }

    private static List<byte> DrainOutboundBytes(TestConnectionStage stage, bool stripH2Preface)
    {
        ReadOnlySpan<byte> preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;
        var bytes = new List<byte>();
        var prefaceStripped = false;

        while (stage.TryGetOutbound(out var outbound))
        {
            if (outbound is not TransportData { Buffer: var buf })
            {
                continue;
            }

            var span = buf.Span;
            if (stripH2Preface && !prefaceStripped)
            {
                prefaceStripped = true;
                if (span.Length >= 24 && span[..24].SequenceEqual(preface))
                {
                    var remainder = span[24..];
                    if (remainder.Length > 0)
                    {
                        bytes.AddRange(remainder.ToArray());
                    }

                    continue;
                }
            }

            bytes.AddRange(span.ToArray());
        }

        return bytes;
    }
}