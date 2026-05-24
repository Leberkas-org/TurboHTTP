using System.Buffers;

namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class ContentLengthBufferedDecoder : IBodyDecoder
{
    private readonly int _expected;
    private readonly IMemoryOwner<byte> _owner;
    private int _received;
    private bool _complete;

    public bool IsBuffered => true;
    public IReadOnlyList<(string Name, string Value)> Trailers => [];
    public bool IsComplete => _complete;

    public ContentLengthBufferedDecoder(int expected, MemoryPool<byte> pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expected);
        _expected = expected;
        _owner = pool.Rent(Math.Max(expected, 1));
        _complete = expected == 0;
    }

    public bool Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        var need = _expected - _received;
        var take = Math.Min(need, data.Length);
        if (take > 0)
        {
            data[..take].CopyTo(_owner.Memory.Span[_received..]);
            _received += take;
        }

        consumed = take;
        _complete = _received == _expected;
        return _complete;
    }

    public bool OnEof() => _complete;

    public int Drain(ReadOnlySpan<byte> data)
    {
        if (_complete)
        {
            return 0;
        }

        var need = _expected - _received;
        var take = Math.Min(need, data.Length);
        if (take > 0)
        {
            _received += take;
        }

        _complete = _received == _expected;
        return take;
    }

    public Stream GetBodyStream() => new MemoryStream(_owner.Memory[.._expected].ToArray());

    public void Dispose()
    {
        _owner.Dispose();
    }
}