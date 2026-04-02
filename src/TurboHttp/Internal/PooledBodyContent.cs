using System.Buffers;
using System.Net;

namespace TurboHttp.Internal;

/// <summary>
/// An <see cref="HttpContent"/> backed by a pooled <see cref="IMemoryOwner{T}"/>.
/// Writes directly from the rented memory without copying. The memory is returned
/// to the pool when the content (and therefore the owning <see cref="HttpResponseMessage"/>)
/// is disposed.
/// </summary>
internal sealed class PooledBodyContent : HttpContent
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public PooledBodyContent(IMemoryOwner<byte> owner, int length)
    {
        _owner = owner;
        _length = length;
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
        => stream.Write(_owner!.Memory.Span[.._length]);

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var vt = stream.WriteAsync(_owner!.Memory[.._length]);
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        var vt = stream.WriteAsync(_owner!.Memory[.._length], cancellationToken);
        return vt.IsCompletedSuccessfully ? Task.CompletedTask : vt.AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Dispose();
        }

        base.Dispose(disposing);
    }
}