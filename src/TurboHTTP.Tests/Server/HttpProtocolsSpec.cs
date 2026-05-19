using System.Net.Security;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class HttpProtocolsSpec
{
    [Fact(Timeout = 5000)]
    public void Http1_should_have_value_1()
    {
        Assert.Equal(1, (int)HttpProtocols.Http1);
    }

    [Fact(Timeout = 5000)]
    public void Http2_should_have_value_2()
    {
        Assert.Equal(2, (int)HttpProtocols.Http2);
    }

    [Fact(Timeout = 5000)]
    public void Http1AndHttp2_should_combine_Http1_and_Http2()
    {
        Assert.Equal(HttpProtocols.Http1 | HttpProtocols.Http2, HttpProtocols.Http1AndHttp2);
    }

    [Fact(Timeout = 5000)]
    public void Http3_should_have_value_4()
    {
        Assert.Equal(4, (int)HttpProtocols.Http3);
    }

    [Fact(Timeout = 5000)]
    public void ToAlpnProtocols_should_return_http11_for_Http1()
    {
        var result = HttpProtocols.Http1.ToAlpnProtocols();
        Assert.Single(result);
        Assert.Equal(SslApplicationProtocol.Http11, result[0]);
    }

    [Fact(Timeout = 5000)]
    public void ToAlpnProtocols_should_return_h2_for_Http2()
    {
        var result = HttpProtocols.Http2.ToAlpnProtocols();
        Assert.Single(result);
        Assert.Equal(SslApplicationProtocol.Http2, result[0]);
    }

    [Fact(Timeout = 5000)]
    public void ToAlpnProtocols_should_return_h2_then_http11_for_Http1AndHttp2()
    {
        var result = HttpProtocols.Http1AndHttp2.ToAlpnProtocols();
        Assert.Equal(2, result.Count);
        Assert.Equal(SslApplicationProtocol.Http2, result[0]);
        Assert.Equal(SslApplicationProtocol.Http11, result[1]);
    }

    [Fact(Timeout = 5000)]
    public void ToAlpnProtocols_should_return_h3_for_Http3()
    {
        var result = HttpProtocols.Http3.ToAlpnProtocols();
        Assert.Single(result);
        Assert.Equal(new SslApplicationProtocol("h3"), result[0]);
    }

    [Fact(Timeout = 5000)]
    public void ToAlpnProtocols_should_return_empty_for_None()
    {
        var result = HttpProtocols.None.ToAlpnProtocols();
        Assert.Empty(result);
    }
}
