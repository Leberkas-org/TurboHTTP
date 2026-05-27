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
/// Unit tests for HTTP/3 Http3ServerSessionManager stream lifecycle.
/// Tests request emission, concurrent streams, response handling, and cleanup.
/// </summary>
public sealed class Http3StreamLifecycleSpec
{
    private static IFeatureCollection CreateResponseContext(long streamId = 999)
    {
        var features = new TurboFeatureCollection();
        features.Set<IHttpRequestFeature>(new TurboHttpRequestFeature());
        features.Set<IHttpResponseFeature>(new TurboHttpResponseFeature { StatusCode = 200 });
        features.Set<IHttpStreamIdFeature>(new TurboStreamIdFeature(streamId));
        var bodyFeature = new TurboHttpResponseBodyFeature();
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        features.Set<IHttpResponseBodyFeature>(bodyFeature);
        return features;
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

    private static Http3ServerSessionManager CreateSM(FakeServerOps ops)
    {
        var enc = new Http3ServerEncoderOptions { QpackMaxTableCapacity = 0 };
        var dec = new Http3ServerDecoderOptions { MaxConcurrentStreams = 100 };
        return new Http3ServerSessionManager(enc, dec, ops);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void Request_should_be_emitted_after_StreamReadCompleted()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 4;
        SendRequest(sm, streamId);

        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(streamId, streamIdFeature.StreamId);
        var requestFeature = context.Get<IHttpRequestFeature>() as TurboHttpRequestFeature;
        Assert.NotNull(requestFeature);
        Assert.Equal("GET", requestFeature.Method);
        Assert.Equal("https", requestFeature.Scheme);
        Assert.Equal("localhost", requestFeature.ExtractedHost);
        Assert.Equal("/", requestFeature.Path);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.1")]
    public void Multiple_concurrent_streams_should_all_emit_requests()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId1 = 0;
        const long streamId2 = 4;

        SendRequest(sm, streamId1, "GET", "/path1");
        SendRequest(sm, streamId2, "POST", "/path2");

        Assert.Equal(2, ops.Requests.Count);

        var ctx1 = ops.Requests[0];
        var ctx2 = ops.Requests[1];

        var streamIdFeature1 = ctx1.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature1);
        var streamIdFeature2 = ctx2.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature2);
        Assert.Equal(streamId1, streamIdFeature1.StreamId);
        Assert.Equal(streamId2, streamIdFeature2.StreamId);

        var requestFeature1 = ctx1.Get<IHttpRequestFeature>() as TurboHttpRequestFeature;
        var requestFeature2 = ctx2.Get<IHttpRequestFeature>() as TurboHttpRequestFeature;
        Assert.NotNull(requestFeature1);
        Assert.NotNull(requestFeature2);
        Assert.Equal("GET", requestFeature1.Method);
        Assert.Equal("POST", requestFeature2.Method);
        Assert.Equal("/path1", requestFeature1.Path);
        Assert.Equal("/path2", requestFeature2.Path);
    }

    [Fact(Timeout = 5000)]
    public void OnResponse_for_unknown_stream_should_not_crash()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        // Should not throw when responding on unknown stream
        var context = CreateResponseContext();
        sm.OnResponse(context);

        // No requests should be emitted (stream 999 never existed)
        Assert.Empty(ops.Requests);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-4.1")]
    public void OnResponse_no_body_should_emit_CompleteWrites()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 8;
        SendRequest(sm, streamId);

        Assert.Single(ops.Requests);
        var context = ops.Requests[0];

        ops.Outbound.Clear();

        context.Get<IHttpResponseFeature>().StatusCode = 200;
        sm.OnResponse(context);

        var completeWrites = ops.Outbound.OfType<CompleteWrites>().ToList();
        Assert.Single(completeWrites);
        Assert.Equal(streamId, completeWrites[0].StreamId.Value);
    }

    [Fact(Timeout = 5000)]
    public void Cleanup_should_be_idempotent()
    {
        var ops = new FakeServerOps();
        var sm = CreateSM(ops);

        const long streamId = 12;
        SendRequest(sm, streamId);

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
        var ops = new FakeServerOps();
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
        var context = ops.Requests[0];

        var streamIdFeature = context.Get<IHttpStreamIdFeature>();
        Assert.NotNull(streamIdFeature);
        Assert.Equal(streamId, streamIdFeature.StreamId);
    }
}
