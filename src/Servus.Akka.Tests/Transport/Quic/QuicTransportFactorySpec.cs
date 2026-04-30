using Akka;
using Akka.Actor;
using Akka.Streams.Dsl;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

public sealed class QuicTransportFactorySpec
{
    [Fact(Timeout = 5000)]
    public void Create_should_return_non_null_flow()
    {
        var factory = new QuicTransportFactory(ActorRefs.Nobody);

        var flow = factory.Create();

        Assert.NotNull(flow);
    }
}