using TurboHTTP.Client;
using System.Net;
using System.Net.Security;
using Servus.Akka.Transport;
using TurboHTTP.Internal;

namespace TurboHTTP.Tests.Internal;

public sealed class OptionsFactorySpec
{
    private static RequestEndpoint CreateHttpEndpoint(string host = "example.com", ushort port = 80)
    {
        return new RequestEndpoint
        {
            Scheme = "http",
            Host = host,
            Port = port,
            Version = HttpVersion.Version11
        };
    }

    private static RequestEndpoint CreateHttpsEndpoint(string host = "example.com", ushort port = 443)
    {
        return new RequestEndpoint
        {
            Scheme = "https",
            Host = host,
            Port = port,
            Version = HttpVersion.Version11
        };
    }

    private static RequestEndpoint CreateHttp2Endpoint(string host = "example.com", ushort port = 443)
    {
        return new RequestEndpoint
        {
            Scheme = "https",
            Host = host,
            Port = port,
            Version = HttpVersion.Version20
        };
    }

    private static RequestEndpoint CreateHttp3Endpoint(string host = "example.com", ushort port = 443)
    {
        return new RequestEndpoint
        {
            Scheme = "https",
            Host = host,
            Port = port,
            Version = new Version(3, 0)
        };
    }

    private static TurboClientOptions CreateClientOptions()
    {
        return new TurboClientOptions();
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_build_plain_tcp_options_for_http_scheme()
    {
        var endpoint = CreateHttpEndpoint();
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.IsType<TcpTransportOptions>(options);
        Assert.IsNotType<TlsTransportOptions>(options);
        Assert.Equal("example.com", options.Host);
        Assert.Equal(80, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_build_tls_options_for_https_scheme()
    {
        var endpoint = CreateHttpsEndpoint();
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.IsType<TlsTransportOptions>(options);
        Assert.IsNotType<QuicTransportOptions>(options);
        Assert.Equal("example.com", options.Host);
        Assert.Equal(443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_build_quic_options_for_http3()
    {
        var endpoint = CreateHttp3Endpoint();
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.IsType<QuicTransportOptions>(options);
        Assert.Equal("example.com", options.Host);
        Assert.Equal(443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_use_default_port_80_for_http()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "http",
            Host = "example.com",
            Port = 0,
            Version = HttpVersion.Version11
        };
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(80, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_use_default_port_443_for_https()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 0,
            Version = HttpVersion.Version11
        };
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_use_custom_port_when_provided()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 8443,
            Version = HttpVersion.Version11
        };
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(8443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_set_http11_alpn_for_http11()
    {
        var endpoint = CreateHttpsEndpoint();
        var clientOptions = CreateClientOptions();

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.NotNull(options.ApplicationProtocols);
        Assert.Single(options.ApplicationProtocols);
        Assert.Equal(SslApplicationProtocol.Http11, options.ApplicationProtocols[0]);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_set_http2_alpn_for_http20()
    {
        var endpoint = CreateHttp2Endpoint();
        var clientOptions = CreateClientOptions();

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.NotNull(options.ApplicationProtocols);
        Assert.Single(options.ApplicationProtocols);
        Assert.Equal(SslApplicationProtocol.Http2, options.ApplicationProtocols[0]);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_set_http3_alpn_for_http30()
    {
        var endpoint = CreateHttp3Endpoint();
        var clientOptions = CreateClientOptions();

        var options = (QuicTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.NotNull(options.ApplicationProtocols);
        Assert.Single(options.ApplicationProtocols);
        Assert.Equal(SslApplicationProtocol.Http3, options.ApplicationProtocols[0]);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_not_set_alpn_for_http10()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 443,
            Version = HttpVersion.Version10
        };
        var clientOptions = CreateClientOptions();

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.Null(options.ApplicationProtocols);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_preserve_client_certificate_validation_callback()
    {
        var endpoint = CreateHttpsEndpoint();
        var callback = (RemoteCertificateValidationCallback)((_, _, _, _) => true);
        var clientOptions = new TurboClientOptions
        {
            ServerCertificateValidationCallback = callback
        };

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.Same(callback, options.ServerCertificateValidationCallback);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_preserve_connect_timeout()
    {
        var endpoint = CreateHttpsEndpoint();
        var timeout = TimeSpan.FromSeconds(30);
        var clientOptions = new TurboClientOptions
        {
            ConnectTimeout = timeout
        };

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(timeout, options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_preserve_socket_send_buffer_size()
    {
        var endpoint = CreateHttpEndpoint();
        var clientOptions = new TurboClientOptions
        {
            SocketSendBufferSize = 65536
        };

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(65536, options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_preserve_socket_receive_buffer_size()
    {
        var endpoint = CreateHttpEndpoint();
        var clientOptions = new TurboClientOptions
        {
            SocketReceiveBufferSize = 65536
        };

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(65536, options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_set_target_host_for_tls_options()
    {
        var endpoint = CreateHttpsEndpoint();
        var clientOptions = CreateClientOptions();

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal("example.com", options.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_preserve_http3_connection_migration_setting()
    {
        var endpoint = CreateHttp3Endpoint();
        var clientOptions = new TurboClientOptions
        {
            Http3 = new Http3Options { AllowConnectionMigration = false }
        };

        var options = (QuicTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.False(options.AllowConnectionMigration);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_handle_wss_scheme_as_https()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "wss",
            Host = "example.com",
            Port = 0,
            Version = HttpVersion.Version11
        };
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        // WSS should be treated as HTTPS and default port should be 443
        Assert.IsType<TlsTransportOptions>(options);
        Assert.Equal(443, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_treat_scheme_case_insensitively()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "HTTP",
            Host = "example.com",
            Port = 0,
            Version = HttpVersion.Version11
        };
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.IsType<TcpTransportOptions>(options);
        Assert.IsNotType<TlsTransportOptions>(options);
        Assert.Equal(80, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_preserve_proxy_settings()
    {
        var endpoint = CreateHttpEndpoint();
        var proxy = new WebProxy("http://proxy.example.com:8080");
        var credentials = new NetworkCredential("user", "pass");
        var clientOptions = new TurboClientOptions
        {
            UseProxy = true,
            Proxy = proxy,
            DefaultProxyCredentials = credentials
        };

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.IsType<TcpTransportOptions>(options);
        var tcpOptions = (TcpTransportOptions)options;
        Assert.True(tcpOptions.UseProxy);
        Assert.Same(proxy, tcpOptions.Proxy);
        Assert.Same(credentials, tcpOptions.DefaultProxyCredentials);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_build_options_for_all_http_versions()
    {
        var clientOptions = CreateClientOptions();

        // HTTP/1.0
        var options10 = OptionsFactory.Build(
            new RequestEndpoint
            {
                Scheme = "http",
                Host = "example.com",
                Port = 80,
                Version = HttpVersion.Version10
            },
            clientOptions);
        Assert.IsType<TcpTransportOptions>(options10);

        // HTTP/1.1
        var options11 = OptionsFactory.Build(
            new RequestEndpoint
            {
                Scheme = "http",
                Host = "example.com",
                Port = 80,
                Version = HttpVersion.Version11
            },
            clientOptions);
        Assert.IsType<TcpTransportOptions>(options11);

        // HTTP/2.0
        var options20 = OptionsFactory.Build(
            new RequestEndpoint
            {
                Scheme = "https",
                Host = "example.com",
                Port = 443,
                Version = HttpVersion.Version20
            },
            clientOptions);
        Assert.IsType<TlsTransportOptions>(options20);

        // HTTP/3.0
        var options30 = OptionsFactory.Build(
            new RequestEndpoint
            {
                Scheme = "https",
                Host = "example.com",
                Port = 443,
                Version = new Version(3, 0)
            },
            clientOptions);
        Assert.IsType<QuicTransportOptions>(options30);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_handle_non_standard_ports()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "example.com",
            Port = 9999,
            Version = HttpVersion.Version11
        };
        var clientOptions = CreateClientOptions();

        var options = OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal(9999, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_handle_localhost()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "localhost",
            Port = 8443,
            Version = HttpVersion.Version20
        };
        var clientOptions = CreateClientOptions();

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal("localhost", options.Host);
        Assert.Equal(8443, options.Port);
        Assert.Equal("localhost", options.TargetHost);
    }

    [Fact(Timeout = 5000)]
    public void OptionsFactory_should_handle_ip_addresses()
    {
        var endpoint = new RequestEndpoint
        {
            Scheme = "https",
            Host = "192.168.1.1",
            Port = 443,
            Version = HttpVersion.Version20
        };
        var clientOptions = CreateClientOptions();

        var options = (TlsTransportOptions)OptionsFactory.Build(endpoint, clientOptions);

        Assert.Equal("192.168.1.1", options.Host);
    }
}