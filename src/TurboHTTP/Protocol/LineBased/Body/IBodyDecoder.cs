namespace TurboHTTP.Protocol.LineBased.Body;

internal interface IBodyDecoder : IDisposable
{
    bool IsBuffered { get; }
    IReadOnlyList<(string Name, string Value)> Trailers { get; }
    bool IsComplete { get; }
    bool Feed(ReadOnlySpan<byte> data, out int consumed);
    bool OnEof();
    int Drain(ReadOnlySpan<byte> data);
    Stream GetBodyStream();
}
