using System.Buffers;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Per-stream response assembly state for HTTP/3 multiplexing.
/// Each active request stream gets its own instance so multiple responses
/// can be assembled concurrently. Pooled and reused via <see cref="Reset"/>.
/// </summary>
internal sealed class StreamState
{
    private readonly MemoryPool<byte> _pool = MemoryPool<byte>.Shared;

    private IMemoryOwner<byte>? _bodyOwner;
    private Memory<byte> _bodyBuffer;
    private int _bodyLength;
    private HttpResponseMessage? _response;
    private List<(string Name, string Value)>? _contentHeaders;

    public long StreamId { get; private set; } = -1;

    public bool HasResponse => _response is not null;

    public bool HasContentHeaders => _contentHeaders is not null;

    public long? ExpectedContentLength { get; set; }

    public long AccumulatedBodyLength => _bodyLength;

    public void Initialize(long streamId)
    {
        StreamId = streamId;
    }

    public HttpResponseMessage InitResponse()
    {
        _response = new HttpResponseMessage();
        return _response;
    }

    public HttpResponseMessage GetResponse()
    {
        return _response ?? throw new InvalidOperationException("No response has been initialized.");
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
        _bodyOwner?.Dispose();
        _bodyOwner = null;
        _bodyBuffer = default;
        _bodyLength = 0;
        StreamId = -1;
        _response = null;
        ExpectedContentLength = null;
        _contentHeaders?.Clear();
    }

    public (IMemoryOwner<byte>? Owner, int Length) TakeBodyOwnership()
    {
        var owner = _bodyOwner;
        var length = _bodyLength;
        _bodyOwner = null;
        _bodyLength = 0;
        return (owner, length);
    }

    public void AppendBody(ReadOnlySpan<byte> data)
    {
        EnsureBodyCapacity(_bodyLength + data.Length);
        data.CopyTo(_bodyBuffer.Span[_bodyLength..]);
        _bodyLength += data.Length;
    }

    private void EnsureBodyCapacity(int required)
    {
        if (_bodyOwner != null && required <= _bodyBuffer.Length)
        {
            return;
        }

        var newOwner = _pool.Rent(required);

        if (_bodyOwner != null)
        {
            _bodyBuffer.Span.CopyTo(newOwner.Memory.Span);
            _bodyOwner.Dispose();
        }

        _bodyOwner = newOwner;
        _bodyBuffer = newOwner.Memory;
    }
}
