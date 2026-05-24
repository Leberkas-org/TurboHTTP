namespace TurboHTTP.Protocol.LineBased.Body;

internal sealed class CloseDelimitedBodyDecoder : IBodyDecoder
{
    private readonly BodyHandle _handle;

    public bool IsBuffered => false;
    public IReadOnlyList<(string Name, string Value)> Trailers => [];
    public bool IsComplete => false;

    public CloseDelimitedBodyDecoder(long maxBodySize = 10_485_760)
    {
        _handle = new BodyHandle(maxBodySize);
    }

    public bool Feed(ReadOnlySpan<byte> data, out int consumed)
    {
        if (data.Length > 0)
        {
            _handle.Feed(data);
        }

        consumed = data.Length;
        return false;
    }

    public bool OnEof()
    {
        _handle.Complete();
        return true;
    }

    public int Drain(ReadOnlySpan<byte> data)
    {
        return 0;
    }

    public Stream GetBodyStream() => _handle.AsStream();

    public void Dispose()
    {
        _handle.Dispose();
    }
}