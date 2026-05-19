using System.Net;
using System.Net.Security;
using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3SniTlsEnforcementSpec
{
    private static RequestEndpoint ToEndpoint(Uri uri, Version version)
    {
        return new RequestEndpoint
        {
            Host = uri.Host,
            Port = (ushort)(uri.IsDefaultPort ? 0 : uri.Port),
            Scheme = uri.Scheme,
            Version = version
        };
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_CarryHostname_When_Http3QuicOptionsCreated()
    {
        var uri = new Uri("https://example.com/path");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.Equal("example.com", quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_MatchRequestHost_When_CustomHostUsed()
    {
        var uri = new Uri("https://my-server.example.org:8443/api");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.Equal("my-server.example.org", quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_AcceptNullHost_In_Options()
    {
        // Verify QuicTransportOptions can be created with null host
        // (validation happens at connection time in QuicClientProvider)
        var quicOptions = new QuicTransportOptions { Host = null!, Port = 443 };
        Assert.Null(quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_AcceptEmptyHost_In_Options()
    {
        // Verify QuicTransportOptions can be created with empty host
        // (validation happens at connection time in QuicClientProvider)
        var quicOptions = new QuicTransportOptions { Host = "", Port = 443 };
        Assert.Empty(quicOptions.Host);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    public void Should_AcceptIpAddress_When_UsedAsHost()
    {
        var uri = new Uri("https://192.168.1.1:443/");
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
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
        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.NotNull(quicOptions.ServerCertificateValidationCallback);

        // Invoke the callback to verify it's the same one
        quicOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None);
        Assert.True(callbackInvoked);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3.2")]
    [InlineData("https://example.com/", "example.com")]
    [InlineData("https://sub.domain.example.com/", "sub.domain.example.com")]
    [InlineData("https://localhost:4433/", "localhost")]
    [InlineData("https://xn--nxasmq6b.example.com/", "xn--nxasmq6b.example.com")]
    public void Should_PreserveHostname_When_VariousHostsUsed(string uriString, string expectedHost)
    {
        var uri = new Uri(uriString);
        var clientOptions = new TurboClientOptions();

        var result = OptionsFactory.Build(ToEndpoint(uri, HttpVersion.Version30), clientOptions);

        var quicOptions = Assert.IsType<QuicTransportOptions>(result);
        Assert.Equal(expectedHost, quicOptions.Host);
    }
}