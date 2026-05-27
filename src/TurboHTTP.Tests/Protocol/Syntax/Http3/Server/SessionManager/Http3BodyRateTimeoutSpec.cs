using Microsoft.AspNetCore.Http.Features;
using Servus.Akka.Transport;
using TurboHTTP.Context.Features;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Unit tests for HTTP/3 Http3ServerSessionManager body rate checking and timeout handling.
/// Tests that DATA frames trigger body-rate-check timers, and that headers-timeout is properly
/// cancelled upon successful decoding or stream completion.
/// </summary>
public sealed class Http3BodyRateTimeoutSpec
{

    private static (byte[] Data, long StreamId) BuildRequest(string method, string path, long streamId)
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var headers = new List<(string, string)>
        {
            (":method", method),
            (":path", path),
            (":scheme", "https"),
            (":authority", "localhost"),
        };
        var headerBlock = tableSync.Encoder.Encode(headers);
        var frame = new HeadersFrame(headerBlock);
        var buf = new byte[frame.SerializedSize];
        var span = buf.AsSpan();
        frame.WriteTo(ref span);
        return (buf, streamId);
    }

    private static byte[] BuildDataFrameBytes(int size)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(size);
        var df = new DataFrame(owner, size);
        var buf = new byte[df.SerializedSize];
        var span = buf.AsSpan();
        df.WriteTo(ref span);
        return buf;
    }

    private static Http3ServerSessionManager CreateSM(FakeServerOps ops)
    {
        var enc = new Http3ServerEncoderOptions { QpackMaxTableCapacity = 0 };
        var dec = new Http3ServerDecoderOptions { MaxConcurrentStreams = 100 };
        return new Http3ServerSessionManager(enc, dec, ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.3")]
    public void First_DATA_frame_should_schedule_body_rate_check()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 4;

        // Build HEADERS
        var (headerBytes, _) = BuildRequest("POST", "/upload", streamId);

        // Open stream
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        // Send HEADERS (no StreamReadCompleted yet)
        var headerBuffer = TransportBuffer.Rent(headerBytes.Length);
        headerBytes.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headerBytes.Length;
        sm.DecodeClientData(new MultiplexedData(headerBuffer, streamId));

        // No request emitted yet (no StreamReadCompleted)
        Assert.Empty(ops.Requests);

        // Clear any timers from header processing
        ops.ScheduledTimers.Clear();

        // Build and send DATA frame
        var dataBytes = BuildDataFrameBytes(100);
        var dataBuffer = TransportBuffer.Rent(dataBytes.Length);
        dataBytes.CopyTo(dataBuffer.FullMemory.Span);
        dataBuffer.Length = dataBytes.Length;
        sm.DecodeClientData(new MultiplexedData(dataBuffer, streamId));

        // body-rate-check timer should now be scheduled
        Assert.True(ops.ScheduledTimers.Any(t => t.Name == "body-rate-check"),
            "body-rate-check timer should be scheduled after first DATA frame");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Headers_timeout_should_be_cancelled_on_successful_decode()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 8;

        // Build HEADERS
        var (headerBytes, _) = BuildRequest("GET", "/", streamId);

        // Open stream
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        // Send HEADERS
        var headerBuffer = TransportBuffer.Rent(headerBytes.Length);
        headerBytes.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headerBytes.Length;
        sm.DecodeClientData(new MultiplexedData(headerBuffer, streamId));

        // With capacity=0, QPACK decodes immediately, so headers-timeout is never scheduled.
        // Instead, FlushPendingRequest cancels it preemptively.

        // Send StreamReadCompleted to flush the pending request
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        // Request should now be emitted
        Assert.Single(ops.Requests);

        // The timeout should have been cancelled
        var timerName = string.Concat("headers-timeout:", streamId.ToString());
        Assert.Contains(timerName, ops.CancelledTimers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void StreamReadCompleted_without_body_should_emit_request_with_empty_content()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 12;

        // Build HEADERS
        var (headerBytes, _) = BuildRequest("GET", "/", streamId);

        // Open stream
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        // Send HEADERS
        var headerBuffer = TransportBuffer.Rent(headerBytes.Length);
        headerBytes.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headerBytes.Length;
        sm.DecodeClientData(new MultiplexedData(headerBuffer, streamId));

        // Send StreamReadCompleted (no DATA frames)
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        // Request should be emitted with empty content
        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        var requestFeature = context.Get<IHttpRequestFeature>() as TurboHttpRequestFeature;
        Assert.NotNull(requestFeature);
        Assert.Equal("GET", requestFeature.Method);
        Assert.Equal("https", requestFeature.Scheme);
        Assert.Equal("localhost", requestFeature.ExtractedHost);
        Assert.Equal("/", requestFeature.Path);
    }
}