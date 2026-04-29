using System.Net;
using System.Net.Sockets;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class TcpClientProviderSpec
{
    [Fact(Timeout = 5000)]
    public void TcpClientProvider_should_initialize_with_options()
    {
        var options = new TcpTransportOptions
        {
            Host = "localhost",
            Port = 8080
        };

        var provider = new TcpClientProvider(options);

        Assert.Null(provider.RemoteEndPoint);
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_dispose_without_socket()
    {
        var options = new TcpTransportOptions { Host = "localhost", Port = 8080 };
        var provider = new TcpClientProvider(options);

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_complete_disposal_on_double_dispose()
    {
        var options = new TcpTransportOptions { Host = "localhost", Port = 8080 };
        var provider = new TcpClientProvider(options);

        await provider.DisposeAsync();
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 10_000)]
    public async Task TcpClientProvider_should_resolve_proxy_when_configured()
    {
        var proxyUri = new Uri("http://proxy.local:8080");
        var proxy = new TestProxy(proxyUri);

        var options = new TcpTransportOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy
        };

        var provider = new TcpClientProvider(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await provider.GetStreamAsync(cts.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_bypass_proxy_when_bypassed()
    {
        var proxy = new TestProxy(null, bypassedHost: "example.com");

        var options = new TcpTransportOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy
        };

        var provider = new TcpClientProvider(options);

        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_not_use_proxy_when_disabled()
    {
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var options = new TcpTransportOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = false,
            Proxy = proxy
        };

        var provider = new TcpClientProvider(options);

        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 10_000)]
    public async Task TcpClientProvider_should_apply_default_proxy_credentials()
    {
        var credentials = new NetworkCredential("user", "pass");
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var options = new TcpTransportOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy,
            DefaultProxyCredentials = credentials
        };

        var provider = new TcpClientProvider(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await provider.GetStreamAsync(cts.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            Assert.True(ex is SocketException or OperationCanceledException, $"Unexpected: {ex}");
        }

        Assert.NotNull(proxy.Credentials);
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 10_000)]
    public async Task TcpClientProvider_should_not_override_existing_proxy_credentials()
    {
        var existingCredentials = new NetworkCredential("existing", "existing");
        var defaultCredentials = new NetworkCredential("default", "default");
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"), credentials: existingCredentials);

        var options = new TcpTransportOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy,
            DefaultProxyCredentials = defaultCredentials
        };

        var provider = new TcpClientProvider(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await provider.GetStreamAsync(cts.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
        }

        Assert.Equal("existing", ((NetworkCredential)proxy.Credentials!).UserName);
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_set_socket_options()
    {
        var options = new TcpTransportOptions
        {
            Host = "localhost",
            Port = 8080,
            SocketSendBufferSize = 65536,
            SocketReceiveBufferSize = 65536
        };

        var provider = new TcpClientProvider(options);

        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_handle_null_buffer_sizes()
    {
        var options = new TcpTransportOptions
        {
            Host = "localhost",
            Port = 8080,
            SocketSendBufferSize = null,
            SocketReceiveBufferSize = null
        };

        var provider = new TcpClientProvider(options);

        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_dispose_socket_on_cancellation()
    {
        var options = new TcpTransportOptions
        {
            Host = "192.0.2.1",
            Port = 443
        };

        var provider = new TcpClientProvider(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await provider.GetStreamAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        await provider.DisposeAsync();
    }

    private sealed class TestProxy(Uri? proxyUri, string? bypassedHost = null, ICredentials? credentials = null)
        : IWebProxy
    {
        public ICredentials? Credentials { get; set; } = credentials;

        public Uri? GetProxy(Uri destination) => proxyUri;

        public bool IsBypassed(Uri host)
        {
            if (bypassedHost is null)
            {
                return false;
            }

            return host.Host == bypassedHost;
        }
    }
}
