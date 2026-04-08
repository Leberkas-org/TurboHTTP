using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Tcp;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Tests.Transport;

/// <summary>
/// Tests <see cref="DirectConnectionFactory"/> establishing connections,
/// handling cancellation, connection errors, and disposal cleanup.
/// </summary>
public sealed class DirectConnectionFactorySpec : IAsyncLifetime
{
    private TcpListener? _listener;
    private int _port;

    public ValueTask InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _listener?.Stop();
        return ValueTask.CompletedTask;
    }

    private TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = _port,
        MaxFrameSize = 16384
    };

    private static RequestEndpoint CreateEndpoint(int port, Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = (ushort)port,
        Scheme = "http",
        Version = version ?? HttpVersion.Version11
    };

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_return_live_lease()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
        Assert.True(lease.Reusable);
        Assert.Equal(endpoint, lease.Key);
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_use_nobody_for_connection_actor()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);

        Assert.Equal(ActorRefs.Nobody, lease.Handle.ConnectionActor);
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_flow_data_through_byte_mover_pumps()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        // Accept the connection server-side
        var acceptTask = _listener!.AcceptTcpClientAsync(TestContext.Current.CancellationToken);

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);

        using var serverClient = await acceptTask;
        var serverStream = serverClient.GetStream();

        // Server sends data → should appear on lease's InboundReader
        var testData = "Hello from server"u8.ToArray();
        await serverStream.WriteAsync(testData, TestContext.Current.CancellationToken);
        await serverStream.FlushAsync(TestContext.Current.CancellationToken);

        // Read from the inbound reader (data flows: TCP → Pipe → Channel)
        var buffer = await lease.Handle.InboundReader.ReadAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(buffer.Length > 0);
            Assert.Equal(testData, buffer.Span.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_send_outbound_data_to_server()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var acceptTask = _listener!.AcceptTcpClientAsync(TestContext.Current.CancellationToken);

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);

        using var serverClient = await acceptTask;
        var serverStream = serverClient.GetStream();

        // Write data to outbound → should arrive at server
        var testData = "Hello from client"u8.ToArray();
        Assert.True(lease.Handle.OutboundWriter.TryWrite(NetworkBuffer.FromArray(testData)));

        // Read from server side
        var readBuf = new byte[1024];
        var bytesRead = await serverStream.ReadAsync(readBuf, TestContext.Current.CancellationToken);

        Assert.Equal(testData, readBuf[..bytesRead]);
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_set_max_concurrent_streams_to_version_defaults()
    {
        var options = CreateOptions();

        // HTTP/1.0 → 1
        var endpoint10 = CreateEndpoint(_port, HttpVersion.Version10);
        using var lease10 = await DirectConnectionFactory.EstablishAsync(options, endpoint10, TestContext.Current.CancellationToken);
        Assert.Equal(1, lease10.MaxConcurrentStreams);

        // HTTP/1.1 → 6
        var endpoint11 = CreateEndpoint(_port, HttpVersion.Version11);
        using var lease11 = await DirectConnectionFactory.EstablishAsync(options, endpoint11, TestContext.Current.CancellationToken);
        Assert.Equal(6, lease11.MaxConcurrentStreams);

        // HTTP/2 → 100
        var endpoint20 = CreateEndpoint(_port, HttpVersion.Version20);
        using var lease20 = await DirectConnectionFactory.EstablishAsync(options, endpoint20, TestContext.Current.CancellationToken);
        Assert.Equal(100, lease20.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_throw_on_pre_cancelled_token()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DirectConnectionFactory.EstablishAsync(options, endpoint, cts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_throw_when_cancelled_during_connect()
    {
        // Use a non-routable address to force a slow connect that we can cancel
        var options = new TcpOptions
        {
            Host = "192.0.2.1", // RFC 5737 TEST-NET — not routable
            Port = 80,
            MaxFrameSize = 16384,
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };
        var endpoint = CreateEndpoint(80);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DirectConnectionFactory.EstablishAsync(options, endpoint, cts.Token));
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_throw_on_connection_refused()
    {
        // Stop listener to ensure connection is refused
        _listener!.Stop();

        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        await Assert.ThrowsAnyAsync<SocketException>(
            () => DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Disposing_lease_should_cancel_its_token()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);
        var token = lease.Token;

        Assert.False(token.IsCancellationRequested);

        lease.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public async Task Disposing_lease_should_mark_it_not_alive()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);

        Assert.True(lease.IsAlive);

        lease.Dispose();

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task Server_close_should_trigger_disposal()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var acceptTask = _listener!.AcceptTcpClientAsync(TestContext.Current.CancellationToken);

        var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint, TestContext.Current.CancellationToken);

        using var serverClient = await acceptTask;

        // Close server side — should trigger MoveStreamToPipe to see 0-byte read
        serverClient.Close();

        // Wait for the lease to be disposed by the onClose callback
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (lease.IsAlive && sw.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(lease.IsAlive);
    }

    [Fact(Timeout = 5000)]
    public async Task EstablishAsync_should_throw_on_null_options()
    {
        var endpoint = CreateEndpoint(80);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DirectConnectionFactory.EstablishAsync(null!, endpoint, TestContext.Current.CancellationToken));
    }
}
