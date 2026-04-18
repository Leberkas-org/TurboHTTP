using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.StreamTests.Transport;

public sealed class QuicStreamRouterSpec
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
    public void EnsureStreamContext_should_return_NeedsConnection_when_no_connection()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        var result = router.EnsureStreamContext(item, 1, hasConnection: false);

        Assert.Equal(QuicStreamRouter.StreamContextResult.NeedsConnection, result);
        Assert.True(router.RequestStreams.ContainsKey(1));
    }

    [Fact(Timeout = 5000)]
    public void EnsureStreamContext_should_return_OpenNewStream_when_connected()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        var result = router.EnsureStreamContext(item, 1, hasConnection: true);

        Assert.Equal(QuicStreamRouter.StreamContextResult.OpenNewStream, result);
        Assert.True(router.RequestStreams.ContainsKey(1));
    }

    [Fact(Timeout = 5000)]
    public void EnsureStreamContext_should_return_AlreadyExists_for_known_stream_id()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        router.EnsureStreamContext(item, 1, hasConnection: true);
        var result = router.EnsureStreamContext(item, 1, hasConnection: true);

        Assert.Equal(QuicStreamRouter.StreamContextResult.AlreadyExists, result);
    }

    [Fact(Timeout = 5000)]
    public void EnsureStreamContext_should_return_AlreadyExists_for_negative_stream_id()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        var result = router.EnsureStreamContext(item, -1, hasConnection: true);

        Assert.Equal(QuicStreamRouter.StreamContextResult.AlreadyExists, result);
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_should_write_to_handle_for_known_request_stream()
    {
        var (router, _) = CreateRouter();
        var (handle, outboundReader) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);
        ctx.Handle = handle;

        var dataItem = Http3NetworkBuffer.Rent(4);
        dataItem.StreamType = Http3StreamType.Request;
        dataItem.StreamId = 1;
        dataItem.Length = 3;

        router.RouteTaggedItem(dataItem, null, new Queue<NetworkBuffer>(), null, new Queue<NetworkBuffer>());

        Assert.True(outboundReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_should_enqueue_when_handle_not_ready()
    {
        var (router, ops) = CreateRouter();
        router.GetOrCreateContext(1);

        var dataItem = Http3NetworkBuffer.Rent(4);
        dataItem.StreamType = Http3StreamType.Request;
        dataItem.StreamId = 1;
        dataItem.Length = 3;

        router.RouteTaggedItem(dataItem, null, new Queue<NetworkBuffer>(), null, new Queue<NetworkBuffer>());

        Assert.Single(router.RequestStreams[1].PendingWrites);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_should_route_control_to_pending_queue_when_no_handle()
    {
        var (router, ops) = CreateRouter();
        var pendingControl = new Queue<NetworkBuffer>();

        var dataItem = Http3NetworkBuffer.Rent(4);
        dataItem.StreamType = Http3StreamType.Control;
        dataItem.Length = 3;

        router.RouteTaggedItem(dataItem, null, pendingControl, null, new Queue<NetworkBuffer>());

        Assert.Single(pendingControl);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RouteTaggedItem_should_write_control_to_handle_when_available()
    {
        var (router, _) = CreateRouter();
        var (controlHandle, controlReader) = CreateTestHandle();

        var dataItem = Http3NetworkBuffer.Rent(4);
        dataItem.StreamType = Http3StreamType.Control;
        dataItem.Length = 3;

        router.RouteTaggedItem(dataItem, controlHandle, new Queue<NetworkBuffer>(), null, new Queue<NetworkBuffer>());

        Assert.True(controlReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void RouteUntaggedData_should_write_to_first_stream_with_handle()
    {
        var (router, _) = CreateRouter();
        var (handle, outboundReader) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);
        ctx.Handle = handle;

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        router.RouteUntaggedData(buffer);

        Assert.True(outboundReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void RouteUntaggedData_should_enqueue_when_no_handle_on_first_stream()
    {
        var (router, ops) = CreateRouter();
        router.GetOrCreateContext(1);

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        router.RouteUntaggedData(buffer);

        Assert.Single(router.RequestStreams[1].PendingWrites);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RouteUntaggedData_should_drop_when_no_request_streams()
    {
        var (router, ops) = CreateRouter();
        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);

        router.RouteUntaggedData(buffer);

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandleEndOfRequest_should_complete_outbound_writer()
    {
        var (router, ops) = CreateRouter();
        var (handle, _) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);
        ctx.Handle = handle;

        router.HandleEndOfRequest(new Http3EndOfRequestItem { Key = TestEndpoint, StreamId = 1 });

        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void HandleEndOfRequest_should_mark_pending_when_no_handle()
    {
        var (router, ops) = CreateRouter();
        router.GetOrCreateContext(1);

        router.HandleEndOfRequest(new Http3EndOfRequestItem { Key = TestEndpoint, StreamId = 1 });

        Assert.True(router.RequestStreams[1].PendingEndOfRequest);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void DequeueNextPendingStreamId_should_return_oldest_first()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        router.EnsureStreamContext(item, 10, hasConnection: false);
        router.EnsureStreamContext(item, 20, hasConnection: false);
        router.EnsureStreamContext(item, 30, hasConnection: false);

        Assert.Equal(10, router.DequeueNextPendingStreamId());
        Assert.Equal(20, router.DequeueNextPendingStreamId());
        Assert.Equal(30, router.DequeueNextPendingStreamId());
        Assert.Equal(-1, router.DequeueNextPendingStreamId());
    }

    [Fact(Timeout = 5000)]
    public void DrainPendingStreamIds_should_return_all_and_clear()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        router.EnsureStreamContext(item, 10, hasConnection: false);
        router.EnsureStreamContext(item, 20, hasConnection: false);

        var drained = router.DrainPendingStreamIds();

        Assert.Equal([10, 20], drained);
        Assert.Equal(-1, router.DequeueNextPendingStreamId());
    }

    [Fact(Timeout = 5000)]
    public void FlushPendingWrites_should_drain_queue_to_handle()
    {
        var (router, _) = CreateRouter();
        var (handle, outboundReader) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);

        ctx.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([1, 2]));
        ctx.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([3, 4]));
        ctx.Handle = handle;

        router.FlushPendingWrites(ctx);

        Assert.True(outboundReader.TryRead(out _));
        Assert.True(outboundReader.TryRead(out _));
        Assert.False(outboundReader.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void FlushPendingWrites_should_complete_writer_when_end_of_request_pending()
    {
        var (router, _) = CreateRouter();
        var (handle, _) = CreateTestHandle();
        var ctx = router.GetOrCreateContext(1);

        ctx.PendingEndOfRequest = true;
        ctx.Handle = handle;

        router.FlushPendingWrites(ctx);

        Assert.False(ctx.PendingEndOfRequest);
    }

    [Fact(Timeout = 5000)]
    public void FlushAllReadyStreams_should_process_all_streams_with_handles()
    {
        var (router, _) = CreateRouter();

        var (handle1, reader1) = CreateTestHandle();
        var ctx1 = router.GetOrCreateContext(1);
        ctx1.Handle = handle1;
        ctx1.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([1]));

        var (handle2, reader2) = CreateTestHandle();
        var ctx2 = router.GetOrCreateContext(2);
        ctx2.Handle = handle2;
        ctx2.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([2]));

        router.FlushAllReadyStreams();

        Assert.True(reader1.TryRead(out _));
        Assert.True(reader2.TryRead(out _));
    }

    [Fact(Timeout = 5000)]
    public void RequeueEarlyData_should_enqueue_to_first_stream()
    {
        var (router, ops) = CreateRouter();
        router.GetOrCreateContext(1);

        var buffer = NetworkBufferTestExtensions.FromArray([1, 2, 3]);
        router.RequeueEarlyData(buffer);

        Assert.Single(router.RequestStreams[1].PendingWrites);
        Assert.True(ops.PullInputCount > 0);
    }

    [Fact(Timeout = 5000)]
    public void RemoveStream_should_cleanup_context()
    {
        var (router, _) = CreateRouter();
        router.GetOrCreateContext(1);

        Assert.True(router.RequestStreams.ContainsKey(1));

        router.RemoveStream(1);

        Assert.False(router.RequestStreams.ContainsKey(1));
    }

    [Fact(Timeout = 5000)]
    public void Clear_should_remove_all_streams_and_pending_ids()
    {
        var (router, _) = CreateRouter();
        var item = new ConnectItem(new QuicOptions { Host = "localhost", Port = 443 })
        {
            Key = TestEndpoint
        };

        router.EnsureStreamContext(item, 1, hasConnection: false);
        router.EnsureStreamContext(item, 2, hasConnection: false);

        router.Clear();

        Assert.Empty(router.RequestStreams);
        Assert.Equal(-1, router.DequeueNextPendingStreamId());
    }

    [Fact(Timeout = 5000)]
    public void DisposePendingWrites_should_drain_all_queues()
    {
        var (router, _) = CreateRouter();
        var ctx1 = router.GetOrCreateContext(1);
        ctx1.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([1]));
        ctx1.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([2]));

        var ctx2 = router.GetOrCreateContext(2);
        ctx2.PendingWrites.Enqueue(NetworkBufferTestExtensions.FromArray([3]));

        router.DisposePendingWrites();

        Assert.Empty(ctx1.PendingWrites);
        Assert.Empty(ctx2.PendingWrites);
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateContext_should_create_new_when_missing()
    {
        var (router, _) = CreateRouter();

        var ctx = router.GetOrCreateContext(42);

        Assert.NotNull(ctx);
        Assert.True(router.RequestStreams.ContainsKey(42));
    }

    [Fact(Timeout = 5000)]
    public void GetOrCreateContext_should_return_existing_when_present()
    {
        var (router, _) = CreateRouter();

        var ctx1 = router.GetOrCreateContext(42);
        var ctx2 = router.GetOrCreateContext(42);

        Assert.Same(ctx1, ctx2);
    }
}