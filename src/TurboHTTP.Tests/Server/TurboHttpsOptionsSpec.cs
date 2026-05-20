using System.Security.Authentication;
using Servus.Akka.Transport;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboHttpsOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_ssl_protocols_to_none()
    {
        var options = new TurboHttpsOptions();
        Assert.Equal(SslProtocols.None, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_handshake_timeout_to_10_seconds()
    {
        var options = new TurboHttpsOptions();
        Assert.Equal(TimeSpan.FromSeconds(10), options.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_server_certificate_to_null()
    {
        var options = new TurboHttpsOptions();
        Assert.Null(options.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_certificate_path_to_null()
    {
        var options = new TurboHttpsOptions();
        Assert.Null(options.CertificatePath);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_certificate_password_to_null()
    {
        var options = new TurboHttpsOptions();
        Assert.Null(options.CertificatePassword);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_client_cert_callback_to_null()
    {
        var options = new TurboHttpsOptions();
        Assert.Null(options.ClientCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_allow_setting_ssl_protocols()
    {
        var options = new TurboHttpsOptions
        {
            EnabledSslProtocols = SslProtocols.Tls13
        };
        Assert.Equal(SslProtocols.Tls13, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_allow_setting_handshake_timeout()
    {
        var options = new TurboHttpsOptions
        {
            HandshakeTimeout = TimeSpan.FromSeconds(30)
        };
        Assert.Equal(TimeSpan.FromSeconds(30), options.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_client_certificate_mode_to_no_certificate()
    {
        var options = new TurboHttpsOptions();
        Assert.Equal(ClientCertificateMode.NoCertificate, options.ClientCertificateMode);
    }

    [Fact(Timeout = 5000)]
    public void TurboHttpsOptions_should_default_server_certificate_selector_to_null()
    {
        var options = new TurboHttpsOptions();
        Assert.Null(options.ServerCertificateSelector);
    }
}
