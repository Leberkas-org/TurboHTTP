using Akka.Actor;
using Akka.Event;
using Servus.Akka.Transport;
using TurboHTTP.Protocol;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Options;
using TurboHTTP.Protocol.Syntax.Http3.Qpack;
using TurboHTTP.Protocol.Syntax.Http3.Server;
using TurboHTTP.Streams;
using TurboHTTP.Streams.Stages.Server;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Server.SessionManager;

/// <summary>
/// Unit tests for HTTP/3 Http3ServerSessionManager stream lifecycle.
/// Tests request emission, concurrent streams, response handling, and cleanup.
/// </summary>
public sealed class Http3StreamLifecycleSpec
{
    private sealed class TrackingServerOps : IServerStageOperations
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<ITransportOutbound> Outbound { get; } = [];
        public Dictionary<string, (string Name, TimeSpan Delay)> ScheduledTimers { get; } = [];
        public List<string> CancelledTimers { get; } = [];
        public ILoggingAdapter Log { get; } = NoLogger.Instance;
        public IActorRef StageActor { get; set; } = ActorRefs.Nobody;

        public void OnRequest(HttpRequestMessage request) => Requests.Add(request);

        public void OnOutbound(ITransportOutbound item) => Outbound.Add(item);

        public void OnScheduleTimer(string name, TimeSpan delay) => ScheduledTimers[name] = (name, delay);

        public void OnCancelTimer(string name)
        {
            ScheduledTimers.Remove(name);
            CancelledTimers.Add(name);
        }
    }

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

    private static void SendRequest(Http3ServerSessionManager sm, long streamId, string method = "GET",
        string path = "/")
    {
        var (data, _) = BuildRequest(method, path, streamId);
        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));
        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));
        sm.DecodeClientData(new StreamReadCompleted(StreamTarget.FromId(streamId)));
    }

    private static Http3ServerSessionManager CreateSM(TrackingServerOps ops)
    {
        var enc = new Http3ServerEncoderOptions { QpackMaxTableCapacity = 0 };
        var dec = new Http3ServerDecoderOptions { MaxConcurrentStreams = 100 };
        return new Http3ServerSessionManager(enc, dec, ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Request_should_be_emitted_after_StreamReadCompleted()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        const long streamId = 4;
        SendRequest(sm, streamId, "GET", "/");

        Assert.Single(ops.Requests);
        var request = ops.Requests[0];

        Assert.True(request.Options.TryGetValue(StreamIdKey.Http3, out var storedStreamId));
        Assert.Equal(streamId, storedStreamId);
        Assert.Equal("GET", request.Method.Method);
        Assert.Equal("https://localhost/", request.RequestUri?.ToString());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void Multiple_concurrent_streams_should_all_emit_requests()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        const long streamId1 = 0;
        const long streamId2 = 4;

        SendRequest(sm, streamId1, "GET", "/path1");
        SendRequest(sm, streamId2, "POST", "/path2");

        Assert.Equal(2, ops.Requests.Count);

        var req1 = ops.Requests[0];
        var req2 = ops.Requests[1];

        Assert.True(req1.Options.TryGetValue(StreamIdKey.Http3, out var id1));
        Assert.True(req2.Options.TryGetValue(StreamIdKey.Http3, out var id2));
        Assert.Equal(streamId1, id1);
        Assert.Equal(streamId2, id2);

        Assert.Equal("GET", req1.Method.Method);
        Assert.Equal("POST", req2.Method.Method);
        Assert.Equal("/path1", req1.RequestUri?.AbsolutePath);
        Assert.Equal("/path2", req2.RequestUri?.AbsolutePath);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        var request = new HttpRequestMessage();
        request.Options.Set(StreamIdKey.Http3, 999L);
        response.RequestMessage = request;
        response.Content = new ByteArrayContent([]);
        response.Content.Headers.ContentLength = 0;

        // Should not throw
        sm.OnResponse(response);

        // No requests should be emitted (stream 999 never existed)
        Assert.Empty(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnResponse_no_body_should_emit_CompleteWrites()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        const long streamId = 8;
        SendRequest(sm, streamId, "GET", "/");

        Assert.Single(ops.Requests);
        var request = ops.Requests[0];

        ops.Outbound.Clear();

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            RequestMessage = request
        };
        response.Content = new ByteArrayContent([]);
        response.Content.Headers.ContentLength = 0;

        sm.OnResponse(response);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.Single(completeWrites);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        const long streamId = 12;
        SendRequest(sm, streamId, "GET", "/");

        // First cleanup
        sm.Cleanup();

        // Second cleanup should not crash
        sm.Cleanup();

        Assert.Single(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void FlushAllPendingRequests_should_emit_pending()
    {
        var ops = new TrackingServerOps();
        var sm = CreateSM(ops);

        const long streamId = 16;
        var (data, _) = BuildRequest("GET", "/", streamId);

        sm.DecodeClientData(new ServerStreamAccepted(StreamTarget.FromId(streamId),
            StreamDirection.Bidirectional));

        var buffer = TransportBuffer.Rent(data.Length);
        data.CopyTo(buffer.FullMemory.Span);
        buffer.Length = data.Length;
        sm.DecodeClientData(new MultiplexedData(buffer, streamId));

        // Request not yet emitted (no StreamReadCompleted)
        Assert.Empty(ops.Requests);

        // Flush all pending
        sm.FlushAllPendingRequests();

        // Now request should be emitted
        Assert.Single(ops.Requests);
        var request = ops.Requests[0];

        Assert.True(request.Options.TryGetValue(StreamIdKey.Http3, out var storedStreamId));
        Assert.Equal(streamId, storedStreamId);
    }
}
