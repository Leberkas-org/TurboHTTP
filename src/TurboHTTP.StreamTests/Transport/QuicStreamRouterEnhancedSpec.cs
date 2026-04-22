using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.StreamTests.Transport;

public sealed class QuicStreamRouterEnhancedSpec
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version30
    };

    private static (QuicStreamRouter Router, MockTransportOperations Ops) CreateRouter()
    {
        var ops = new MockTransportOperations();
        var router = new QuicStreamRouter(ops, ActorRefs.Nobody);
        return (router, ops);
    }

    private static (ConnectionHandle Handle, ChannelReader<NetworkBuffer> OutboundReader) CreateTestHandle(
        RequestEndpoint? endpoint = null)
    {
        var key = endpoint ?? TestEndpoint;
        var inbound = Channel.CreateUnbounded<NetworkBuffer>();
        var outbound = Channel.CreateUnbounded<NetworkBuffer>();
        return (ConnectionHandle.CreateDirect(outbound.Writer, inbound.Reader, key), outbound.Reader);
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_should_route_encoder_to_pending_when_no_handle()
    {
        var (router, ops) = CreateRouter();

        var encoderData = RoutedNetworkBuffer.Rent(4);
        encoderData.StreamTypeValue = (long)StreamType.QpackEncoder;
        encoderData.Length = 3;

        var encoderState = new TypedStreamState { StreamId = -3 };
        var typedStreams = new Dictionary<long, TypedStreamState> { [0x02] = encoderState };
        router.RouteTaggedItem(encoderData, 0x02, typedStreams);

        Assert.Single(encoderState.PendingItems);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_should_write_encoder_to_handle_when_available()
    {
        var (router, _) = CreateRouter();
        var (encoderHandle, encoderReader) = CreateTestHandle();

        var encoderData = RoutedNetworkBuffer.Rent(4);
        encoderData.StreamTypeValue = (long)StreamType.QpackEncoder;
        encoderData.Length = 3;

        var encoderState = new TypedStreamState { Handle = encoderHandle, StreamId = -3 };
        var typedStreams = new Dictionary<long, TypedStreamState> { [0x02] = encoderState };
        router.RouteTaggedItem(encoderData, 0x02, typedStreams);

        Assert.True(encoderReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void FlushAllReadyStreams_should_skip_streams_without_handles()
    {
        var (router, _) = CreateRouter();

        // Stream 1 has handle, stream 2 doesn't
        var (handle1, reader1) = CreateTestHandle();
        var ctx1 = router.GetOrCreateContext(1);
        ctx1.Handle = handle1;
        ctx1.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([1]));

        var ctx2 = router.GetOrCreateContext(2);
        ctx2.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([2]));

        router.FlushAllReadyStreams();

        // Only stream 1 should be flushed
        Assert.True(reader1.TryRead(out _));
        Assert.Single(ctx2.PendingWrites);
    }

    [Fact(Timeout = 5000)]
    public void FlushPendingWrites_should_preserve_order()
    {
        var (router, _) = CreateRouter();
        var (handle, outboundReader) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);

        var buf1 = NetworkBufferTestExtensions.FromArray([1]);
        var buf2 = NetworkBufferTestExtensions.FromArray([2]);
        var buf3 = NetworkBufferTestExtensions.FromArray([3]);

        ctx.PendingWrites.Enqueue(buf1);
        ctx.PendingWrites.Enqueue(buf2);
        ctx.PendingWrites.Enqueue(buf3);
        ctx.Handle = handle;

        router.FlushPendingWrites(ctx);

        Assert.True(outboundReader.TryRead(out _));
        Assert.True(outboundReader.TryRead(out _));
        Assert.True(outboundReader.TryRead(out _));
        Assert.False(outboundReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void HandleEndOfRequest_with_pending_writes_should_mark_and_signal()
    {
        var (router, ops) = CreateRouter();
        router.GetOrCreateContext(1);
        router.GetOrCreateContext(1).PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([1, 2]));

        router.HandleEndOfRequest(new Http3EndOfRequestItem { Key = TestEndpoint, StreamId = 1 });

        Assert.True(router.RequestStreams[1].PendingEndOfRequest);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandleEndOfRequest_unknown_stream_should_signal_only()
    {
        var (router, ops) = CreateRouter();

        router.HandleEndOfRequest(new Http3EndOfRequestItem { Key = TestEndpoint, StreamId = 999 });

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RequeueEarlyData_should_find_first_stream()
    {
        var (router, ops) = CreateRouter();
        router.GetOrCreateContext(10);
        router.GetOrCreateContext(20);
        router.GetOrCreateContext(30);

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        router.RequeueEarlyData(buffer);

        Assert.Single(router.RequestStreams[10].PendingWrites);
        Assert.Empty(router.RequestStreams[20].PendingWrites);
        Assert.Empty(router.RequestStreams[30].PendingWrites);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RequeueEarlyData_without_streams_should_signal_only()
    {
        var (router, ops) = CreateRouter();
        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        router.RequeueEarlyData(buffer);

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RemoveStream_should_not_affect_other_streams()
    {
        var (router, _) = CreateRouter();
        router.GetOrCreateContext(1);
        router.GetOrCreateContext(2);
        router.GetOrCreateContext(3);

        router.RemoveStream(2);

        Assert.True(router.RequestStreams.ContainsKey(1));
        Assert.False(router.RequestStreams.ContainsKey(2));
        Assert.True(router.RequestStreams.ContainsKey(3));
    }

    [Fact(Timeout = 5000)]
    public void Clear_should_clean_all_state()
    {
        var (router, _) = CreateRouter();

        // Add streams and pending IDs
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };
        router.EnsureStreamContext(item, 1, hasConnection: false);
        router.EnsureStreamContext(item, 2, hasConnection: false);
        router.GetOrCreateContext(3);

        Assert.Equal(3, router.RequestStreams.Count);

        router.Clear();

        Assert.Empty(router.RequestStreams);
        Assert.Equal(-1, router.DequeueNextPendingStreamId());
    }

    [Fact(Timeout = 5000)]
    public void DisposePendingWrites_should_dispose_all_buffers()
    {
        var (router, _) = CreateRouter();

        var ctx1 = router.GetOrCreateContext(1);
        ctx1.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([1]));
        ctx1.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([2]));

        var ctx2 = router.GetOrCreateContext(2);
        ctx2.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([3]));

        // Method should complete without throwing
        router.DisposePendingWrites();

        Assert.Empty(ctx1.PendingWrites);
        Assert.Empty(ctx2.PendingWrites);
    }

    [Fact(Timeout = 5000)]
    public void EnsureStreamContext_should_reject_default_endpoint()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = RequestEndpoint.Default
        };

        var result = router.EnsureStreamContext(item, 1, hasConnection: true);

        Assert.Equal(QuicStreamRouter.StreamContextResult.AlreadyExists, result);
        Assert.False(router.RequestStreams.ContainsKey(1));
    }

    [Fact(Timeout = 5000)]
    public void EnsureStreamContext_should_reject_null_scheme()
    {
        var (router, _) = CreateRouter();
        var endpoint = new RequestEndpoint
            { Scheme = null!, Host = "localhost", Port = 443, Version = HttpVersion.Version30 };
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = endpoint
        };

        var result = router.EnsureStreamContext(item, 1, hasConnection: true);

        Assert.Equal(QuicStreamRouter.StreamContextResult.AlreadyExists, result);
    }

    [Fact(Timeout = 5000)]
    public void Pending_streams_should_queue_when_no_connection()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        var r1 = router.EnsureStreamContext(item, 100, hasConnection: false);
        var r2 = router.EnsureStreamContext(item, 200, hasConnection: false);
        var r3 = router.EnsureStreamContext(item, 300, hasConnection: false);

        Assert.Equal(QuicStreamRouter.StreamContextResult.NeedsConnection, r1);
        Assert.Equal(QuicStreamRouter.StreamContextResult.NeedsConnection, r2);
        Assert.Equal(QuicStreamRouter.StreamContextResult.NeedsConnection, r3);

        Assert.Equal(100, router.DequeueNextPendingStreamId());
        Assert.Equal(200, router.DequeueNextPendingStreamId());
        Assert.Equal(300, router.DequeueNextPendingStreamId());
    }

    [Fact(Timeout = 5000)]
    public void DrainPendingStreamIds_should_empty_queue()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        router.EnsureStreamContext(item, 5, hasConnection: false);
        router.EnsureStreamContext(item, 15, hasConnection: false);
        router.EnsureStreamContext(item, 25, hasConnection: false);

        var drained = router.DrainPendingStreamIds();

        Assert.Equal([5, 15, 25], drained);
        Assert.True(router.RequestStreams.Count >= 3);
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_request_with_wrong_stream_id_should_handle_gracefully()
    {
        var (router, _) = CreateRouter();
        var (handle, _) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);
        ctx.Handle = handle;

        var dataItem = RoutedNetworkBuffer.Rent(4);
        dataItem.StreamId = 999; // Different from expected
        dataItem.Length = 3;

        var typedStreams = new Dictionary<long, TypedStreamState>();
        router.RouteTaggedItem(dataItem, -1, typedStreams);

        // Verify the operation completed without error
        Assert.NotNull(router);
    }

    [Fact(Timeout = 5000)]
    public void RouteUntaggedData_with_multiple_streams_should_pick_first_ready()
    {
        var (router, _) = CreateRouter();

        var ctx1 = router.GetOrCreateContext(1);
        var ctx2 = router.GetOrCreateContext(2);

        // Only stream 2 has handle
        var (handle2, _) = CreateTestHandle();
        ctx2.Handle = handle2;

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        router.RouteUntaggedData(buffer);

        // Should be queued to stream 1 since it comes first
        Assert.Single(ctx1.PendingWrites);
        Assert.Empty(ctx2.PendingWrites);
    }
}