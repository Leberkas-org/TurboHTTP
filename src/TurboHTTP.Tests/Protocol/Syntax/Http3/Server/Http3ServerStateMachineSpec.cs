using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server;

/// <summary>
/// Unit tests for HTTP/3 Http3ServerStateMachine.
/// Tests QUIC stream multiplexing, request assembly from HEADERS/DATA frames,
/// response encoding, and critical stream handling.
/// </summary>
public sealed class Http3ServerStateMachineSpec
{
    private sealed class FakeServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> EmittedRequests { get; } = [];
        public List<ITransportOutbound> EmittedOutbound { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;
        public Dictionary<string, (string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];

        public void OnRequest(HttpRequestMessage request)
        {
            EmittedRequests.Add(request);
        }

        public void OnOutbound(ITransportOutbound item)
        {
            EmittedOutbound.Add(item);
        }

        public void OnScheduleTimer(string name, TimeSpan delay)
        {
            ScheduledTimers[name] = (name, delay);
        }

        public void OnCancelTimer(string name)
        {
            ScheduledTimers.Remove(name);
        }
    }

    private static byte[] BuildHeadersFrameData(ReadOnlyMemory<byte> headerBlock)
    {
        var headersFrame = new HeadersFrame(headerBlock);
        var buffer = new byte[headersFrame.SerializedSize];
        var span = buffer.AsSpan();
        headersFrame.WriteTo(ref span);
        return buffer;
    }

    private static byte[] BuildDataFrameData(ReadOnlyMemory<byte> data)
    {
        using var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(owner.Memory);
        var dataFrame = new DataFrame(owner, data.Length);
        var buffer = new byte[dataFrame.SerializedSize];
        var span = buffer.AsSpan();
        dataFrame.WriteTo(ref span);
        return buffer;
    }

    private static ReadOnlyMemory<byte> EncodeHeaders(
        string method,
        string path,
        string scheme = "https",
        string authority = "localhost")
    {
        var tableSync = new QpackTableSync(0, 0, 0, 0);
        var encoder = tableSync.Encoder;
        var headers = new List<(string Name, string Value)>
        {
            (":method", method),
            (":path", path),
            (":scheme", scheme),
            (":authority", authority),
        };

        return encoder.Encode(headers);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void PreStart_should_open_control_and_qpack_streams()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        // Should emit 4 items: 3x OpenStream + 1x MultiplexedData(settings)
        Assert.Equal(4, ops.EmittedOutbound.Count);

        // Verify stream opens
        Assert.IsType<OpenStream>(ops.EmittedOutbound[0]);
        Assert.IsType<OpenStream>(ops.EmittedOutbound[1]);
        Assert.IsType<OpenStream>(ops.EmittedOutbound[2]);
        Assert.IsType<MultiplexedData>(ops.EmittedOutbound[3]);

        var controlOpen = (OpenStream)ops.EmittedOutbound[0];
        var encoderOpen = (OpenStream)ops.EmittedOutbound[1];
        var decoderOpen = (OpenStream)ops.EmittedOutbound[2];

        Assert.Equal(CriticalStreamId.ControlId, controlOpen.StreamId.Value);
        Assert.Equal(CriticalStreamId.QpackEncoderId, encoderOpen.StreamId.Value);
        Assert.Equal(CriticalStreamId.QpackDecoderId, decoderOpen.StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void PreStart_should_emit_settings_on_control_stream()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        sm.PreStart();

        var settingsData = ops.EmittedOutbound[3];
        Assert.IsType<MultiplexedData>(settingsData);

        var multiplexed = (MultiplexedData)settingsData;
        Assert.Equal(CriticalStreamId.ControlId, multiplexed.StreamId.Value);

        // Verify buffer contains valid data (at least stream type + settings frame)
        Assert.NotEqual(0, multiplexed.Buffer.Span.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void DecodeClientData_with_headers_should_produce_request_with_stream_id()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        const long streamId = 4; // Client-initiated bidirectional stream

        var headerBlock = EncodeHeaders("GET", "/", "https", "example.com");
        var headersFrameData = BuildHeadersFrameData(headerBlock);

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;

        // Signal stream opening
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        // Send HEADERS frame
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));

        // Signal end of stream
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];

