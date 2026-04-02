using System.Net;
using System.Net.Security;
using TurboHttp.Transport.Tcp;
using TurboHttp.Transport.Quic;

namespace TurboHttp.Tests.Http3.Connection;

/// <summary>
/// RFC 9114 §3.2 — Clients MUST include the SNI extension in the TLS handshake
/// for HTTP/3 QUIC connections. These tests verify that the SNI hostname is properly
/// propagated and that missing SNI is rejected.
/// </summary>
public sealed class SniTlsEnforcementSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_CarryHostname_When_Http3QuicOptionsCreated()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("example.com", quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_MatchRequestHost_When_CustomHostUsed()
    {
        var uri = new Uri("https://my-server.example.org:8443/api");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("my-server.example.org", quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public async Task Should_ThrowInvalidOperation_When_HostIsNull()
    {
        var quicOptions = new QuicOptions { Host = null!, Port = 443 };

#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(quicOptions);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync(TestContext.Current.CancellationToken));
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
        Assert.Contains("Server Name Indication", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public async Task Should_ThrowInvalidOperation_When_HostIsEmpty()
    {
        var quicOptions = new QuicOptions { Host = "", Port = 443 };

#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(quicOptions);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync(TestContext.Current.CancellationToken));
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
        Assert.Contains("Server Name Indication", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_IncludeH3Alpn_When_QuicOptionsCreated()
    {
        var quicOptions = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Single(quicOptions.ApplicationProtocols);
        Assert.Equal(new SslApplicationProtocol("h3"), quicOptions.ApplicationProtocols[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_AcceptIpAddress_When_UsedAsHost()
    {
        var uri = new Uri("https://192.168.1.1:443/");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("192.168.1.1", quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_PropagateCertCallback_When_Http3WithSni()
    {
        var callbackInvoked = false;
        var clientOptions = new TurboClientOptions
        {
            ServerCertificateValidationCallback = (_, _, _, _) =>
            {
                callbackInvoked = true;
                return true;
            },
        };

        var uri = new Uri("https://secure.example.com/");
        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);

        // Invoke the callback to verify it's the same one
        quicOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None);
        Assert.True(callbackInvoked);
    }

    [Theory]
    [Trait("RFC", "RFC9114-3.2")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("https://sub.domain.example.com/", "sub.domain.example.com")]
    [InlineData("https://localhost:4433/", "localhost")]
    [InlineData("https://xn--nxasmq6b.example.com/", "xn--nxasmq6b.example.com")]
    public void Should_PreserveHostname_When_VariousHostsUsed(string uriString, string expectedHost)
    {
        var uri = new Uri(uriString);
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal(expectedHost, quicOptions.Host);
    }
}
