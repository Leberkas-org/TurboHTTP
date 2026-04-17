using System.Buffers;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Per-stream header and body buffer management for HTTP/2.
/// Extracted from Http20ConnectionStage for independent testability.
/// </summary>
internal sealed class StreamState
{
    private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

    private IMemoryOwner<byte>? _headerOwner;
    private IMemoryOwner<byte>? _bodyOwner;
    private Memory<byte> _headerBuffer;
    private Memory<byte> _bodyBuffer;
    private int _headerLength;
    private int _bodyLength;
    private HttpResponseMessage? _response;
    private List<(string Name, string Value)>? _contentHeaders;

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is not null;

    public ReadOnlySpan<byte> GetHeaderSpan()
    {
        return _headerBuffer[.._headerLength].Span;
    }

    public void InitResponse(HttpResponseMessage response)
    {
        _response = response;
    }

    public HttpResponseMessage GetResponse()
    {
        return _response ?? throw new InvalidOperationException("No response has been initialized.");
    }

    public HttpResponseMessage GetOrCreateResponse()
    {
        return _response ??= new HttpResponseMessage();
    }

    public void AddContentHeader(string name, string value)
    {
        _contentHeaders ??= [];
        _contentHeaders.Add((name, value));
    }

    public void ApplyContentHeadersTo(HttpContent content)
    {
        if (_contentHeaders is null)
        {
            return;
        }

        foreach (var (name, value) in _contentHeaders)
        {
            content.Headers.TryAddWithoutValidation(name, value);
        }
    }

    public void Reset()
    {
        _headerOwner?.Dispose();
        _headerOwner = null;
        _bodyOwner?.Dispose();
        _bodyOwner = null;
        _headerBuffer = default;
        _bodyBuffer = default;
        _headerLength = 0;
        _bodyLength = 0;
        _response = null;
        _contentHeaders = null;
    }

    public (IMemoryOwner<byte>? Owner, int Length) TakeBodyOwnership()
    {
        var owner = _bodyOwner;
        var length = _bodyLength;
        _bodyOwner = null;
        _bodyLength = 0;
        return (owner, length);
    }

    public void AppendHeader(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        EnsureHeaderCapacity(_headerLength + data.Length);
        data.CopyTo(_headerBuffer.Span[_headerLength..]);
        _headerLength += data.Length;
    }

    public void AppendBody(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        EnsureBodyCapacity(_bodyLength + data.Length);
        data.CopyTo(_bodyBuffer.Span[_bodyLength..]);
        _bodyLength += data.Length;
    }

    private void EnsureHeaderCapacity(int required)
    {
        if (_headerOwner == null || required > _headerBuffer.Length)
        {
            RentNewHeaderBuffer(required);
        }
    }

    private void EnsureBodyCapacity(int required)
    {
        if (_bodyOwner == null || required > _bodyBuffer.Length)
        {
            RentNewBodyBuffer(required);
        }
    }

    private void RentNewHeaderBuffer(int size)
    {
        var newOwner = _pool.Rent(size);
        if (_headerOwner != null)
        {
            _headerBuffer.Span.CopyTo(newOwner.Memory.Span);
            _headerOwner.Dispose();
        }

        _headerOwner = newOwner;
        _headerBuffer = newOwner.Memory;
    }

    private void RentNewBodyBuffer(int size)
    {
        var newOwner = _pool.Rent(size);
        if (_bodyOwner != null)
        {
            _bodyBuffer.Span.CopyTo(newOwner.Memory.Span);
            _bodyOwner.Dispose();
        }

        _bodyOwner = newOwner;
        _bodyBuffer = newOwner.Memory;
    }
}