        // Verify stream ID was stored in request options
        Assert.True(request.Options.TryGetValue(StreamIdKey.Http3, out var storedStreamId));
        Assert.Equal(streamId, storedStreamId);

        // Verify request properties
        Assert.Equal("GET", request.Method.Method);
        Assert.Equal("https://example.com/", request.RequestUri?.ToString());
        Assert.Equal(3, request.Version.Major);
        Assert.Equal(0, request.Version.Minor);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public async Task DecodeClientData_with_headers_and_data_should_accumulate_body()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        const long streamId = 8; // Different stream ID
        const string bodyContent = "Hello, World!";

        var headerBlock = EncodeHeaders("POST", "/api/data", "https", "example.com");
        var headersFrameData = BuildHeadersFrameData(headerBlock);

        var bodyData = System.Text.Encoding.UTF8.GetBytes(bodyContent);
        var dataFrameData = BuildDataFrameData(bodyData);

        // Signal stream opening
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        // Send HEADERS frame
        var headerBuffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headersFrameData.Length;
        sm.DecodeClientData(new MultiplexedData(headerBuffer, streamId));

        // Send DATA frame
        var dataBuffer = TransportBuffer.Rent(dataFrameData.Length);
        dataFrameData.CopyTo(dataBuffer.FullMemory.Span);
        dataBuffer.Length = dataFrameData.Length;
        sm.DecodeClientData(new MultiplexedData(dataBuffer, streamId));

