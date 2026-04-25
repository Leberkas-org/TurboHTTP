using Servus.Akka.IO.Quic;

#pragma warning disable CA1416

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicConnectionFactorySpec
{
    [Fact(Timeout = 5000)]
    public void Instance_should_be_singleton()
    {
        var instance1 = QuicConnectionFactory.Instance;
        var instance2 = QuicConnectionFactory.Instance;

        Assert.Same(instance1, instance2);
    }

    [Fact(Timeout = 5000)]
    public void Instance_should_not_be_null()
    {
        Assert.NotNull(QuicConnectionFactory.Instance);
    }
}
