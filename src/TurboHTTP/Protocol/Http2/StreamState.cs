using System.Buffers;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Per-stream header and body buffer management for HTTP/2.
/// Extracted from Http20ConnectionStage for independent testability.
/// </summary>
public sealed class StreamState
{
    private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

    private IMemoryOwner<byte>? _headerOwner;
    private IMemoryOwner<byte>? _bodyOwner;

    public Memory<byte> HeaderBuffer;
    public Memory<byte> BodyBuffer;

    public int HeaderLength;
    public int BodyLength;

    public HttpResponseMessage? Response;

    // Content headers captured during DecodeHeaders, applied when Content is created.
    public List<(string Name, string Value)>? ContentHeaders;

    public void Reset()
    {
        _headerOwner?.Dispose();
        _headerOwner = null;
        _bodyOwner?.Dispose();
        _bodyOwner = null;
        HeaderBuffer = default;
        BodyBuffer = default;
        HeaderLength = 0;
        BodyLength = 0;
        Response = null;
        ContentHeaders?.Clear();
    }

    public (IMemoryOwner<byte>? Owner, int Length) TakeBodyOwnership()
    {
        var owner = _bodyOwner;
        var length = BodyLength;
        _bodyOwner = null;
        BodyLength = 0;
        return (owner, length);
    }

    public void AppendHeader(ReadOnlySpan<byte> data)
    {
        EnsureHeaderCapacity(HeaderLength + data.Length);
        data.CopyTo(HeaderBuffer.Span[HeaderLength..]);
        HeaderLength += data.Length;
    }

    public void AppendBody(ReadOnlySpan<byte> data)
    {
        EnsureBodyCapacity(BodyLength + data.Length);
        data.CopyTo(BodyBuffer.Span[BodyLength..]);
        BodyLength += data.Length;
    }

    private void EnsureHeaderCapacity(int required)
    {
        if (_headerOwner == null || required > HeaderBuffer.Length)
        {
            RentNewHeaderBuffer(required);
        }
    }

    private void EnsureBodyCapacity(int required)
    {
        if (_bodyOwner == null || required > BodyBuffer.Length)
        {
            RentNewBodyBuffer(required);
        }
    }

    private void RentNewHeaderBuffer(int size)
    {
        var newOwner = _pool.Rent(size);
        if (_headerOwner != null)
        {
            HeaderBuffer.Span.CopyTo(newOwner.Memory.Span);
            _headerOwner.Dispose();
        }

        _headerOwner = newOwner;
        HeaderBuffer = newOwner.Memory;
    }

    private void RentNewBodyBuffer(int size)
    {
        var newOwner = _pool.Rent(size);
        if (_bodyOwner != null)
        {
            BodyBuffer.Span.CopyTo(newOwner.Memory.Span);
            _bodyOwner.Dispose();
        }

        _bodyOwner = newOwner;
        BodyBuffer = newOwner.Memory;
    }
}
