using System;
using System.Net;
using System.Net.Security;
using System.Threading.Tasks;
using TurboHttp.Client;
using TurboHttp.Transport;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// RFC 9114 §3.2 — Clients MUST include the SNI extension in the TLS handshake
/// for HTTP/3 QUIC connections. These tests verify that the SNI hostname is properly
/// propagated and that missing SNI is rejected.
/// </summary>
public sealed class SniTlsEnforcementTests
{
    [Fact(DisplayName = "RFC9114-3.2-SNI-001: QuicOptions carries hostname for SNI")]
    public void Should_CarryHostname_When_Http3QuicOptionsCreated()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("example.com", quicOptions.Host);
    }

    [Fact(DisplayName = "RFC9114-3.2-SNI-002: QuicOptions hostname matches request URI host")]
    public void Should_MatchRequestHost_When_CustomHostUsed()
    {
        var uri = new Uri("https://my-server.example.org:8443/api");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("my-server.example.org", quicOptions.Host);
    }

    [Fact(DisplayName = "RFC9114-3.2-SNI-003: QuicClientProvider rejects null host")]
    public async Task Should_ThrowInvalidOperation_When_HostIsNull()
    {
        var quicOptions = new QuicOptions { Host = null!, Port = 443 };

#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(quicOptions);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync());
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
        Assert.Contains("RFC 9114", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-3.2-SNI-004: QuicClientProvider rejects empty host")]
    public async Task Should_ThrowInvalidOperation_When_HostIsEmpty()
    {
        var quicOptions = new QuicOptions { Host = "", Port = 443 };

#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(quicOptions);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync());
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
        Assert.Contains("RFC 9114", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-3.2-SNI-005: ALPN protocol h3 is set for HTTP/3")]
    public void Should_IncludeH3Alpn_When_QuicOptionsCreated()
    {
        var quicOptions = new QuicOptions { Host = "example.com", Port = 443 };

        Assert.Single(quicOptions.ApplicationProtocols);
        Assert.Equal(new SslApplicationProtocol("h3"), quicOptions.ApplicationProtocols[0]);
    }

    [Fact(DisplayName = "RFC9114-3.2-SNI-006: IP address host is valid for QUIC SNI")]
    public void Should_AcceptIpAddress_When_UsedAsHost()
    {
        var uri = new Uri("https://192.168.1.1:443/");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("192.168.1.1", quicOptions.Host);
    }

    [Fact(DisplayName = "RFC9114-3.2-SNI-007: Certificate validation callback propagated for SNI verification")]
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

    [Theory(DisplayName = "RFC9114-3.2-SNI-008: Various valid hostnames preserved for SNI")]
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
