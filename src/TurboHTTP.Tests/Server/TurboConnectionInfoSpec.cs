using System.Net;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboConnectionInfoSpec
{
    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_store_connection_id()
    {
        var info = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Loopback, 443);
        Assert.Equal("conn-1", info.Id);
    }

    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_store_remote_endpoint()
    {
        var info = new TurboConnectionInfo("conn-1", IPAddress.Parse("192.168.1.1"), 54321, IPAddress.Loopback, 443);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), info.RemoteIpAddress);
        Assert.Equal(54321, info.RemotePort);
    }

    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_store_local_endpoint()
    {
        var info = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Parse("10.0.0.1"), 8080);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), info.LocalIpAddress);
        Assert.Equal(8080, info.LocalPort);
    }

    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_allow_null_addresses()
    {
        var info = new TurboConnectionInfo("conn-1", null, 0, null, 0);
        Assert.Null(info.RemoteIpAddress);
        Assert.Null(info.LocalIpAddress);
    }
}