using Akka.Actor;

namespace Servus.Akka.Transport.Quic;

internal sealed class StreamContext
{
    private readonly StreamDirection _direction;
    private StreamHandle? _handle;
    private readonly Queue<TransportBuffer> _pendingWrites = new();
    private IActorRef? _self;

    public StreamContext(StreamDirection direction)
    {
        _direction = direction;
    }

    internal void SetSelf(IActorRef self)
    {
        _self = self;
    }

    public bool HasHandle() => _handle is not null;

    public void AttachHandle(StreamHandle handle)
    {
        _handle = handle;
    }

    public void Write(TransportBuffer buffer)
    {
        if (_handle is null)
        {
            _pendingWrites.Enqueue(buffer);
            return;
        }

        _ = _handle.WriteAsync(buffer).AsTask().ContinueWith((t, state) =>
        {
            var self = (IActorRef)state!;
            if (t.IsFaulted)
            {
                self.Tell(new OutboundWriteFailed(t.Exception!.GetBaseException(), -1));
            }
        }, _self, TaskScheduler.Default);
    }

    public bool TryDequeuePendingWrite(out TransportBuffer? buffer)
    {
        return _pendingWrites.TryDequeue(out buffer);
    }

    public void CompleteWrites()
    {
        _handle?.CompleteWrites();
    }

    public StreamDirection Direction() => _direction;

    private void DisposePendingWrites()
    {
        while (_pendingWrites.TryDequeue(out var orphan))
        {
            orphan.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        DisposePendingWrites();
        if (_handle is not null)
        {
            await _handle.DisposeAsync().ConfigureAwait(false);
            _handle = null;
        }
    }
}
