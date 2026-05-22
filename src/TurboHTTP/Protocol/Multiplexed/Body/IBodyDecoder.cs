namespace TurboHTTP.Protocol.Multiplexed.Body;

internal interface IBodyDecoder : IDisposable
{
    bool IsBuffered { get; }
    bool IsComplete { get; }
    void Feed(ReadOnlySpan<byte> data, bool endStream);
    HttpContent GetContent();
    Stream GetBodyStream();
    void Abort();
}
