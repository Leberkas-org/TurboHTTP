using System.Buffers;

namespace TurboHttp.StreamTests;

internal sealed class SimpleMemoryOwner(byte[] data) : IMemoryOwner<byte>
{
    public Memory<byte> Memory { get; } = data;

    public void Dispose()
    {
    }
}