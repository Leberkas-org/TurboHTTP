namespace TurboHTTP.Protocol.Multiplexed.Body;

internal sealed class StreamingBodyDecoder : IBodyDecoder
{
    private readonly BodyHandle _handle;

    public StreamingBodyDecoder(long maxBodySize = long.MaxValue)
    {
        _handle = new BodyHandle(maxBodySize);
    }

    public bool IsBuffered => false;
    public bool IsComplete { get; private set; }

    public void Feed(ReadOnlySpan<byte> data, bool endStream)
    {
        if (!data.IsEmpty)
        {
            _handle.Feed(data);
        }

        if (endStream)
        {
            IsComplete = true;
            _handle.Complete();
        }
    }

    public HttpContent GetContent()
    {
        return new StreamContent(_handle.AsStream());
    }

    public Stream GetBodyStream()
    {
        return _handle.AsStream();
    }

    public void Abort()
    {
        _handle.Abort(new OperationCanceledException());
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
}