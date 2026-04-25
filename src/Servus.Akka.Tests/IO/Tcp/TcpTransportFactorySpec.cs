using Akka.Actor;
using Servus.Akka.IO.Tcp;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpTransportFactorySpec
{
    [Fact(Timeout = 5000)]
    public void TcpTransportFactory_should_throw_on_null_connection_manager()
    {
        Assert.Throws<ArgumentNullException>(() => new TcpTransportFactory(null!));
    }

    [Fact(Timeout = 5000)]
    public void TcpTransportFactory_should_accept_valid_actor_ref()
    {
        var factory = new TcpTransportFactory(ActorRefs.Nobody);

        Assert.NotNull(factory);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_non_null_flow()
    {
        var factory = new TcpTransportFactory(ActorRefs.Nobody);

        var flow = factory.Create();

        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_independent_flows()
    {
        var factory = new TcpTransportFactory(ActorRefs.Nobody);

        var flow1 = factory.Create();
        var flow2 = factory.Create();

        Assert.NotSame(flow1, flow2);
    }
}
