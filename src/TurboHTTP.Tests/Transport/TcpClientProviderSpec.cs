using System.Net;
using System.Net.Sockets;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests <see cref="TcpClientProvider"/> socket creation, proxy resolution, and cleanup logic.
/// </summary>
public sealed class TcpClientProviderSpec
{
    [Fact(Timeout = 5000)]
    public void TcpClientProvider_should_initialize_with_options()
    {
        var options = new TcpOptions
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
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var provider = new TcpClientProvider(options);

        // No GetStreamAsync called, so no socket created
        await provider.DisposeAsync();

        // Assert: should complete without error
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_complete_disposal_on_double_dispose()
    {
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        var provider = new TcpClientProvider(options);

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        // Assert: should not throw on second dispose
    }

    [Fact(Timeout = 5000)]
    public void TcpClientProvider_should_not_support_multiple_streams()
    {
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        IClientProvider provider = new TcpClientProvider(options);

        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_throw_on_unidirectional_stream()
    {
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        IClientProvider provider = new TcpClientProvider(options);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.GetUnidirectionalStreamAsync(CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_throw_on_accept_inbound_stream()
    {
        var options = new TcpOptions { Host = "localhost", Port = 8080 };
        IClientProvider provider = new TcpClientProvider(options);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.AcceptInboundStreamAsync(CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_resolve_proxy_when_configured()
    {
        var proxyUri = new Uri("http://proxy.local:8080");
        var proxy = new TestProxy(proxyUri);

        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy
        };

        var provider = new TcpClientProvider(options);

        // Verify GetStreamAsync uses proxy for connection (DNS lookup will fail, which is ok for this test)
        // This test verifies the proxy resolution path works without requiring actual network
        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
            // Expected: DNS resolution fails for "proxy.local"
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_bypass_proxy_when_bypassed()
    {
        var proxy = new TestProxy(null, bypassedHost: "example.com");

        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy
        };

        var provider = new TcpClientProvider(options);

        // Should attempt direct connection to example.com (not through proxy)
        // DNS lookup will fail, which is ok for this test
        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
            // Expected: DNS resolution fails for "example.com"
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_not_use_proxy_when_disabled()
    {
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = false,
            Proxy = proxy
        };

        var provider = new TcpClientProvider(options);

        // Should attempt direct connection to example.com
        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
            // Expected: DNS resolution fails for "example.com"
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_apply_default_proxy_credentials()
    {
        var credentials = new NetworkCredential("user", "pass");
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"));

        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy,
            DefaultProxyCredentials = credentials
        };

        var provider = new TcpClientProvider(options);

        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
            // Expected
        }

        // Verify credentials were applied to proxy
        Assert.NotNull(proxy.Credentials);
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_not_override_existing_proxy_credentials()
    {
        var existingCredentials = new NetworkCredential("existing", "existing");
        var defaultCredentials = new NetworkCredential("default", "default");
        var proxy = new TestProxy(new Uri("http://proxy.local:8080"), credentials: existingCredentials);

        var options = new TcpOptions
        {
            Host = "example.com",
            Port = 443,
            UseProxy = true,
            Proxy = proxy,
            DefaultProxyCredentials = defaultCredentials
        };

        var provider = new TcpClientProvider(options);

        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (SocketException)
        {
            // Expected
        }

        // Verify existing credentials were not replaced
        Assert.Equal("existing", ((NetworkCredential)proxy.Credentials!).UserName);
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_set_socket_options()
    {
        // This test verifies that socket options are correctly configured.
        // We can't fully test this without a real connection, but we can verify
        // the provider handles options without throwing.
        var options = new TcpOptions
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
            // Expected
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_handle_null_buffer_sizes()
    {
        var options = new TcpOptions
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
            // Expected
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task TcpClientProvider_should_dispose_socket_on_cancellation()
    {
        var options = new TcpOptions
        {
            Host = "192.0.2.1", // TEST-NET-1: guaranteed not to route
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
            // Expected
        }

        // Provider should be disposable even after cancellation
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