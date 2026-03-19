using System.Buffers;
using TurboHttp.IO;
using TurboHttp.Protocol.RFC9112;

namespace TurboHttp.Internal;

public interface IInputItem
{
    RequestEndpoint Key { get; }
}

public interface IOutputItem
{
    RequestEndpoint Key { get; }
}

public interface IControlItem : IOutputItem;

public record ConnectionReuseItem(RequestEndpoint Key, ConnectionReuseDecision Decision) : IControlItem;

public record ConnectItem(TcpOptions Options) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public record DataItem(IMemoryOwner<byte> Memory, int Length) : IOutputItem, IInputItem
{
    public RequestEndpoint Key { get; init; }
}

public record MaxConcurrentStreamsItem(int MaxStreams) : IControlItem
{
    public RequestEndpoint Key { get; init; }
}

public record StreamAcquireItem : IControlItem
{
    public RequestEndpoint Key { get; init; }
}