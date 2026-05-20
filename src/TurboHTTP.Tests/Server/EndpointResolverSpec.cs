using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Servus.Akka.Transport;
using TurboHTTP.Routing;
using TurboHTTP.Server;

namespace TurboHTTP.Tests.Server;

public sealed class EndpointResolverSpec
{
    [Fact(Timeout = 5000)]
    public void Resolve_should_produce_tcp_binding_for_http_listen()
    {
        var options = new TurboServerOptions();
        options.ListenLocalhost(5000);

        var bindings = new EndpointResolver().Resolve(options);

        Assert.Single(bindings);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Equal("127.0.0.1", tcp.Host);
        Assert.Equal((ushort)5000, tcp.Port);
        Assert.Null(tcp.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_produce_tcp_binding_with_cert_for_https_listen()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ListenLocalhost(5001, listen =>
        {
            listen.UseHttps(cert);
        });

        var bindings = new EndpointResolver().Resolve(options);

        Assert.Single(bindings);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Same(cert, tcp.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_set_alpn_from_protocols()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ListenLocalhost(5001, listen =>
        {
            listen.UseHttps(cert);
            listen.Protocols = HttpProtocols.Http2;
        });

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.NotNull(tcp.ApplicationProtocols);
        Assert.Single(tcp.ApplicationProtocols);
        Assert.Equal(SslApplicationProtocol.Http2, tcp.ApplicationProtocols[0]);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_produce_two_bindings_when_http3_is_set()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ListenAnyIP(443, listen =>
        {
            listen.UseHttps(cert);
            listen.Protocols = HttpProtocols.Http1AndHttp2 | HttpProtocols.Http3;
        });

        var bindings = new EndpointResolver().Resolve(options);

        Assert.Equal(2, bindings.Count);
        Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.IsType<QuicListenerOptions>(bindings[1].Options);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_share_certificate_between_tcp_and_quic_bindings()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ListenAnyIP(443, listen =>
        {
            listen.UseHttps(cert);
            listen.Protocols = HttpProtocols.Http1AndHttp2 | HttpProtocols.Http3;
        });

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = (TcpListenerOptions)bindings[0].Options;
        var quic = (QuicListenerOptions)bindings[1].Options;
        Assert.Same(cert, tcp.ServerCertificate);
        Assert.Same(cert, quic.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_apply_https_defaults_to_endpoints_with_use_https()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ConfigureHttpsDefaults(https =>
        {
            https.ServerCertificate = cert;
        });
        options.ListenLocalhost(443, listen =>
        {
            listen.UseHttps();
        });

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Same(cert, tcp.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_not_apply_https_defaults_to_plain_http_endpoints()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ConfigureHttpsDefaults(https =>
        {
            https.ServerCertificate = cert;
        });
        options.ListenLocalhost(80);

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Null(tcp.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_parse_http_url()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("http://localhost:5000");

        var bindings = new EndpointResolver().Resolve(options);

        Assert.Single(bindings);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Equal("127.0.0.1", tcp.Host);
        Assert.Equal((ushort)5000, tcp.Port);
        Assert.Null(tcp.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_parse_https_url_with_defaults()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ConfigureHttpsDefaults(https =>
        {
            https.ServerCertificate = cert;
        });
        options.Urls.Add("https://localhost:5001");

        var bindings = new EndpointResolver().Resolve(options);

        Assert.Single(bindings);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Same(cert, tcp.ServerCertificate);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_parse_wildcard_host_as_any_ip()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("http://*:8080");

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Equal("0.0.0.0", tcp.Host);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_parse_ipv6_url()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("http://[::1]:5000");

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Equal("::1", tcp.Host);
        Assert.Equal((ushort)5000, tcp.Port);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_throw_for_https_url_without_certificate()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("https://localhost:5001");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EndpointResolver().Resolve(options));
        Assert.Contains("No server certificate configured", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_throw_for_invalid_url()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("not-a-url");

        Assert.Throws<FormatException>(() =>
            new EndpointResolver().Resolve(options));
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_throw_for_unsupported_scheme()
    {
        var options = new TurboServerOptions();
        options.Urls.Add("ftp://localhost:21");

        Assert.Throws<NotSupportedException>(() =>
            new EndpointResolver().Resolve(options));
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_throw_for_http3_without_https()
    {
        var options = new TurboServerOptions();
        options.ListenLocalhost(443, listen =>
        {
            listen.Protocols = HttpProtocols.Http3;
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EndpointResolver().Resolve(options));
        Assert.Contains("HTTP/3 requires HTTPS", ex.Message);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_throw_for_missing_cert_file()
    {
        var options = new TurboServerOptions();
        options.ListenLocalhost(443, listen =>
        {
            listen.UseHttps("nonexistent.pfx", "pw");
        });

        Assert.Throws<FileNotFoundException>(() =>
            new EndpointResolver().Resolve(options));
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_include_raw_bind_endpoints()
    {
        var options = new TurboServerOptions();
        options.BindTcp("0.0.0.0", 9090);
        options.ListenLocalhost(5000);

        var bindings = new EndpointResolver().Resolve(options);

        Assert.Equal(2, bindings.Count);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_set_handshake_timeout_on_tcp_options()
    {
        using var cert = CreateSelfSignedCert();
        var options = new TurboServerOptions();
        options.ListenLocalhost(443, listen =>
        {
            listen.UseHttps(cert, https =>
            {
                https.HandshakeTimeout = TimeSpan.FromSeconds(30);
            });
        });

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Equal(TimeSpan.FromSeconds(30), tcp.HandshakeTimeout);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_set_client_cert_callback_on_tcp_options()
    {
        using var cert = CreateSelfSignedCert();
        RemoteCertificateValidationCallback callback = (_, _, _, _) => true;
        var options = new TurboServerOptions();
        options.ListenLocalhost(443, listen =>
        {
            listen.UseHttps(cert, https =>
            {
                https.ClientCertificateValidationCallback = callback;
            });
        });

        var bindings = new EndpointResolver().Resolve(options);
        var tcp = Assert.IsType<TcpListenerOptions>(bindings[0].Options);
        Assert.Same(callback, tcp.ClientCertificateValidationCallback);
    }

    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
    }
}
