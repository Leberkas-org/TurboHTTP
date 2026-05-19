using System.Net;
using Microsoft.AspNetCore.Http;
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

    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_support_late_binding_remote_endpoint()
    {
        var info = new TurboConnectionInfo("conn-1", null, 0, null, 8080);

        Assert.Null(info.RemoteIpAddress);
        Assert.Equal(0, info.RemotePort);

        info.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        info.RemotePort = 54321;

        Assert.Equal(IPAddress.Parse("192.168.1.100"), info.RemoteIpAddress);
        Assert.Equal(54321, info.RemotePort);
    }

    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_be_assignable_to_ConnectionInfo()
    {
        var info = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Loopback, 443);
        ConnectionInfo baseRef = info;
        Assert.Equal("conn-1", baseRef.Id);
        Assert.Equal(IPAddress.Loopback, baseRef.RemoteIpAddress);
        Assert.Equal(12345, baseRef.RemotePort);
    }

    [Fact(Timeout = 5000)]
    public void TurboConnectionInfo_should_return_null_client_certificate()
    {
        var info = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Loopback, 443);
        Assert.Null(info.ClientCertificate);
    }

    [Fact(Timeout = 5000)]
    public async Task TurboConnectionInfo_should_return_null_from_GetClientCertificateAsync()
    {
        var info = new TurboConnectionInfo("conn-1", IPAddress.Loopback, 12345, IPAddress.Loopback, 443);
        var cert = await info.GetClientCertificateAsync(TestContext.Current.CancellationToken);
        Assert.Null(cert);
    }
}