        // Signal end of stream
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];

        // Verify stream ID
        Assert.True(request.Options.TryGetValue(StreamIdKey.Http3, out var storedStreamId));
        Assert.Equal(streamId, storedStreamId);

        // Verify request properties
        Assert.Equal("POST", request.Method.Method);
        Assert.Equal("https://example.com/api/data", request.RequestUri?.ToString());

        // Verify body was accumulated
        var content = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bodyContent, content);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void OnResponse_no_body_should_emit_HEADERS_and_CompleteWrites()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        const long streamId = 12;

        // First, receive a request
        var headerBlock = EncodeHeaders("GET", "/", "https", "example.com");
        var headersFrameData = BuildHeadersFrameData(headerBlock);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        var headerBuffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headersFrameData.Length;
        sm.DecodeClientData(new MultiplexedData(headerBuffer, streamId));

        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];

        // Verify StreamIdKey is set
        Assert.True(request.Options.TryGetValue(StreamIdKey.Http3, out var retrievedId));
        Assert.Equal(streamId, retrievedId);

        // Clear outbound to focus on response
        ops.EmittedOutbound.Clear();

        // Send response without body
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request
        };

        sm.OnResponse(response);

        // Should emit HEADERS frame + CompleteWrites immediately (no body)
        var frameItems = ops.EmittedOutbound.OfType<MultiplexedData>().ToList();
        var completeWrites = ops.EmittedOutbound.OfType<CompleteWrites>().ToList();

        Assert.NotEmpty(frameItems);
        Assert.Equal(2, ops.EmittedOutbound.Count);
        Assert.Single(completeWrites);
        Assert.Equal(streamId, frameItems[0].StreamId.Value);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2")]
    public void OnResponse_with_body_should_schedule_drain_timer()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        const long streamId = 12;

        // First, receive a request
        var headerBlock = EncodeHeaders("GET", "/", "https", "example.com");
        var headersFrameData = BuildHeadersFrameData(headerBlock);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        var headerBuffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(headerBuffer.FullMemory.Span);
        headerBuffer.Length = headersFrameData.Length;
        sm.DecodeClientData(new MultiplexedData(headerBuffer, streamId));

        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));

        Assert.Single(ops.EmittedRequests);
        var request = ops.EmittedRequests[0];

        // Clear outbound to focus on response
        ops.EmittedOutbound.Clear();

        // Send response with body
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent("test"u8.ToArray())
        };

        sm.OnResponse(response);

        // Should emit HEADERS frame immediately
        var frameItems = ops.EmittedOutbound.OfType<MultiplexedData>().ToList();
        Assert.NotEmpty(frameItems);
        Assert.Equal(streamId, frameItems[0].StreamId.Value);

        // Should schedule drain-body timer
        Assert.True(ops.ScheduledTimers.ContainsKey($"drain-body:{streamId}"), "Should schedule drain-body timer");
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-5.2")]
    public void DecodeClientData_with_multiple_streams_should_multiplex()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        // Stream 1
        const long stream1 = 0;
        var headers1 = EncodeHeaders("GET", "/path1", "https", "host1.com");
        var headersData1 = BuildHeadersFrameData(headers1);

        // Stream 2
        const long stream2 = 4;
        var headers2 = EncodeHeaders("POST", "/path2", "https", "host2.com");
        var headersData2 = BuildHeadersFrameData(headers2);

        // Open stream 1 and send request
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(stream1), StreamDirection.Bidirectional));
        var buf1 = TransportBuffer.Rent(headersData1.Length);
        headersData1.CopyTo(buf1.FullMemory.Span);
        buf1.Length = headersData1.Length;
        sm.DecodeClientData(new MultiplexedData(buf1, stream1));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(stream1)));

        // Open stream 2 and send request
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(stream2), StreamDirection.Bidirectional));
        var buf2 = TransportBuffer.Rent(headersData2.Length);
        headersData2.CopyTo(buf2.FullMemory.Span);
        buf2.Length = headersData2.Length;
        sm.DecodeClientData(new MultiplexedData(buf2, stream2));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(stream2)));

        // Should have two requests
        Assert.Equal(2, ops.EmittedRequests.Count);

        var req1 = ops.EmittedRequests[0];
        var req2 = ops.EmittedRequests[1];

        // Verify stream IDs
        Assert.True(req1.Options.TryGetValue(StreamIdKey.Http3, out var id1));
        Assert.True(req2.Options.TryGetValue(StreamIdKey.Http3, out var id2));
        Assert.Equal(stream1, id1);
        Assert.Equal(stream2, id2);

        // Verify different requests
        Assert.Equal("GET", req1.Method.Method);
        Assert.Equal("POST", req2.Method.Method);
        Assert.Equal("/path1", req1.RequestUri?.AbsolutePath);
        Assert.Equal("/path2", req2.RequestUri?.AbsolutePath);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void OnDownstreamFinished_should_flush_pending_requests()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        const long streamId = 4;

        var headerBlock = EncodeHeaders("GET", "/", "https", "example.com");
        var headersFrameData = BuildHeadersFrameData(headerBlock);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));

        // Request not yet flushed (stream still open)
        Assert.Empty(ops.EmittedRequests);

        // Simulate downstream finishing (connection closing)
        sm.OnDownstreamFinished();

        // Request should be flushed on downstream finish
        Assert.Single(ops.EmittedRequests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Cleanup_should_dispose_stream_decoders()
    {
        var ops = new FakeServerOps();
        var sm = new Http3ServerStateMachine(new TurboServerOptions(), ops);

        const long streamId = 4;

        var headerBlock = EncodeHeaders("GET", "/", "https", "example.com");
        var headersFrameData = BuildHeadersFrameData(headerBlock);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId), StreamDirection.Bidirectional));

        var buffer = TransportBuffer.Rent(headersFrameData.Length);
        headersFrameData.CopyTo(buffer.FullMemory.Span);
        buffer.Length = headersFrameData.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));

        // Should not throw during cleanup
        sm.Cleanup();
    }
}