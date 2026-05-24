namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthStreamedDecoder : IBodyDecoder
{
    private readonly long _expected;
    private readonly BodyHandle _handle;
    private long _received;
    private bool _complete;

    public bool IsBuffered => false;
    public IReadOnlyList<(string Name, string Value)> Trailers => [];
    public bool IsComplete => _complete;

    public ContentLengthStreamedDecoder(long expected, long maxBodySize = 10_485_760)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        _expected = expected;
        _handle = new BodyHandle(maxBodySize);
        _complete = expected == 0;
        if (_complete)
        {
            _handle.Complete();
        }
    }

    public bool Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        if (_complete)
        {
            consumed = 0;
            return true;
        }

        var need = (int)Math.Min(int.MaxValue, _expected - _received);
        var take = Math.Min(need, data.Length);
        if (take > 0)
        {
            _handle.Feed(data[..take]);
            _received += take;
        }

        consumed = take;
        _complete = _received == _expected;
        if (_complete)
        {
            _handle.Complete();
        }

        return _complete;
    }

    public bool OnEof()
    {
        if (!_complete)
        {
            _handle.Abort(new HttpProtocolException("Connection closed before content-length satisfied."));
        }

        return _complete;
    }

    public int Drain(ReadOnlySpan<byte> data)
    {
        if (_complete)
        {
            return 0;
        }

        var need = (int)Math.Min(int.MaxValue, _expected - _received);
        var take = Math.Min(need, data.Length);
        if (take > 0)
        {
            _received += take;
        }

        _complete = _received == _expected;
        if (_complete)
        {
            _handle.Complete();
        }

        return take;
    }

    public Stream GetBodyStream() => _handle.AsStream();

    public void Dispose()
    {
        _handle.Dispose();
    }
}