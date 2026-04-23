using Servus.Akka.IO;

namespace TurboHTTP.Tests.Shared;

/// <summary>
/// Test-only helper that replicates the removed <c>NetworkBuffer.FromArray</c> convenience method.
/// Wraps a byte array in a <see cref="NetworkBuffer"/> without copying, using a non-disposing owner.
/// </summary>
internal static class NetworkBufferTestExtensions
{
    internal static NetworkBuffer FromArray(byte[] data, int length = -1)
    {
        var len = length < 0 ? data.Length : length;
        var buf = NetworkBuffer.Rent(len);
        data.AsSpan(0, len).CopyTo(buf.FullMemory.Span);
        buf.Length = len;
        return buf;
    }
}
