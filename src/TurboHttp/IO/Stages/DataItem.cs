using System.Buffers;

namespace TurboHttp.IO.Stages;

public record DataItem(IMemoryOwner<byte> Memory, int Length) : IOutputItem, IInputItem
{
    public HostKey Key { get; init; } = HostKey.Default;
}