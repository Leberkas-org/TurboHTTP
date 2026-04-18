using Akka.Actor;
using Akka.Event;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Tcp;

// QUIC APIs are platform-guarded; usage is gated at runtime via ConnectItem.Options being QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Manages per-stream transport context for concurrent QUIC request streams —
/// context creation, tagged item routing, pending write buffering, and flush.
/// Extracted from <see cref="QuicTransportStateMachine"/> for single-responsibility.
/// </summary>
internal sealed class QuicStreamRouter
{
    private readonly ITransportOperations _ops;
    private readonly IActorRef _self;

    /// <summary>Per-stream transport context for concurrent request streams.</summary>
    private readonly Dictionary<long, RequestStreamContext> _requestStreams = new();

    /// <summary>
    /// Queue of stream IDs awaiting QUIC stream handles. When multiple requests arrive
    /// before the connection is established, each gets enqueued here. Dequeued one-by-one
    /// as request stream leases are acquired.
    /// </summary>
    private readonly Queue<long> _pendingOpenStreamIds = new();

    public IReadOnlyDictionary<long, RequestStreamContext> RequestStreams => _requestStreams;

    public QuicStreamRouter(ITransportOperations ops, IActorRef self)
    {
        _ops = ops;
        _self = self;
    }

    /// <summary>
    /// Ensures a stream context exists for the given stream ID. Returns true if a new
    /// connection must be established (no existing connection with control stream).
    /// Returns false if the context already existed or was handled.
    /// </summary>
    public StreamContextResult EnsureStreamContext(IOutputItem item, long streamId,
        bool hasConnection)
    {
        if (streamId < 0 || _requestStreams.ContainsKey(streamId) || string.IsNullOrEmpty(item.Key.Scheme) ||
            item.Key == RequestEndpoint.Default)
        {
            return StreamContextResult.AlreadyExists;
        }

        _requestStreams[streamId] = new RequestStreamContext();

        if (hasConnection)
        {
            return StreamContextResult.OpenNewStream;
        }

        _pendingOpenStreamIds.Enqueue(streamId);
        return StreamContextResult.NeedsConnection;
    }

    /// <summary>
    /// Routes a tagged item to the appropriate stream (request, control, or encoder).
    /// </summary>
    public void RouteTaggedItem(Http3NetworkBuffer dataItem,
        ConnectionHandle? controlHandle, Queue<NetworkBuffer> pendingControlItems,
        ConnectionHandle? encoderHandle, Queue<NetworkBuffer> pendingEncoderItems)
    {
        switch (dataItem.StreamType)
        {
            case Http3StreamType.Request:
                RouteToRequestStream(dataItem.StreamId, dataItem);
                break;
            case Http3StreamType.Control:
                RouteToTypedStream(controlHandle, pendingControlItems, dataItem);
                break;
            case Http3StreamType.QpackEncoder:
                RouteToTypedStream(encoderHandle, pendingEncoderItems, dataItem);
                break;
        }
    }

    /// <summary>
    /// Routes an untagged NetworkBuffer to the first available request stream.
    /// </summary>
    public void RouteUntaggedData(NetworkBuffer dataItem)
    {
        foreach (var ctx in _requestStreams.Values)
        {
            if (ctx.Handle is not null)
            {
                WriteToHandle(ctx.Handle, dataItem);
                return;
            }

            ctx.PendingWrites.Enqueue(dataItem);
            _ops.OnSignalPullInput();
            return;
        }

        // No request streams at all — drop
        _ops.Log.Warning("QuicConnectionStage: Untagged data received but no request stream — dropping.");
        _ops.OnSignalPullInput();
    }

    /// <summary>
    /// Handles an end-of-request item: completes the outbound channel or marks as pending.
    /// </summary>
    public void HandleEndOfRequest(Http3EndOfRequestItem endItem)
    {
        if (_requestStreams.TryGetValue(endItem.StreamId, out var ctx) && ctx.Handle is not null)
        {
            ctx.Handle.OutboundWriter.TryComplete();
        }
        else if (_requestStreams.TryGetValue(endItem.StreamId, out var pendingCtx))
        {
            pendingCtx.PendingEndOfRequest = true;
        }

        _ops.OnSignalPullInput();
    }

    /// <summary>
    /// Dequeues the next pending stream ID awaiting a QUIC stream handle.
    /// Returns -1 if no pending streams exist.
    /// </summary>
    public long DequeueNextPendingStreamId()
    {
        return _pendingOpenStreamIds.TryDequeue(out var id) ? id : -1;
    }

