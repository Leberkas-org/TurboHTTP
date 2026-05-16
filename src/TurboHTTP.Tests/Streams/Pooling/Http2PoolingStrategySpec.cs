using Servus.Akka.Transport;
using TurboHTTP.Streams.Pooling;

namespace TurboHTTP.Tests.Streams.Pooling;

public sealed class Http2PoolingStrategySpec
{
    [Fact(Timeout = 5000)]
    public void OnUpstreamFinish_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnUpstreamFinish(new object()));
    }

    [Fact(Timeout = 5000)]
    public void OnDisconnect_should_return_Dispose()
    {
        var strategy = new Http2PoolingStrategy();
        Assert.Equal(PoolAction.Dispose, strategy.OnDisconnect(new object(), DisconnectReason.Error));
    }
}