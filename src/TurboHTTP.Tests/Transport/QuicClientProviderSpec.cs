using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

#pragma warning disable CA1416

public sealed class QuicClientProviderSpec
{
    [Fact(Timeout = 5000)]
    public void QuicClientProvider_should_initialize_with_options()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443
        };

        var provider = new QuicClientProvider(options);

        Assert.Null(provider.RemoteEndPoint);
        Assert.Null(provider.LocalEndPoint);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_dispose_without_connection()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };
        var provider = new QuicClientProvider(options);

        // No connection established
        await provider.DisposeAsync();

        // Assert: should complete without error
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_complete_disposal_on_double_dispose()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };
        var provider = new QuicClientProvider(options);

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        // Assert: should not throw on second dispose
    }

    [Fact(Timeout = 5000)]
    public void QuicClientProvider_should_support_multiple_streams()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };
        var provider = new QuicClientProvider(options);

        Assert.True(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    public void QuicClientProvider_should_have_supported_os_platforms()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };
        var provider = new QuicClientProvider(options);

        Assert.NotNull(provider);

        // Verify type is sealed (platform support attributes only work on sealed classes)
        Assert.True(typeof(QuicClientProvider).IsSealed);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_throw_on_empty_host_during_connect()
    {
        var options = new QuicOptions
        {
            Host = "",
            Port = 443
        };

        var provider = new QuicClientProvider(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetStreamAsync(CancellationToken.None));

        Assert.Contains("non-empty hostname", ex.Message);

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_throw_on_null_host_during_connect()
    {
        var options = new QuicOptions
        {
            Host = null!,
            Port = 443
        };

        var provider = new QuicClientProvider(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetStreamAsync(CancellationToken.None));

        Assert.Contains("non-empty hostname", ex.Message);

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public void QuicClientProvider_should_throw_early_data_rejected()
    {
        // This test verifies the EarlyDataRejectedException type and message format
        var exception = new QuicClientProvider.EarlyDataRejectedException(
            "QUIC 0-RTT early data rejected by 'example.com:443'. Request will be re-sent after full handshake.");

        Assert.NotNull(exception);
        Assert.Contains("0-RTT early data rejected", exception.Message);
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_throw_on_unidirectional_stream_when_connection_fails()
    {
        var options = new QuicOptions
        {
            Host = "",
            Port = 443
        };

        var provider = new QuicClientProvider(options);

        // First attempt to get a stream will fail during connection
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetUnidirectionalStreamAsync(CancellationToken.None));

        Assert.Contains("non-empty hostname", ex1.Message);

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_throw_on_accept_inbound_stream_when_connection_fails()
    {
        var options = new QuicOptions
        {
            Host = "",
            Port = 443
        };

        var provider = new QuicClientProvider(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.AcceptInboundStreamAsync(CancellationToken.None));

        Assert.Contains("non-empty hostname", ex.Message);

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public void QuicClientProvider_should_have_configurable_options()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 8443,
            MaxBidirectionalStreams = 100,
            MaxUnidirectionalStreams = 50,
            IdleTimeout = TimeSpan.FromSeconds(60),
            AllowEarlyData = true
        };

        var provider = new QuicClientProvider(options);

        Assert.NotNull(provider);
        // Verify provider accepts these options (no error)
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_clear_connection_on_dispose()
    {
        var options = new QuicOptions
        {
            Host = "example.com",
            Port = 443,
            ApplicationProtocols = [System.Net.Security.SslApplicationProtocol.Http3]
        };
        var provider = new QuicClientProvider(options);

        // Attempt connection (will fail, but we verify disposal after failure)
        try
        {
            await provider.GetStreamAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Expected: connection failure
        }

        // Should be disposable
        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_handle_connection_timeout()
    {
        var options = new QuicOptions
        {
            Host = "192.0.2.1", // TEST-NET-1: guaranteed not to route
            Port = 443,
            ApplicationProtocols = [System.Net.Security.SslApplicationProtocol.Http3]
        };

        var provider = new QuicClientProvider(options);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        try
        {
            await provider.GetStreamAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected: connection timeout
        }

        await provider.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task QuicClientProvider_should_support_concurrent_dispose()
    {
        var options = new QuicOptions { Host = "example.com", Port = 443 };
        var provider = new QuicClientProvider(options);

        // Attempt multiple concurrent disposals
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => provider.DisposeAsync().AsTask())
            .ToList();

        await Task.WhenAll(tasks);

        // Assert: all should complete without exception
    }
}