using System.Net;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class TurboServerOptionsBindingSpec
{
    [Fact(Timeout = 5000)]
    public void Urls_should_be_empty_by_default()
    {
        var options = new TurboServerOptions();
        Assert.Empty(options.Urls);
    }

    [Fact(Timeout = 5000)]
    public void Urls_should_accept_url_strings()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("http://localhost:5000");
        options.Urls.Add("https://localhost:5001");
        Assert.Equal(2, options.Urls.Count);
    }

    [Fact(Timeout = 5000)]
    public void Listen_should_add_listen_options_with_address_and_port()
    {
        var options = new TurboServerOptions();
        options.Listen(IPAddress.Loopback, 5000);
        Assert.Single(options.ListenOptions);
        Assert.Equal(IPAddress.Loopback, options.ListenOptions[0].Address);
        Assert.Equal((ushort)5000, options.ListenOptions[0].Port);
    }

    [Fact(Timeout = 5000)]
    public void Listen_with_configure_should_apply_callback()
    {
        var options = new TurboServerOptions();
        options.Listen(IPAddress.Any, 443, listen =>
        {
            listen.Protocols = HttpProtocols.Http2;
        });
        Assert.Equal(HttpProtocols.Http2, options.ListenOptions[0].Protocols);
    }

    [Fact(Timeout = 5000)]
    public void ListenLocalhost_should_use_loopback_address()
    {
        var options = new TurboServerOptions();
        options.ListenLocalhost(8080);
        Assert.Equal(IPAddress.Loopback, options.ListenOptions[0].Address);
        Assert.Equal((ushort)8080, options.ListenOptions[0].Port);
    }

    [Fact(Timeout = 5000)]
    public void ListenLocalhost_with_configure_should_apply_callback()
    {
        var options = new TurboServerOptions();
        options.ListenLocalhost(443, listen =>
        {
            listen.UseHttps();
        });
        Assert.True(options.ListenOptions[0].IsHttps);
    }

    [Fact(Timeout = 5000)]
    public void ListenAnyIP_should_use_any_address()
    {
        var options = new TurboServerOptions();
        options.ListenAnyIP(80);
        Assert.Equal(IPAddress.Any, options.ListenOptions[0].Address);
        Assert.Equal((ushort)80, options.ListenOptions[0].Port);
    }

    [Fact(Timeout = 5000)]
    public void ListenAnyIP_with_configure_should_apply_callback()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ListenAnyIP(443, listen =>
        {
            listen.UseHttps(cert);
        });
        Assert.True(options.ListenOptions[0].IsHttps);
        Assert.Same(cert, options.ListenOptions[0].HttpsOptions!.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void ConfigureHttpsDefaults_should_store_defaults_callback()
    {
        var options = new TurboServerOptions();
        options.ConfigureHttpsDefaults(https =>
        {
            https.CertificatePath = "default.pfx";
        });
        Assert.NotNull(options.HttpsDefaultsCallback);
    }

    [Fact(Timeout = 5000)]
    public void Multiple_listen_calls_should_accumulate()
    {
        var options = new TurboServerOptions();
        options.ListenLocalhost(5000);
        options.ListenLocalhost(5001);
        options.ListenAnyIP(8080);
        Assert.Equal(3, options.ListenOptions.Count);
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
    }
}
