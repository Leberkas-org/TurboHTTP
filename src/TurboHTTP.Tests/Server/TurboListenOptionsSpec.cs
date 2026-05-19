using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboListenOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void TurboListenOptions_should_store_address_and_port()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 5001);
        Assert.Equal(IPAddress.Loopback, options.Address);
        Assert.Equal((ushort)5001, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void TurboListenOptions_should_default_protocols_to_Http1AndHttp2()
    {
        var options = new TurboListenOptions(IPAddress.Any, 80);
        Assert.Equal(HttpProtocols.Http1AndHttp2, options.Protocols);
    }

    [Fact(Timeout = 5000)]
    public void TurboListenOptions_should_not_be_https_by_default()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 80);
        Assert.False(options.IsHttps);
        Assert.Null(options.HttpsOptions);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_no_args_should_enable_https()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps();
        Assert.True(options.IsHttps);
        Assert.NotNull(options.HttpsOptions);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_with_certificate_should_set_server_certificate()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps(cert);
        Assert.Same(cert, options.HttpsOptions!.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_with_path_should_set_certificate_path()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps("cert.pfx", "password");
        Assert.Equal("cert.pfx", options.HttpsOptions!.CertificatePath);
        Assert.Equal("password", options.HttpsOptions.CertificatePassword);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_with_path_and_null_password_should_set_null_password()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps("cert.pem");
        Assert.Equal("cert.pem", options.HttpsOptions!.CertificatePath);
        Assert.Null(options.HttpsOptions.CertificatePassword);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_with_configure_action_should_apply_callback()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps(https =>
        {
            https.EnabledSslProtocols = SslProtocols.Tls13;
        });
        Assert.Equal(SslProtocols.Tls13, options.HttpsOptions!.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_with_cert_and_configure_should_set_both()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps(cert, https =>
        {
            https.EnabledSslProtocols = SslProtocols.Tls12;
        });
        Assert.Same(cert, options.HttpsOptions!.ServerCertificate);
        Assert.Equal(SslProtocols.Tls12, options.HttpsOptions.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_with_path_and_configure_should_set_both()
    {
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps("cert.pfx", "pw", https =>
        {
            https.HandshakeTimeout = TimeSpan.FromSeconds(30);
        });
        Assert.Equal("cert.pfx", options.HttpsOptions!.CertificatePath);
        Assert.Equal("pw", options.HttpsOptions.CertificatePassword);
        Assert.Equal(TimeSpan.FromSeconds(30), options.HttpsOptions.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void UseHttps_called_twice_should_use_last_configuration()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboListenOptions(IPAddress.Loopback, 443);
        options.UseHttps("first.pfx");
        options.UseHttps(cert);
        Assert.Same(cert, options.HttpsOptions!.ServerCertificate);
        Assert.Null(options.HttpsOptions.CertificatePath);
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
    }
}
