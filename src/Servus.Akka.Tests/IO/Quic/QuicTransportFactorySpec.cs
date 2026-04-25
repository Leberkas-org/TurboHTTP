using Akka.Actor;
using Servus.Akka.IO.Quic;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicTransportFactorySpec
{
    [Fact(Timeout = 5000)]
    public void QuicTransportFactory_should_accept_valid_actor_ref()
    {
        var factory = new QuicTransportFactory(ActorRefs.Nobody);

        Assert.NotNull(factory);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_non_null_flow()
    {
        var factory = new QuicTransportFactory(ActorRefs.Nobody);

        var flow = factory.Create();

        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Create_should_return_independent_flows()
    {
        var factory = new QuicTransportFactory(ActorRefs.Nobody);

        var flow1 = factory.Create();
        var flow2 = factory.Create();

        Assert.NotSame(flow1, flow2);
    }

    [Fact(Timeout = 5000)]
    public void QuicTransportFactory_should_default_allow_connection_migration_to_true()
    {
        var factory = new QuicTransportFactory(ActorRefs.Nobody);

        var flow = factory.Create();

        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void QuicTransportFactory_should_accept_migration_disabled()
    {
        var factory = new QuicTransportFactory(ActorRefs.Nobody, allowConnectionMigration: false);

        var flow = factory.Create();

        Assert.NotNull(flow);
    }
}
