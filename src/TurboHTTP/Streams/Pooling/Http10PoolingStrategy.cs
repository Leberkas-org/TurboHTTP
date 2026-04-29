using Servus.Akka.Transport;

namespace TurboHTTP.Streams.Pooling;

internal sealed class Http10PoolingStrategy : IPoolingStrategy
{
    public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
    public PoolAction OnUpstreamFinish(object lease) => PoolAction.Dispose;
}
