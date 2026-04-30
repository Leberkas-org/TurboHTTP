using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using Servus.Akka.Transport;

namespace Servus.Akka.Tests.Transport;

public sealed class ConnectionInfoSpec
{
    [Fact(Timeout = 5000)]
    public void Should_store_all_properties()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);
        var sslProtocol = SslProtocols.Tls13;
        var appProtocol = SslApplicationProtocol.Http2;

        var info = new ConnectionInfo(local, remote, sslProtocol, appProtocol);

        Assert.Equal(local, info.Local);
        Assert.Equal(remote, info.Remote);
        Assert.Equal(sslProtocol, info.NegotiatedSslProtocol);
        Assert.Equal(appProtocol, info.NegotiatedApplicationProtocol);
    }

    [Fact(Timeout = 5000)]
    public void Should_support_null_ssl_properties()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 8080);
        var remote = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 80);

        var info = new ConnectionInfo(local, remote, NegotiatedSslProtocol: null, NegotiatedApplicationProtocol: null);

        Assert.Equal(local, info.Local);
        Assert.Equal(remote, info.Remote);
        Assert.Null(info.NegotiatedSslProtocol);
        Assert.Null(info.NegotiatedApplicationProtocol);
    }

    [Fact(Timeout = 5000)]
    public void Equality_should_work_for_records()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);

        var info1 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);
        var info2 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);

        Assert.Equal(info1, info2);
        Assert.Equal(info1.GetHashCode(), info2.GetHashCode());
    }

    [Fact(Timeout = 5000)]
    public void Inequality_should_work_for_different_local_endpoint()
    {
        var local1 = new IPEndPoint(IPAddress.Loopback, 5000);
        var local2 = new IPEndPoint(IPAddress.Loopback, 5001);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);

        var info1 = new ConnectionInfo(local1, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);
        var info2 = new ConnectionInfo(local2, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);

        Assert.NotEqual(info1, info2);
    }

    [Fact(Timeout = 5000)]
    public void Inequality_should_work_for_different_remote_endpoint()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote1 = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);
        var remote2 = new IPEndPoint(IPAddress.Parse("192.168.1.2"), 443);

        var info1 = new ConnectionInfo(local, remote1, SslProtocols.Tls13, SslApplicationProtocol.Http2);
        var info2 = new ConnectionInfo(local, remote2, SslProtocols.Tls13, SslApplicationProtocol.Http2);

        Assert.NotEqual(info1, info2);
    }

    [Fact(Timeout = 5000)]
    public void Inequality_should_work_for_different_ssl_protocol()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);

        var info1 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);
        var info2 = new ConnectionInfo(local, remote, SslProtocols.Tls12, SslApplicationProtocol.Http2);

        Assert.NotEqual(info1, info2);
    }

    [Fact(Timeout = 5000)]
    public void Inequality_should_work_for_different_app_protocol()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);

        var info1 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);
        var info2 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http11);

        Assert.NotEqual(info1, info2);
    }

    [Fact(Timeout = 5000)]
    public void Should_support_mixed_null_ssl_fields()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);

        var info1 = new ConnectionInfo(local, remote, SslProtocols.Tls13, NegotiatedApplicationProtocol: null);
        var info2 = new ConnectionInfo(local, remote, NegotiatedSslProtocol: null, SslApplicationProtocol.Http2);

        Assert.Equal(SslProtocols.Tls13, info1.NegotiatedSslProtocol);
        Assert.Null(info1.NegotiatedApplicationProtocol);

        Assert.Null(info2.NegotiatedSslProtocol);
        Assert.Equal(SslApplicationProtocol.Http2, info2.NegotiatedApplicationProtocol);
    }

    [Fact(Timeout = 5000)]
    public void Should_work_as_dictionary_key()
    {
        var local = new IPEndPoint(IPAddress.Loopback, 5000);
        var remote = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 443);

        var info1 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);
        var info2 = new ConnectionInfo(local, remote, SslProtocols.Tls13, SslApplicationProtocol.Http2);

        var dict = new Dictionary<ConnectionInfo, string> { { info1, "pooled" } };

        Assert.True(dict.ContainsKey(info2));
        Assert.Equal("pooled", dict[info2]);
    }
}
