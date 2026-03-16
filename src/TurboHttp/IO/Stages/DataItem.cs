using System.Buffers;

namespace TurboHttp.IO.Stages;

public record DataItem(HostKey Key, IMemoryOwner<byte> Memory, int Length) : IOutputItem, IInputItem;