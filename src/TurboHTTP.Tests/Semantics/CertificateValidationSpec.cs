using System.Net;
using System.Net.Security;
using Servus.Akka.Transport;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Semantics;

public sealed class CertificateValidationSpec
{
    private static RequestEndpoint ToEndpoint(Uri uri)
    {
        return new RequestEndpoint
        {
            Host = uri.Host,
            Port = (ushort)(uri.IsDefaultPort ? 0 : uri.Port),
            Scheme = uri.Scheme,
            Version = HttpVersion.Version11
        };
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void DefaultOptions_should_enable_validation()
    {
        var options = new TurboClientOptions();

        // Default callback rejects certificates with policy errors
        Assert.NotNull(options.ServerCertificateValidationCallback);
        Assert.False(options.DangerousAcceptAnyServerCertificate);

        // Valid certificate accepted
        Assert.True(options.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None));

        // Certificate with name mismatch rejected
        Assert.False(options.ServerCertificateValidationCallback!(null!, null, null,
            SslPolicyErrors.RemoteCertificateNameMismatch));

        // Certificate with chain error rejected
        Assert.False(options.ServerCertificateValidationCallback!(null!, null, null,
            SslPolicyErrors.RemoteCertificateChainErrors));

        // Certificate not available rejected
        Assert.False(options.ServerCertificateValidationCallback!(null!, null, null,
            SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void CustomCallback_should_be_invoked()
    {
        var callbackInvoked = false;
        RemoteCertificateValidationCallback customCallback = (_, _, _, errors) =>
        {
            callbackInvoked = true;
            return errors is SslPolicyErrors.None;
        };

        var options = new TurboClientOptions
        {
            ServerCertificateValidationCallback = customCallback,
        };

        // The effective callback should be the custom one (DangerousAcceptAny is false)
        var effective = options.EffectiveServerCertificateValidationCallback;
        Assert.NotNull(effective);

        effective(null!, null, null, SslPolicyErrors.None);
        Assert.True(callbackInvoked);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void DangerousAcceptAny_should_disable_validation()
    {
        var customCallbackInvoked = false;
        var options = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
            ServerCertificateValidationCallback = (_, _, _, _) =>
            {
                customCallbackInvoked = true;
                return false;
            },
        };

        var effective = options.EffectiveServerCertificateValidationCallback;
        Assert.NotNull(effective);

        // Should accept any certificate regardless of errors
        Assert.True(effective(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.True(effective(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(effective(null!, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));

        // Custom callback should NOT have been invoked — DangerousAcceptAny takes precedence
        Assert.False(customCallbackInvoked);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void EffectiveCallback_should_propagate_to_tls_options()
    {
        var options = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
        };

        var uri = new Uri("https://example.com/path");
        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);

        var tlsOptions = Assert.IsType<TlsTransportOptions>(tcpOptions);
        Assert.NotNull(tlsOptions.ServerCertificateValidationCallback);

        // DangerousAcceptAny was set, so TlsTransportOptions callback should accept anything
        Assert.True(tlsOptions.ServerCertificateValidationCallback!(null!, null, null,
            SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void DefaultEffectiveCallback_should_reject_invalid_certs()
    {
        var options = new TurboClientOptions();

        var uri = new Uri("https://example.com/");
        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);

        var tlsOptions = Assert.IsType<TlsTransportOptions>(tcpOptions);
        Assert.NotNull(tlsOptions.ServerCertificateValidationCallback);

        // Default should accept only SslPolicyErrors.None
        Assert.True(tlsOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None));
        Assert.False(tlsOptions.ServerCertificateValidationCallback!(null!, null, null,
            SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void HttpUri_should_not_produce_tls_options()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("http://example.com/");

        var tcpOptions = OptionsFactory.Build(ToEndpoint(uri), options);

        Assert.IsType<TcpTransportOptions>(tcpOptions);
        Assert.IsNotType<TlsTransportOptions>(tcpOptions);
    }
}