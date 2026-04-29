using Servus.Akka.Transport;

namespace TurboHTTP.Tests.Shared;

internal static class TransportBufferTestExtensions
{
    internal static TransportBuffer FromArray(byte[] data, int length = -1)
    {
        var len = length < 0 ? data.Length : length;
        var buf = TransportBuffer.Rent(len);
        data.AsSpan(0, len).CopyTo(buf.FullMemory.Span);
        buf.Length = len;
        return buf;
    }
}
