using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TurboHttp.Transport;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

/// <summary>
/// Tests for TASK-007-001: QuicClientProvider reentrant multi-stream support
/// and IClientProvider.SupportsMultipleStreams property.
/// </summary>
public sealed class QuicMultiStreamTests
{
    // ──────────────────────────────────────────────────────────────────────
    // SupportsMultipleStreams property tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3-MS-001: TcpClientProvider.SupportsMultipleStreams returns false")]
    public void TcpClientProvider_SupportsMultipleStreams_ReturnsFalse()
    {
        IClientProvider provider = new TcpClientProvider(new TcpOptions { Host = "localhost", Port = 80 });
        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(DisplayName = "RFC9114-3-MS-002: TlsClientProvider.SupportsMultipleStreams returns false")]
    public void TlsClientProvider_SupportsMultipleStreams_ReturnsFalse()
    {
        IClientProvider provider = new TlsClientProvider(new TlsOptions { Host = "localhost", Port = 443 });
        Assert.False(provider.SupportsMultipleStreams);
    }

    [Fact(DisplayName = "RFC9114-3-MS-003: QuicClientProvider.SupportsMultipleStreams returns true")]
    public void QuicClientProvider_SupportsMultipleStreams_ReturnsTrue()
    {
#pragma warning disable CA1416 // Platform compatibility verified at test runner level
        var provider = new QuicClientProvider(new QuicOptions { Host = "example.com", Port = 443 });
        IClientProvider iface = provider;
#pragma warning restore CA1416
        Assert.True(iface.SupportsMultipleStreams);
    }

    [Fact(DisplayName = "RFC9114-3-MS-004: Default interface implementation of SupportsMultipleStreams is false")]
    public void DefaultInterface_SupportsMultipleStreams_ReturnsFalse()
    {
        IClientProvider provider = new MinimalClientProvider();
        Assert.False(provider.SupportsMultipleStreams);
    }

    // ──────────────────────────────────────────────────────────────────────
    // QuicClientProvider reentrant connection tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3-MS-005: QuicClientProvider rejects empty host on GetStreamAsync")]
    public async Task QuicClientProvider_ThrowsOnEmptyHost()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = "", Port = 443 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync());
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-3-MS-006: QuicClientProvider rejects null host on GetStreamAsync")]
    public async Task QuicClientProvider_ThrowsOnNullHost()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = null!, Port = 443 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync());
#pragma warning restore CA1416
        Assert.Contains("SNI", ex.Message);
    }

    [Fact(DisplayName = "RFC9114-3-MS-007: QuicClientProvider.Close is safe to call when not connected")]
    public void QuicClientProvider_Close_WhenNotConnected_DoesNotThrow()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = "example.com", Port = 443 });
        provider.Close();
#pragma warning restore CA1416
    }

    [Fact(DisplayName = "RFC9114-3-MS-008: QuicClientProvider.Close is safe to call twice")]
    public void QuicClientProvider_Close_Twice_DoesNotThrow()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = "example.com", Port = 443 });
        provider.Close();
        provider.Close();
#pragma warning restore CA1416
    }

    [Fact(DisplayName = "RFC9114-3-MS-009: QuicClientProvider.RemoteEndPoint is null when not connected")]
    public void QuicClientProvider_RemoteEndPoint_NullWhenNotConnected()
    {
#pragma warning disable CA1416
        var provider = new QuicClientProvider(new QuicOptions { Host = "example.com", Port = 443 });
        Assert.Null(provider.RemoteEndPoint);
#pragma warning restore CA1416
    }

    // ──────────────────────────────────────────────────────────────────────
    // Concurrent connection establishment tests (using testable seam)
    // ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "RFC9114-3-MS-010: ReentrantStreamProvider opens multiple streams on same connection")]
    public async Task ReentrantStreamProvider_OpensMultipleStreams()
    {
        var provider = new FakeReentrantProvider(streamCount: 5);

        var stream1 = await provider.GetStreamAsync();
        var stream2 = await provider.GetStreamAsync();
        var stream3 = await provider.GetStreamAsync();

        Assert.NotSame(stream1, stream2);
        Assert.NotSame(stream2, stream3);
        Assert.Equal(1, provider.ConnectionCount);
        Assert.Equal(3, provider.StreamCount);
        Assert.True(provider.SupportsMultipleStreams);
    }

    [Fact(DisplayName = "RFC9114-3-MS-011: Concurrent GetStreamAsync calls create only one connection")]
    public async Task ConcurrentGetStreamAsync_CreatesOneConnection()
    {
        var provider = new FakeReentrantProvider(streamCount: 10, connectDelay: TimeSpan.FromMilliseconds(50));

        // Launch 5 concurrent GetStreamAsync calls
        var tasks = new Task<Stream>[5];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = provider.GetStreamAsync();
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

    [Fact(DisplayName = "RFC9114-3-MS-012: Dead connection triggers reconnect on next GetStreamAsync")]
    public async Task DeadConnection_TriggersReconnect()
    {
        var provider = new FakeReentrantProvider(streamCount: 10);

        // First stream succeeds
        var stream1 = await provider.GetStreamAsync();
        Assert.Equal(1, provider.ConnectionCount);

        // Simulate connection death
        provider.KillConnection();

        // Next call should reconnect
        var stream2 = await provider.GetStreamAsync();
        Assert.Equal(2, provider.ConnectionCount);
        Assert.NotSame(stream1, stream2);
    }

    [Fact(DisplayName = "RFC9114-3-MS-013: Stream open failure wraps as reconnectable error")]
    public async Task StreamOpenFailure_WrapsAsReconnectableError()
    {
        var provider = new FakeReentrantProvider(streamCount: 10, failStreamOpen: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetStreamAsync());
        Assert.Contains("no longer usable", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Test doubles
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IClientProvider that does NOT override SupportsMultipleStreams,
    /// verifying the default interface implementation returns false.
    /// </summary>
    private sealed class MinimalClientProvider : IClientProvider
    {
        public EndPoint? RemoteEndPoint => null;
        public Task<Stream> GetStreamAsync(CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public void Close() { }
        public ValueTask DisposeAsync() { Close(); return ValueTask.CompletedTask; }
    }

    /// <summary>
    /// Fake reentrant provider that mimics QuicClientProvider's connection-reuse pattern
    /// for testing multi-stream, thread-safety, and reconnection behavior without real QUIC.
    /// </summary>
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

        public ValueTask DisposeAsync() { Close(); return ValueTask.CompletedTask; }

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
