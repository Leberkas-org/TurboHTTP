using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

internal sealed class TypedStreamState
{
    public ConnectionHandle? Handle;
    public readonly Queue<NetworkBuffer> PendingItems = new();
    public long StreamId;
    public long OriginalSyntheticStreamId;
    public bool IsOutbound;
}
