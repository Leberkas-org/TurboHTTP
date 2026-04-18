using System.Net;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class QuicMultiStreamSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void TcpClientProvider_SupportsMultipleStreams_ReturnsFalse()
    {
        IClientProvider provider = new TcpClientProvider(new TcpOptions { Host = "localhost", Port = 80 });
        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void TlsClientProvider_SupportsMultipleStreams_ReturnsFalse()
    {
        IClientProvider provider = new TlsClientProvider(new TlsOptions { Host = "localhost", Port = 443 });
        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void QuicClientProvider_SupportsMultipleStreams_ReturnsTrue()
    {
#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(new QuicOptions { Host = "example.com", Port = 443 });
        IClientProvider iface = provider;
#pragma warning restore CA1416
        Assert.True(iface.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void DefaultInterface_SupportsMultipleStreams_ReturnsFalse()
    {
        IClientProvider provider = new MinimalClientProvider();
        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task QuicClientProvider_ThrowsOnEmptyHost()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = "", Port = 443 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetStreamAsync(TestContext.Current.CancellationToken));
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task QuicClientProvider_ThrowsOnNullHost()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = null!, Port = 443 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetStreamAsync(TestContext.Current.CancellationToken));
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public void QuicClientProvider_RemoteEndPoint_NullWhenNotConnected()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = "example.com", Port = 443 });
        Assert.Null(provider.RemoteEndPoint);
#pragma warning restore CA1416
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task ReentrantStreamProvider_OpensMultipleStreams()
    {
        var provider = new FakeReentrantProvider(streamCount: 5);

        var stream1 = await provider.GetStreamAsync(TestContext.Current.CancellationToken);
        var stream2 = await provider.GetStreamAsync(TestContext.Current.CancellationToken);
        var stream3 = await provider.GetStreamAsync(TestContext.Current.CancellationToken);

        Assert.NotSame(stream1, stream2);
        Assert.NotSame(stream2, stream3);
        Assert.Equal(1, provider.ConnectionCount);
        Assert.Equal(3, provider.StreamCount);
        Assert.True(provider.SupportsMultipleStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task ConcurrentGetStreamAsync_CreatesOneConnection()
    {
        var provider = new FakeReentrantProvider(streamCount: 10, connectDelay: TimeSpan.FromMilliseconds(50));

        // Launch 5 concurrent GetStreamAsync calls
        var tasks = new Task<Stream>[5];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = provider.GetStreamAsync(TestContext.Current.CancellationToken);
        }

        var streams = await Task.WhenAll(tasks);

        Assert.Equal(1, provider.ConnectionCount);
        Assert.Equal(5, provider.StreamCount);

        // All streams should be distinct
        for (var i = 0; i < streams.Length; i++)
        {
            for (var j = i + 1; j < streams.Length; j++)
            {
                Assert.NotSame(streams[i], streams[j]);
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task DeadConnection_TriggersReconnect()
    {
        var provider = new FakeReentrantProvider(streamCount: 10);

        // First stream succeeds
        var stream1 = await provider.GetStreamAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, provider.ConnectionCount);

        // Simulate connection death
        provider.KillConnection();

        // Next call should reconnect
        var stream2 = await provider.GetStreamAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, provider.ConnectionCount);
        Assert.NotSame(stream1, stream2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-3")]
    public async Task StreamOpenFailure_WrapsAsReconnectableError()
    {
        var provider = new FakeReentrantProvider(streamCount: 10, failStreamOpen: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetStreamAsync(TestContext.Current.CancellationToken));
        Assert.Contains("no longer usable", ex.Message);
    }

    private sealed class MinimalClientProvider : IClientProvider
    {
        public EndPoint? RemoteEndPoint => null;

        public Task<Stream> GetStreamAsync(CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public static void Close()
        {
        }

        public ValueTask DisposeAsync()
        {
            Close();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeReentrantProvider : IClientProvider
    {
        private readonly TimeSpan _connectDelay;
        private readonly bool _failStreamOpen;
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private object? _connection; // simulates QuicConnection
        private int _connectionCount;
        private int _streamCount;

        public FakeReentrantProvider(int streamCount, TimeSpan connectDelay = default, bool failStreamOpen = false)
        {
            _ = streamCount; // reserved for future stream-limit tests
            _connectDelay = connectDelay;
            _failStreamOpen = failStreamOpen;
        }

        public EndPoint? RemoteEndPoint => _connection is not null ? new IPEndPoint(IPAddress.Loopback, 443) : null;
        public bool SupportsMultipleStreams => true;
        public int ConnectionCount => _connectionCount;
        public int StreamCount => _streamCount;

        public async Task<Stream> GetStreamAsync(CancellationToken ct = default)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);

            if (_failStreamOpen)
            {
                Interlocked.Exchange(ref _connection, null);
                throw new InvalidOperationException(
                    "QUIC connection to 'fake:443' is no longer usable. "
                    + "A new connection will be established on the next request.");
            }

            Interlocked.Increment(ref _streamCount);
            return new MemoryStream();
        }

        public void KillConnection()
        {
            Interlocked.Exchange(ref _connection, null);
        }

        public void Close()
        {
            Interlocked.Exchange(ref _connection, null);
        }

        public ValueTask DisposeAsync()
        {
            Close();
            return ValueTask.CompletedTask;
        }

        private async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _connection) is not null)
            {
                return;
            }

            await _connectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _connection) is not null)
                {
                    return;
                }

                if (_connectDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_connectDelay, ct).ConfigureAwait(false);
                }

                Volatile.Write(ref _connection, new object());
                Interlocked.Increment(ref _connectionCount);
            }
            finally
            {
                _connectLock.Release();
            }
        }
    }
}