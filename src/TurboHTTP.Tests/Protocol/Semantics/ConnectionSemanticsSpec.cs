using System.Net;
using TurboHTTP.Protocol.Semantics;

namespace TurboHTTP.Tests.Protocol.Semantics;

public sealed class ConnectionSemanticsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void IsPersistent_should_default_false_on_HTTP10_without_keepalive()
    {
        Assert.False(ConnectionSemantics.IsPersistent(new HeaderCollection(), HttpVersion.Version10));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void IsPersistent_should_be_true_on_HTTP10_with_keepalive()
    {
        var h = new HeaderCollection { { "Connection", "keep-alive" } };
        Assert.True(ConnectionSemantics.IsPersistent(h, HttpVersion.Version10));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void IsPersistent_should_default_true_on_HTTP11_without_close()
    {
        Assert.True(ConnectionSemantics.IsPersistent(new HeaderCollection(), HttpVersion.Version11));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9112-9.3")]
    public void IsPersistent_should_be_false_on_HTTP11_with_connection_close()
    {
        var h = new HeaderCollection { { "Connection", "close" } };
        Assert.False(ConnectionSemantics.IsPersistent(h, HttpVersion.Version11));
    }

    [Theory(Timeout = 5000)]
    [InlineData("Connection"), InlineData("Keep-Alive"), InlineData("Transfer-Encoding")]
    [InlineData("TE"), InlineData("Upgrade"), InlineData("Proxy-Authenticate")]
    [InlineData("Proxy-Authorization"), InlineData("Trailer")]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void IsHopByHop_should_detect_standard_hop_by_hop_headers(string name)
    {
        Assert.True(ConnectionSemantics.IsHopByHop(name));
    }

    [Theory(Timeout = 5000)]
    [InlineData("Content-Length"), InlineData("Host"), InlineData("User-Agent")]
    [Trait("RFC", "RFC9110-7.6.1")]
    public void IsHopByHop_should_return_false_for_end_to_end_header(string name)
    {
        Assert.False(ConnectionSemantics.IsHopByHop(name));
    }
}