    /// <summary>
    /// Drains all remaining pending stream IDs into a list.
    /// Used after the connection is fully established to open remaining request streams.
    /// </summary>
    public List<long> DrainPendingStreamIds()
    {
        var result = new List<long>(_pendingOpenStreamIds.Count);
        while (_pendingOpenStreamIds.TryDequeue(out var id))
        {
            result.Add(id);
        }

        return result;
    }

    /// <summary>
    /// Gets or creates the context for a stream ID.
    /// </summary>
    public RequestStreamContext GetOrCreateContext(long streamId)
    {
        if (!_requestStreams.TryGetValue(streamId, out var ctx))
        {
            ctx = new RequestStreamContext();
            _requestStreams[streamId] = ctx;
        }

        return ctx;
    }

    /// <summary>
    /// Flushes all pending writes for a stream context and completes the writer if end-of-request was pending.
    /// </summary>
    public void FlushPendingWrites(RequestStreamContext ctx)
    {
        while (ctx.PendingWrites.TryDequeue(out var buffered))
        {
            WriteToHandle(ctx.Handle, buffered);
        }

        if (ctx.PendingEndOfRequest)
        {
            ctx.PendingEndOfRequest = false;
            ctx.Handle!.OutboundWriter.TryComplete();
        }
    }

    /// <summary>
    /// Flushes all request stream contexts that have handles assigned.
    /// </summary>
    public void FlushAllReadyStreams()
    {
        foreach (var ctx in _requestStreams.Values)
        {
            if (ctx.Handle is not null)
            {
                FlushPendingWrites(ctx);
            }
        }
    }

    /// <summary>
    /// Re-queues a rejected early-data buffer into the first request stream context.
    /// </summary>
    public void RequeueEarlyData(NetworkBuffer buffer)
    {
        foreach (var ctx in _requestStreams.Values)
        {
            ctx.PendingWrites.Enqueue(buffer);
            break;
        }

        _ops.OnSignalPullInput();
    }

    /// <summary>
    /// Removes a single request stream context by stream ID.
    /// </summary>
    public void RemoveStream(long streamId) => _requestStreams.Remove(streamId);

    /// <summary>
    /// Clears all request stream contexts and pending stream IDs.
    /// </summary>
    public void Clear()
    {
        _requestStreams.Clear();
        _pendingOpenStreamIds.Clear();
    }

    /// <summary>
    /// Disposes all pending writes in all stream contexts.
    /// </summary>
    public void DisposePendingWrites()
    {
        foreach (var ctx in _requestStreams.Values)
        {
            while (ctx.PendingWrites.TryDequeue(out var orphan))
            {
                orphan.Dispose();
            }
        }
    }

    private void RouteToRequestStream(long streamId, NetworkBuffer dataItem)
    {
        if (streamId >= 0 && _requestStreams.TryGetValue(streamId, out var ctx))
        {
            if (ctx.Handle is not null)
            {
                WriteToHandle(ctx.Handle, dataItem);
            }
            else
            {
                ctx.PendingWrites.Enqueue(dataItem);
                _ops.OnSignalPullInput();
            }
        }
        else
        {
            RouteUntaggedData(dataItem);
        }
    }

    private void RouteToTypedStream(ConnectionHandle? handle, Queue<NetworkBuffer> pendingQueue,
        NetworkBuffer dataItem)
    {
        if (handle is not null)
        {
            WriteToHandle(handle, dataItem);
        }
        else
        {
            pendingQueue.Enqueue(dataItem);
            _ops.OnSignalPullInput();
        }
    }

    private void WriteToHandle(ConnectionHandle? handle, NetworkBuffer buffer)
    {
        if (handle is null)
        {
            _ops.Log.Warning("QuicConnectionStage: Data received but no handle available — dropping element.");
            _ops.OnSignalPullInput();
            return;
        }

        _ = handle.OutboundWriter.WriteAsync(buffer)
            .PipeTo(_self,
                success: () => new OutboundWriteDone(),
                failure: ex => new OutboundWriteFailed(ex.GetBaseException()));
    }

    /// <summary>
    /// Per-stream transport state: tracks the handle, pending writes, and end-of-request flag
    /// for each concurrent request stream on the QUIC connection.
    /// </summary>
    internal sealed class RequestStreamContext
    {
        public ConnectionHandle? Handle;
        public readonly Queue<NetworkBuffer> PendingWrites = new();
        public bool PendingEndOfRequest;
    }

    internal enum StreamContextResult
    {
        AlreadyExists,
        OpenNewStream,
        NeedsConnection
    }
}