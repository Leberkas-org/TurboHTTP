using System.Net.Security;
using TurboHttp.Transport.Tcp;
using TurboHttp.Transport.Connection;

namespace TurboHttp.Tests.Semantics;

/// <summary>
/// Tests for RFC 9110 §4.3.4 — certificate validation callbacks on <see cref="TurboClientOptions"/>.
/// Verifies that the default options enforce certificate validation, custom callbacks are
/// propagated through <see cref="TcpOptionsFactory"/>, and <see cref="TurboClientOptions.DangerousAcceptAnyServerCertificate"/>
/// overrides validation.
/// </summary>
public sealed class CertificateValidationSpec
{
    [Fact]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void DefaultOptions_Should_EnableValidation()
    {
        var options = new TurboClientOptions();

        // Default callback rejects certificates with policy errors
        Assert.NotNull(options.ServerCertificateValidationCallback);
        Assert.False(options.DangerousAcceptAnyServerCertificate);

        // Valid certificate accepted
        Assert.True(options.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None));

        // Certificate with name mismatch rejected
        Assert.False(options.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));

        // Certificate with chain error rejected
        Assert.False(options.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));

        // Certificate not available rejected
        Assert.False(options.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [Fact]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void CustomCallback_Should_BeInvoked()
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

        effective!(null!, null, null, SslPolicyErrors.None);
        Assert.True(callbackInvoked);
    }

    [Fact]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void DangerousAcceptAny_Should_DisableValidation()
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
        Assert.True(effective!(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.True(effective!(null!, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(effective!(null!, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));

        // Custom callback should NOT have been invoked — DangerousAcceptAny takes precedence
        Assert.False(customCallbackInvoked);
    }

    [Fact]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void EffectiveCallback_Should_PropagateToTlsOptions()
    {
        var options = new TurboClientOptions
        {
            DangerousAcceptAnyServerCertificate = true,
        };

        var uri = new Uri("https://example.com/path");
        var tcpOptions = TcpOptionsFactory.Build(uri, options);

        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);
        Assert.NotNull(tlsOptions.ServerCertificateValidationCallback);

        // DangerousAcceptAny was set, so TlsOptions callback should accept anything
        Assert.True(tlsOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void DefaultEffectiveCallback_Should_RejectInvalidCerts()
    {
        var options = new TurboClientOptions();

        var uri = new Uri("https://example.com/");
        var tcpOptions = TcpOptionsFactory.Build(uri, options);

        var tlsOptions = Assert.IsType<TlsOptions>(tcpOptions);
        Assert.NotNull(tlsOptions.ServerCertificateValidationCallback);

        // Default should accept only SslPolicyErrors.None
        Assert.True(tlsOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.None));
        Assert.False(tlsOptions.ServerCertificateValidationCallback!(null!, null, null, SslPolicyErrors.RemoteCertificateNameMismatch));
    }

    [Fact]
    [Trait("RFC", "RFC9110-4.3.4")]
    public void HttpUri_Should_NotProduceTlsOptions()
    {
        var options = new TurboClientOptions();
        var uri = new Uri("http://example.com/");

        var tcpOptions = TcpOptionsFactory.Build(uri, options);

        Assert.IsType<TcpOptions>(tcpOptions);
        Assert.IsNotType<TlsOptions>(tcpOptions);
    }
}
