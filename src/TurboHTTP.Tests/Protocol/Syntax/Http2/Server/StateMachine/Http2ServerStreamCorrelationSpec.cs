using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http2;
using TurboHTTP.Protocol.Syntax.Http2.Hpack;
using TurboHTTP.Protocol.Syntax.Http2.Server;
using TurboHTTP.Server;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Server.StateMachine;

/// <summary>
/// Unit tests for HTTP/2 Http2ServerStateMachine stream correlation.
/// Tests handling of multiple concurrent streams and correct response routing via stream IDs.
/// </summary>
public sealed class Http2ServerStreamCorrelationSpec
{
    private static IFeatureCollection CreateResponseContext(long streamId)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        return features;
    }


    private static ReadOnlyMemory<byte> EncodeHeaders(string method, string path, string authority = "localhost")
    {
        var encoder = new HpackEncoder(useHuffman: true);
        var headers = new List<HpackHeader>
        {
            new(":method", method),
            new(":path", path),
            new(":scheme", "https"),
            new(":authority", authority),
        };

        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = encoder.Encode(headers, ref span, useHuffman: true);

        return new Memory<byte>(buffer, 0, written);
    }

    private static byte[] BuildHeadersFrame(int streamId, ReadOnlyMemory<byte> headerBlock, bool endStream = false,
        bool endHeaders = true)
    {
        const int frameHeaderSize = 9;
        var frameSize = frameHeaderSize + headerBlock.Length;
        var frame = new byte[frameSize];

        var length = headerBlock.Length;
        frame[0] = (byte)(length >> 16);
        frame[1] = (byte)(length >> 8);
        frame[2] = (byte)length;
        frame[3] = (byte)FrameType.Headers;

        byte flags = 0;
        if (endStream) flags |= (byte)Headers.EndStream;
        if (endHeaders) flags |= (byte)Headers.EndHeaders;
        frame[4] = flags;

        frame[5] = (byte)(streamId >> 24);
        frame[6] = (byte)(streamId >> 16);
        frame[7] = (byte)(streamId >> 8);
        frame[8] = (byte)streamId;

        headerBlock.Span.CopyTo(frame.AsSpan(frameHeaderSize));

        return frame;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5")]
    public void Multiple_concurrent_streams_should_correlate_responses_to_correct_stream_ids()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send HEADERS on stream 1
        var headerBlock1 = EncodeHeaders("GET", "/path1", "example.com");
        var headersFrameData1 = BuildHeadersFrame(streamId: 1, headerBlock1, endStream: true, endHeaders: true);

        var buffer1 = TransportBuffer.Rent(headersFrameData1.Length);
        headersFrameData1.CopyTo(buffer1.FullMemory.Span);
        buffer1.Length = headersFrameData1.Length;

        sm.DecodeClientData(new TransportData(buffer1));

        // Send HEADERS on stream 3
        var headerBlock3 = EncodeHeaders("GET", "/path3", "example.com");
        var headersFrameData3 = BuildHeadersFrame(streamId: 3, headerBlock3, endStream: true, endHeaders: true);

        var buffer3 = TransportBuffer.Rent(headersFrameData3.Length);
        headersFrameData3.CopyTo(buffer3.FullMemory.Span);
        buffer3.Length = headersFrameData3.Length;

        sm.DecodeClientData(new TransportData(buffer3));

        // Verify both requests were emitted
        Assert.Equal(2, ops.Requests.Count);

        // Verify stream IDs are correctly stored in request features
        var context1 = ops.Requests[0];
        var streamIdFeature1 = context1.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature1);
        Assert.Equal(1, streamIdFeature1.StreamId);
        Assert.Equal("/path1", context1.Get<IHttpRequestFeature>().Path);

        var context3 = ops.Requests[1];
        var streamIdFeature3 = context3.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature3);
        Assert.Equal(3, streamIdFeature3.StreamId);
        Assert.Equal("/path3", context3.Get<IHttpRequestFeature>().Path);

        // Now respond to stream 3 first
        ops.Outbound.Clear();
        var responseContext3 = CreateResponseContext(streamId: 3);
        sm.OnResponse(responseContext3);

        // Verify HEADERS frame for stream 3 was emitted
        Assert.NotEmpty(ops.Outbound);
        var headersEmitted = false;
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData td)
            {
                var frameData = td.Buffer.Span;
                if (frameData.Length >= 9 && frameData[3] == (byte)FrameType.Headers)
                {
                    // Extract stream ID from frame
                    var sid = (frameData[5] << 24) | (frameData[6] << 16)
                                                   | (frameData[7] << 8) | frameData[8];
                    if (sid == 3)
                    {
                        headersEmitted = true;
                        break;
                    }
                }
            }
        }

        Assert.True(headersEmitted, "Expected HEADERS frame for stream 3 to be emitted");

        // Now respond to stream 1
        ops.Outbound.Clear();
        var responseContext1 = CreateResponseContext(streamId: 1);
        sm.OnResponse(responseContext1);

        // Verify HEADERS frame for stream 1 was emitted
        Assert.NotEmpty(ops.Outbound);
        var headers1Emitted = false;
        foreach (var item in ops.Outbound)
        {
            if (item is TransportData td)
            {
                var frameData = td.Buffer.Span;
                if (frameData.Length >= 9 && frameData[3] == (byte)FrameType.Headers)
                {
                    // Extract stream ID from frame
                    var sid = (frameData[5] << 24) | (frameData[6] << 16)
                                                   | (frameData[7] << 8) | frameData[8];
                    if (sid == 1)
                    {
                        headers1Emitted = true;
                        break;
                    }
                }
            }
        }

        Assert.True(headers1Emitted, "Expected HEADERS frame for stream 1 to be emitted");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5")]
    public void Stream_IDs_should_preserve_request_response_correlation_across_interleaved_processing()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send three requests on streams 1, 3, 5
        for (var streamId = 1; streamId <= 5; streamId += 2)
        {
            var headerBlock = EncodeHeaders("GET", $"/path{streamId}", "example.com");
            var headersFrameData = BuildHeadersFrame(streamId, headerBlock, endStream: true, endHeaders: true);

            var buffer = TransportBuffer.Rent(headersFrameData.Length);
            headersFrameData.CopyTo(buffer.FullMemory.Span);
            buffer.Length = headersFrameData.Length;

            sm.DecodeClientData(new TransportData(buffer));
        }

        Assert.Equal(3, ops.Requests.Count);

        // Verify each request has correct stream ID and path
        for (var i = 0; i < ops.Requests.Count; i++)
        {
            var context = ops.Requests[i];
            var expectedStreamId = 1 + (i * 2);
            var expectedPath = $"/path{expectedStreamId}";

            var streamIdFeature = context.Get<IHttpStreamIdFeature>();
            Assert.NotNull(streamIdFeature);
            Assert.Equal(expectedStreamId, streamIdFeature.StreamId);
            Assert.Equal(expectedPath, context.Get<IHttpRequestFeature>().Path);
        }

        // Respond in reverse order (5, 3, 1) and verify correct stream IDs are used
        var responseOrder = new[] { 2, 1, 0 };
        ops.Outbound.Clear();

        foreach (var idx in responseOrder)
        {
            var reqContext = ops.Requests[idx];
            var reqStreamIdFeature = reqContext.Get<IHttpStreamIdFeature>();
            var reqStreamId = reqStreamIdFeature?.StreamId ?? 0;

            ops.Outbound.Clear();
            var context = CreateResponseContext(streamId: reqStreamId);
            sm.OnResponse(context);

            // Find HEADERS frame in outbound
            var foundCorrectStreamId = false;
            foreach (var item in ops.Outbound)
            {
                if (item is TransportData td)
                {
                    var frameData = td.Buffer.Span;
                    if (frameData.Length >= 9 && frameData[3] == (byte)FrameType.Headers)
                    {
                        var emittedStreamId = (frameData[5] << 24) | (frameData[6] << 16)
                                                                   | (frameData[7] << 8) | frameData[8];
                        if (emittedStreamId == reqStreamId)
                        {
                            foundCorrectStreamId = true;
                            break;
                        }
                    }
                }
            }

            Assert.True(foundCorrectStreamId,
                $"Expected HEADERS frame for stream {reqStreamId} to be emitted");
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5")]
    public void Concurrent_streams_should_maintain_independent_state()
    {
        var ops = new FakeServerOps();
        var sm = new Http2ServerStateMachine(new TurboServerOptions(), ops);

        // Send multiple requests without waiting for responses
        var headerBlock1 = EncodeHeaders("GET", "/");
        var headerBlock2 = EncodeHeaders("POST", "/submit");
        var headerBlock3 = EncodeHeaders("GET", "/status");

        var headersData1 = BuildHeadersFrame(1, headerBlock1, endStream: true, endHeaders: true);
        var headersData2 = BuildHeadersFrame(3, headerBlock2, endStream: true, endHeaders: true);
        var headersData3 = BuildHeadersFrame(5, headerBlock3, endStream: true, endHeaders: true);

        var buf1 = TransportBuffer.Rent(headersData1.Length);
        headersData1.CopyTo(buf1.FullMemory.Span);
        buf1.Length = headersData1.Length;
        sm.DecodeClientData(new TransportData(buf1));

        var buf2 = TransportBuffer.Rent(headersData2.Length);
        headersData2.CopyTo(buf2.FullMemory.Span);
        buf2.Length = headersData2.Length;
        sm.DecodeClientData(new TransportData(buf2));

        var buf3 = TransportBuffer.Rent(headersData3.Length);
        headersData3.CopyTo(buf3.FullMemory.Span);
        buf3.Length = headersData3.Length;
        sm.DecodeClientData(new TransportData(buf3));

        // All three requests should have been emitted
        Assert.Equal(3, ops.Requests.Count);

        // Verify each has correct stream ID
        var streamIds = ops.Requests
            .Select(ctx =>
            {
                var feature = ctx.Get<IHttpStreamIdFeature>();
                return (int)(feature?.StreamId ?? 0);
            })
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(new[] { 1, 3, 5 }, streamIds);

        // Verify paths match stream order
        Assert.Equal("/", ops.Requests[0].Get<IHttpRequestFeature>().Path);
        Assert.Equal("/submit", ops.Requests[1].Get<IHttpRequestFeature>().Path);
        Assert.Equal("/status", ops.Requests[2].Get<IHttpRequestFeature>().Path);
    }
}



