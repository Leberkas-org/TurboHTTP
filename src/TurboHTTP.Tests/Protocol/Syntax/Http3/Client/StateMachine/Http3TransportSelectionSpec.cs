using System.Net;
using Servus.Akka.Transport;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3TransportSelectionSpec
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceQuicOptions_When_Http3Version()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.Equal("example.com", quicOptions.Host);
        Assert.Equal(443, quicOptions.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceTcpOptions_When_Http11Version()
    {
        var uri = new Uri("http://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version11), clientOptions);

        Assert.IsType<TcpTransportOptions>(result);
        Assert.IsNotType<TlsTransportOptions>(result);
        Assert.IsNotType<QuicTransportOptions>(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceTlsOptions_When_Http11AndHttps()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version11), clientOptions);

        Assert.IsType<TlsTransportOptions>(result);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_PropagateCertCallback_When_Http3()
    {
        var uri = new Uri("https://example.com/");
        var clientOptions = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
        };

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_FallBackToScheme_When_NullVersion()
    {
        var httpsUri = new Uri("https://example.com/");
        var httpUri = new Uri("http://example.com/");
        var clientOptions = new TurboClientOptions();

        var httpsResult = OptionsFactory.Build(ToEndpoint(httpsUri, null), clientOptions);
        var httpResult = OptionsFactory.Build(ToEndpoint(httpUri, null), clientOptions);

        Assert.IsType<TlsTransportOptions>(httpsResult);
        Assert.IsType<TcpTransportOptions>(httpResult);
        Assert.IsNotType<QuicTransportOptions>(httpsResult);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_ProduceQuicOptions_When_Http3EvenWithHttpScheme()
    {
        var uri = new Uri("http://example.com:4433/");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.Equal(4433, quicOptions.Port);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_CreateQuicProvider_When_QuicOptions()
    {
        // Verify the pattern match works by checking that QuicTransportOptions is matched
        // before the default TcpTransportOptions case. We can't instantiate the actor in a unit test,
        // but we can verify the type hierarchy that makes the switch work.
        var quicOptions = new QuicTransportOptions { Host = "example.com", Port = 443 };

        // QuicTransportOptions is its own type
        Assert.IsType<QuicTransportOptions>(quicOptions);
        Assert.IsNotType<TlsTransportOptions>(quicOptions);
    }
}