using System.Diagnostics;
using System.Text;
using Akka;
using Akka.Streams.Dsl;
using Servus.Akka.TestKit;
using Servus.Akka.Transport;
using Xunit;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http3;
using FrameDecoder = TurboHTTP.Protocol.Syntax.Http2.FrameDecoder;

namespace TurboHTTP.Tests.Shared;

public abstract class EngineTestBase : StreamTestBase
{
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

    internal static TestConnectionStage CreateAccumulatingScriptedConnection(Func<int, byte[], byte[]?> responseFactory)
    {
        var index = 0;
        var accumulated = new List<byte>();

        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<TransportData>((data, ctx) =>
            {
                accumulated.AddRange(data.Buffer.Span.ToArray());

                var (headerEnd, contentLength) = TryParseRequest(accumulated);
                if (headerEnd < 0)
                {
                    return;
                }

                var totalExpected = headerEnd + 4 + contentLength;
                if (accumulated.Count < totalExpected)
                {
                    return;
                }

                var completeRequest = accumulated.GetRange(0, totalExpected).ToArray();
                accumulated.RemoveRange(0, totalExpected);

                var response = responseFactory(index++, completeRequest);
                if (response is null)
                {
                    ctx.Complete();
                    return;
                }

                ctx.Push(new TransportData(response));
            })
            .Build();
        return stage;

        static (int HeaderEnd, int ContentLength) TryParseRequest(List<byte> bytes)
        {
            var arr = bytes.ToArray();
            var headerEnd = arr.AsSpan().IndexOf("\r\n\r\n"u8);
            if (headerEnd < 0)
            {
                return (-1, 0);
            }

            var headerStr = Encoding.Latin1.GetString(arr, 0, headerEnd);
            var contentLength = 0;
            foreach (var line in headerStr.Split("\r\n"))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                    break;
                }
            }

            return (headerEnd, contentLength);
        }
    }

    internal static TestConnectionStage CreateScriptedConnectionWithClose(Func<int, byte[], byte[]?> responseFactory)
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
                ctx.Push(new TransportDisconnected(DisconnectReason.Graceful));
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
        var transportDataCount = 0;

        var stage = new TestConnectionStageBuilder()
            .AutoConnect()
            .OnOutbound<TransportData>((msg, ctx) =>
            {
                transportDataCount++;

                if (transportDataCount == 1)
                {
                    // Skip first TransportData (HTTP/2 preface + SETTINGS)
                    return;
                }

                PushNextFrame(ctx);
            })
            .Build();
        return stage;

        void PushNextFrame(IStageContext ctx)
        {
            if (frameIndex < serverFrames.Length)
            {
                ctx.Push(new TransportData(serverFrames[frameIndex++]));
            }
        }
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

        // Wait for body encoder to finish sending DATA frames (async via actor messages).
        // The body encoder is started in a fire-and-forget task which may not complete
        // before the response is returned. Give the actor system time to process all messages.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        var outboundBytes = DrainOutboundBytes(stage, stripH2Preface: true);

        var frames = outboundBytes.Count > 0
            ? new FrameDecoder().Decode(outboundBytes.ToArray())
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

        var results = await Source.From(requests)
            .Via(flow)
            .RunWith(Sink.Seq<HttpResponseMessage>(), Materializer);

        // Sink.Seq keeps the stream alive until the source completes, which allows
        // any buffered DATA frames from async body encoders to be flushed through the outlet.
        // Additional delay to allow actor system to process any remaining messages.
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var outboundBytes = DrainOutboundBytes(stage, stripH2Preface: true);

        var frames = outboundBytes.Count > 0
            ? new FrameDecoder().Decode(outboundBytes.ToArray())
            : [];

        return (results.ToList(), frames);
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

        // Wait for body encoder to finish sending DATA frames (async via actor messages)
        await Task.Delay(100, TestContext.Current.CancellationToken);

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
            frames.AddRange(new Protocol.Syntax.Http3.FrameDecoder().DecodeAll(requestBytes.ToArray(), out _));
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
                frames.AddRange(new Protocol.Syntax.Http3.FrameDecoder().DecodeAll(controlSpan.ToArray(), out _));
            }
        }

        return (response, frames);
    }

    private static List<byte> DrainOutboundBytes(TestConnectionStage stage, bool stripH2Preface)
    {
        var preface = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;
        var bytes = new List<byte>();
        var prefaceStripped = false;
        var messageCount = 0;

        while (stage.TryGetOutbound(out var outbound))
        {
            messageCount++;
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

        Debug.WriteLine($"DrainOutboundBytes: {messageCount} outbound messages, {bytes.Count} total bytes");

        return bytes;
    }
}