using System.Net;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class TransportSelectionSpec
{
    private static RequestEndpoint ToEndpoint(Uri uri, Version? version)
    {
        return new RequestEndpoint
        {
            Host = uri.Host,
            Port = (ushort)(uri.IsDefaultPort ? 0 : uri.Port),
            Scheme = uri.Scheme,
            Version = version ?? HttpVersion.Unknown
        };
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceQuicOptions_When_Http3Version()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal("example.com", quicOptions.Host);
        Assert.Equal(443, quicOptions.Port);
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceTcpOptions_When_Http11Version()
    {
        var uri = new Uri("http://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version11), clientOptions);

        Assert.IsType<TcpOptions>(result);
        Assert.IsNotType<TlsOptions>(result);
        Assert.IsNotType<QuicOptions>(result);
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceTlsOptions_When_Http11AndHttps()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version11), clientOptions);

        Assert.IsType<TlsOptions>(result);
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_PropagateCertCallback_When_Http3()
    {
        var uri = new Uri("https://example.com/");
        var clientOptions = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
        };

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_FallBackToScheme_When_NullVersion()
    {
        var httpsUri = new Uri("https://example.com/");
        var httpUri = new Uri("http://example.com/");
        var clientOptions = new TurboClientOptions();

        var httpsResult = OptionsFactory.Build(ToEndpoint(httpsUri, null), clientOptions);
        var httpResult = OptionsFactory.Build(ToEndpoint(httpUri, null), clientOptions);

        Assert.IsType<TlsOptions>(httpsResult);
        Assert.IsType<TcpOptions>(httpResult);
        Assert.IsNotType<QuicOptions>(httpsResult);
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceQuicOptions_When_Http3EvenWithHttpScheme()
    {
        var uri = new Uri("http://example.com:4433/");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicOptions>(result);
        Assert.Equal(4433, quicOptions.Port);
    }

    [Fact]
    [Trait("RFC", "RFC9114-3.2")]
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
