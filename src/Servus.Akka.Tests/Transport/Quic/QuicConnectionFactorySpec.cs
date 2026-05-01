using System.Net.Quic;
using Servus.Akka.Tests.Utils;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

public sealed class QuicConnectionFactorySpec
{
    private static async Task<QuicConnectionLease?> TryEstablishAsync(QuicTransportOptions options,
        CancellationToken ct)
    {
        return await QuicConnectionFactory.Instance.EstablishAsync(options, ct);
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_return_lease_with_valid_handle()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true,
            IdleTimeout = TimeSpan.FromSeconds(5),
            MaxBidirectionalStreams = 10,
            MaxUnidirectionalStreams = 5
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        Assert.NotNull(lease.Handle);
        Assert.True(lease.IsAlive());
        Assert.Equal(0, lease.ActiveStreams);

        await lease.DisposeAsync();
        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_create_bidirectional_streams()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        var (stream, streamId) =
            await lease.Handle.OpenStreamAsync(StreamDirection.Bidirectional, TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.True(streamId >= 0, "Stream ID should be non-negative");

        stream.Dispose();
        await lease.DisposeAsync();
        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_create_unidirectional_streams()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        var (stream, streamId) =
            await lease.Handle.OpenStreamAsync(StreamDirection.Unidirectional, TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        Assert.True(streamId >= 0, "Stream ID should be non-negative");

        stream.Dispose();
        await lease.DisposeAsync();
        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_respect_max_bidirectional_streams()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true,
            MaxBidirectionalStreams = 5
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        Assert.Equal(5,
            lease.Handle.GetType().GetProperty("_maxConcurrentStreams",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(lease.Handle) ?? 0);

        await lease.DisposeAsync();
        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_throw_on_invalid_host()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        var options = new QuicTransportOptions
        {
            Host = "invalid-host-that-does-not-exist-12345.com",
            Port = 443,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true
        };

        await Assert.ThrowsAsync<System.Net.Sockets.SocketException>(() =>
            QuicConnectionFactory.Instance.EstablishAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_dispose_cleanly()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        Assert.True(lease.IsAlive());

        await lease.DisposeAsync();
        Assert.False(lease.IsAlive());

        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_track_active_streams()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true,
            MaxBidirectionalStreams = 10
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        Assert.Equal(0, lease.ActiveStreams);

        lease.MarkBusy();
        Assert.Equal(1, lease.ActiveStreams);

        lease.MarkBusy();
        Assert.Equal(2, lease.ActiveStreams);

        lease.MarkIdle();
        Assert.Equal(1, lease.ActiveStreams);

        lease.MarkIdle();
        Assert.Equal(0, lease.ActiveStreams);

        await lease.DisposeAsync();
        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }

    [Fact(Timeout = 15000)]
    public async Task EstablishAsync_should_return_valid_local_endpoint()
    {
        if (!QuicListener.IsSupported)
        {
            return;
        }

        await using var server = await LoopbackQuicServer.CreateAsync();
        var serverConnTask = server.AcceptConnectionAsync(TestContext.Current.CancellationToken);

        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = (ushort)server.Port,
            ApplicationProtocols = [LoopbackQuicServer.Alpn],
            ServerCertificateValidationCallback = (_, _, _, _) => true
        };

        var lease = await TryEstablishAsync(options, TestContext.Current.CancellationToken);
        if (lease is null)
        {
            return;
        }

        var localEndPoint = lease.Handle.LocalEndPoint();
        Assert.NotNull(localEndPoint);

        await lease.DisposeAsync();
        try
        {
            var serverConn = await serverConnTask;
            await serverConn.DisposeAsync();
        }
        catch
        {
            // Server connection acceptance may fail if client closes first
        }
    }
}