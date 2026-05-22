using System.Buffers;

namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class BufferedBodyDecoder : IBodyDecoder
{
    private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;
    private IMemoryOwner<byte>? _owner;
    private int _length;

    public bool IsBuffered => true;
    public bool IsComplete { get; private set; }

    public void Feed(ReadOnlySpan<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            EnsureCapacity(_length + data.Length);
            data.CopyTo(_owner!.Memory.Span[_length..]);
            _length += data.Length;
        }

        if (endStream)
        {
            IsComplete = true;
        }
    }

    public HttpContent GetContent()
    {
        if (_length == 0)
        {
            return new ByteArrayContent([]);
        }

        var bytes = _owner!.Memory[.._length].ToArray();
        return new ByteArrayContent(bytes);
    }

    public Stream GetBodyStream()
    {
        if (_length == 0)
        {
            return Stream.Null;
        }

        var bytes = _owner!.Memory[.._length].ToArray();
        return new MemoryStream(bytes, writable: false);
    }

    public void Abort()
    {
        Dispose();
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }

    private void EnsureCapacity(int needed)
    {
        if (_owner != null && _owner.Memory.Length >= needed)
        {
            return;
        }

        var newSize = Math.Max(needed, (_owner?.Memory.Length ?? 256) * 2);
        var newOwner = _pool.Rent(newSize);

        if (_owner != null && _length > 0)
        {
            _owner.Memory[.._length].CopyTo(newOwner.Memory);
        }

        _owner?.Dispose();
        _owner = newOwner;
    }
}
