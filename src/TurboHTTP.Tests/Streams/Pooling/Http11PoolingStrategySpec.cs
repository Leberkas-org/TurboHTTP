using Servus.Akka.Transport;
using TurboHTTP.Streams.Pooling;

namespace TurboHTTP.Tests.Streams.Pooling;

public sealed class Http11PoolingStrategySpec
{
    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Reuse()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Reuse, strategy.OnUpstreamFinish(new object()));
    }

    [Fact(Timeout = 5000)]
    public void OnDisconnect_should_return_Dispose()
    {
        var strategy = new Http11PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnDisconnect(new object(), DisconnectReason.Error));
    }
}