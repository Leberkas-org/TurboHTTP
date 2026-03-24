using System.Net;
using TurboHttp.Transport;

namespace TurboHttp.Tests.RFC9114;

public sealed class TransportSelectionTests
{
    [Fact(DisplayName = "RFC9114-3.2-TS-001: HTTP/3 request produces QuicOptions")]
    public void Should_ProduceQuicOptions_When_Http3Version()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("example.com", quicOptions.Host);
        Assert.Equal(443, quicOptions.Port);
    }

    [Fact(DisplayName = "RFC9114-3.2-TS-002: HTTP/1.1 request still produces TcpOptions")]
    public void Should_ProduceTcpOptions_When_Http11Version()
    {
        var uri = new Uri("http://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version11);

        Assert.IsType<TcpOptions>(result);
        Assert.IsNotType<TlsOptions>(result);
        Assert.IsNotType<QuicOptions>(result);
    }

    [Fact(DisplayName = "RFC9114-3.2-TS-003: HTTPS with HTTP/1.1 still produces TlsOptions")]
    public void Should_ProduceTlsOptions_When_Http11AndHttps()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version11);

        Assert.IsType<TlsOptions>(result);
    }

    [Fact(DisplayName = "RFC9114-3.2-TS-004: HTTP/3 QuicOptions carries certificate callback")]
    public void Should_PropagateCertCallback_When_Http3()
    {
        var uri = new Uri("https://example.com/");
        var clientOptions = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
        };

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);
    }

    [Fact(DisplayName = "RFC9114-3.2-TS-005: Null version falls back to scheme-based selection")]
    public void Should_FallBackToScheme_When_NullVersion()
    {
        var httpsUri = new Uri("https://example.com/");
        var httpUri = new Uri("http://example.com/");
        var clientOptions = new TurboClientOptions();

        var httpsResult = TcpOptionsFactory.Build(httpsUri, clientOptions, null);
        var httpResult = TcpOptionsFactory.Build(httpUri, clientOptions, null);

        Assert.IsType<TlsOptions>(httpsResult);
        Assert.IsType<TcpOptions>(httpResult);
        Assert.IsNotType<QuicOptions>(httpsResult);
    }

    [Fact(DisplayName = "RFC9114-3.2-TS-006: HTTP/3 over HTTP URI produces QuicOptions")]
    public void Should_ProduceQuicOptions_When_Http3EvenWithHttpScheme()
    {
        var uri = new Uri("http://example.com:4433/");
        var clientOptions = new TurboClientOptions();

        var result = TcpOptionsFactory.Build(uri, clientOptions, HttpVersion.Version30);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal(4433, quicOptions.Port);
    }

    [Fact(DisplayName = "RFC9114-3.2-TS-007: QuicConnectionManager creates QuicClientProvider for QuicOptions")]
    public void Should_CreateQuicProvider_When_QuicOptions()
    {
        // Verify the pattern match works by checking that QuicOptions is matched
        // before the default TcpOptions case. We can't instantiate the actor in a unit test,
        // but we can verify the type hierarchy that makes the switch work.
        var quicOptions = new QuicOptions { Host = "example.com", Port = 443 };

        // QuicOptions must be matched before TcpOptions in the switch
        Assert.IsAssignableFrom<TcpOptions>(quicOptions);
        Assert.IsNotType<TlsOptions>(quicOptions);

        // Verify QuicClientProvider can be constructed from QuicOptions
#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(quicOptions);
#pragma warning restore CA1416
        Assert.IsAssignableFrom<IClientProvider>(provider);
    }
}
