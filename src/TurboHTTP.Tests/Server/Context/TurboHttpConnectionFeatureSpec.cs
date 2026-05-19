using System.Net;
using TurboHTTP.Server;
using TurboHTTP.Server.Context.Features;

namespace TurboHTTP.Tests.Server.Context;

public sealed class TurboHttpConnectionFeatureSpec
{
    [Fact(Timeout = 5000)]
    public void ConnectionId_should_delegate_to_connection_info()
    {
        var info = new TurboConnectionInfo("conn-42", IPAddress.Loopback, 12345, IPAddress.Any, 443);
        var feature = new TurboHttpConnectionFeature(info);
        Assert.Equal("conn-42", feature.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public void RemoteIpAddress_should_delegate_to_connection_info()
    {
        var info = new TurboConnectionInfo("c", IPAddress.Parse("10.0.0.1"), 9999, IPAddress.Any, 443);
        var feature = new TurboHttpConnectionFeature(info);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), feature.RemoteIpAddress);
        Assert.Equal(9999, feature.RemotePort);
    }

    [Fact(Timeout = 5000)]
    public void LocalEndpoint_should_delegate_to_connection_info()
    {
        var info = new TurboConnectionInfo("c", IPAddress.Loopback, 0, IPAddress.Parse("192.168.1.1"), 8080);
        var feature = new TurboHttpConnectionFeature(info);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), feature.LocalIpAddress);
        Assert.Equal(8080, feature.LocalPort);
    }
}
