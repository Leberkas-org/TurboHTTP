using System.Net;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

public sealed class TcpOptionsSpec
{
    [Fact(Timeout = 5000)]
    public void TcpOptions_should_set_required_host()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Equal("example.com", options.Host);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_set_required_port()
    {
        var options = new TcpOptions { Host = "example.com", Port = 8080 };

        Assert.Equal(8080, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_default_connect_timeout_to_10_seconds()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_custom_connect_timeout()
    {
        var timeout = TimeSpan.FromSeconds(30);
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            ConnectTimeout = timeout
        };

        Assert.Equal(timeout, options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_zero_socket_send_buffer_size()
    {
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            SocketSendBufferSize = 0
        };

        Assert.Equal(0, options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_null_socket_send_buffer_size()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Null(options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_custom_socket_send_buffer_size()
    {
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            SocketSendBufferSize = 65536
        };

        Assert.Equal(65536, options.SocketSendBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_null_socket_receive_buffer_size()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Null(options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_custom_socket_receive_buffer_size()
    {
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            SocketReceiveBufferSize = 65536
        };

        Assert.Equal(65536, options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_default_use_proxy_to_false()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.False(options.UseProxy);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_use_proxy_true()
    {
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            UseProxy = true
        };

        Assert.True(options.UseProxy);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_null_proxy()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Null(options.Proxy);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_custom_proxy()
    {
        var proxy = new WebProxy("http://proxy.example.com:8080");
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            Proxy = proxy
        };

        Assert.Same(proxy, options.Proxy);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_null_default_proxy_credentials()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Null(options.DefaultProxyCredentials);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_custom_default_proxy_credentials()
    {
        var credentials = new NetworkCredential("user", "password");
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            DefaultProxyCredentials = credentials
        };

        Assert.Same(credentials, options.DefaultProxyCredentials);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_be_equal_with_same_values()
    {
        var options1 = new TcpOptions { Host = "example.com", Port = 80 };
        var options2 = new TcpOptions { Host = "example.com", Port = 80 };

        Assert.Equal(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_not_be_equal_with_different_host()
    {
        var options1 = new TcpOptions { Host = "example.com", Port = 80 };
        var options2 = new TcpOptions { Host = "other.com", Port = 80 };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_not_be_equal_with_different_port()
    {
        var options1 = new TcpOptions { Host = "example.com", Port = 80 };
        var options2 = new TcpOptions { Host = "example.com", Port = 8080 };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_not_be_equal_with_different_connect_timeout()
    {
        var options1 = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        var options2 = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };

        Assert.NotEqual(options1, options2);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_support_localhost()
    {
        var options = new TcpOptions { Host = "localhost", Port = 8080 };

        Assert.Equal("localhost", options.Host);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_support_ip_address()
    {
        var options = new TcpOptions { Host = "127.0.0.1", Port = 80 };

        Assert.Equal("127.0.0.1", options.Host);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_support_high_port_numbers()
    {
        var options = new TcpOptions { Host = "example.com", Port = 65535 };

        Assert.Equal(65535, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_support_port_zero()
    {
        var options = new TcpOptions { Host = "example.com", Port = 0 };

        Assert.Equal(0, options.Port);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_very_large_buffer_sizes()
    {
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            SocketSendBufferSize = int.MaxValue,
            SocketReceiveBufferSize = int.MaxValue
        };

        Assert.Equal(int.MaxValue, options.SocketSendBufferSize);
        Assert.Equal(int.MaxValue, options.SocketReceiveBufferSize);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_very_short_connect_timeout()
    {
        var timeout = TimeSpan.FromMilliseconds(1);
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            ConnectTimeout = timeout
        };

        Assert.Equal(timeout, options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_should_allow_very_long_connect_timeout()
    {
        var timeout = TimeSpan.FromHours(24);
        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 80,
            ConnectTimeout = timeout
        };

        Assert.Equal(timeout, options.ConnectTimeout);
    }

    [Fact(Timeout = 5000)]
    public void TcpOptions_hash_code_should_be_consistent()
    {
        var options = new TcpOptions { Host = "example.com", Port = 80 };

        var hash1 = options.GetHashCode();
        var hash2 = options.GetHashCode();

        Assert.Equal(hash1, hash2);
    }
}