using Akka.Actor;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class TcpTransportFactorySpec
{
    private static readonly IPoolingStrategy TestStrategy = new TestPoolingStrategy();

    [Fact(Timeout = 5000)]
    public void TcpTransportFactory_should_accept_valid_actor_ref()
    {
        var factory = new TcpTransportFactory(ActorRefs.Nobody, TestStrategy);

        Assert.NotNull(factory);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_non_null_flow()
    {
        var factory = new TcpTransportFactory(ActorRefs.Nobody, TestStrategy);

        var flow = factory.Create();

        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_independent_flows()
    {
        var factory = new TcpTransportFactory(ActorRefs.Nobody, TestStrategy);

        var flow1 = factory.Create();
        var flow2 = factory.Create();

        Assert.NotSame(flow1, flow2);
    }

    private sealed class TestPoolingStrategy : IPoolingStrategy
    {
        public int MaxConnectionsPerHost => 6;
        public TimeSpan IdleTimeout => TimeSpan.FromSeconds(5);
        public TimeSpan ConnectionLifetime => Timeout.InfiniteTimeSpan;

        public bool CanReuse(TransportOptions options) => true;
        public PoolAction OnRelease(TransportOptions options) => PoolAction.Reuse;
        public PoolAction OnIdle(object lease) => PoolAction.Dispose;
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }
}
