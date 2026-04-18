using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

public sealed class TlsOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_host_from_tcp_options()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Equal("example.com", options.Host);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_port_from_tcp_options()
    {
        var options = new TlsOptions { Host = "example.com", Port = 8443 };

        Assert.Equal(8443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_connect_timeout_from_tcp_options()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ConnectTimeout = TimeSpan.FromSeconds(20)
        };

        Assert.Equal(TimeSpan.FromSeconds(20), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_socket_send_buffer_size_from_tcp_options()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            SocketSendBufferSize = 32768
        };

        Assert.Equal(32768, options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_socket_receive_buffer_size_from_tcp_options()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            SocketReceiveBufferSize = 32768
        };

        Assert.Equal(32768, options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_use_proxy_from_tcp_options()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true
        };

        Assert.True(options.UseProxy);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_proxy_from_tcp_options()
    {
        var proxy = new WebProxy("http://proxy.example.com:8080");
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            Proxy = proxy
        };

        Assert.Same(proxy, options.Proxy);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_inherit_default_proxy_credentials_from_tcp_options()
    {
        var credentials = new NetworkCredential("user", "password");
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            DefaultProxyCredentials = credentials
        };

        Assert.Same(credentials, options.DefaultProxyCredentials);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_null_target_host()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Null(options.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_custom_target_host()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            TargetHost = "sni.example.com"
        };

        Assert.Equal("sni.example.com", options.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_null_client_certificates()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Null(options.ClientCertificates);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_custom_client_certificates()
    {
        var certs = new X509CertificateCollection();
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ClientCertificates = certs
        };

        Assert.Same(certs, options.ClientCertificates);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_null_server_certificate_validation_callback()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Null(options.ServerCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_custom_server_certificate_validation_callback()
    {
        RemoteCertificateValidationCallback callback = (_, _, _, _) => true;
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ServerCertificateValidationCallback = callback
        };

        Assert.Same(callback, options.ServerCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_default_enabled_ssl_protocols_to_none()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Equal(SslProtocols.None, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_custom_enabled_ssl_protocols()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_null_application_protocols()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Null(options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_allow_custom_application_protocols()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 };
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = protocols
        };

        Assert.Same(protocols, options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_be_equal_with_same_values()
    {
        var options1 = new TlsOptions { Host = "example.com", Port = 443 };
        var options2 = new TlsOptions { Host = "example.com", Port = 443 };

        Assert.Equal(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_not_be_equal_with_different_target_host()
    {
        var options1 = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            TargetHost = "sni1.example.com"
        };

        var options2 = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            TargetHost = "sni2.example.com"
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_not_be_equal_with_different_enabled_ssl_protocols()
    {
        var options1 = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            EnabledSslProtocols = SslProtocols.Tls12
        };

        var options2 = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            EnabledSslProtocols = SslProtocols.Tls13
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_support_http2_alpn()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http2 };
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = protocols
        };

        Assert.NotNull(options.ApplicationProtocols);
        Assert.Single(options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_support_http11_alpn()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http11 };
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = protocols
        };

        Assert.NotNull(options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_support_http3_alpn()
    {
        var protocols = new List<SslApplicationProtocol> { SslApplicationProtocol.Http3 };
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = protocols
        };

        Assert.NotNull(options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_support_multiple_alpn_protocols()
    {
        var protocols = new List<SslApplicationProtocol>
        {
            SslApplicationProtocol.Http2,
            SslApplicationProtocol.Http11
        };
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = protocols
        };

        Assert.Equal(2, options.ApplicationProtocols!.Count);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_support_tls12_protocol()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            EnabledSslProtocols = SslProtocols.Tls12
        };

        Assert.Equal(SslProtocols.Tls12, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_should_support_tls13_protocol()
    {
        var options = new TlsOptions
        {
            Host = "example.com",
            Port = 443,
            EnabledSslProtocols = SslProtocols.Tls13
        };

        Assert.Equal(SslProtocols.Tls13, options.EnabledSslProtocols);
    }

    [Fact(Timeout = 5000)]
    public void TlsOptions_hash_code_should_be_consistent()
    {
        var options = new TlsOptions { Host = "example.com", Port = 443 };

        var hash1 = options.GetHashCode();
        var hash2 = options.GetHashCode();

        Assert.Equal(hash1, hash2);
    }
}