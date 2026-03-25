using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests <see cref="DirectConnectionFactory"/> establishing connections,
/// handling cancellation, connection errors, and disposal cleanup.
/// </summary>
public sealed class DirectConnectionFactoryTests : IAsyncLifetime
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

    #region Happy path

    [Fact(DisplayName = "TASK-026-003-001: EstablishAsync returns live ConnectionLease", Timeout = 5000)]
    public async Task EstablishAsync_ReturnsLiveLease()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive);
        Assert.True(lease.Reusable);
        Assert.Equal(endpoint, lease.Key);
    }

    [Fact(DisplayName = "TASK-026-003-002: Lease Handle uses ActorRefs.Nobody", Timeout = 5000)]
    public async Task EstablishAsync_HandleUsesNobody()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);

        Assert.Equal(ActorRefs.Nobody, lease.Handle.ConnectionActor);
    }

    [Fact(DisplayName = "TASK-026-003-003: Data flows through ByteMover pumps", Timeout = 5000)]
    public async Task EstablishAsync_DataFlowsThroughPumps()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        // Accept the connection server-side
        var acceptTask = _listener!.AcceptTcpClientAsync();

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);

        using var serverClient = await acceptTask;
        var serverStream = serverClient.GetStream();

        // Server sends data → should appear on lease's InboundReader
        var testData = "Hello from server"u8.ToArray();
        await serverStream.WriteAsync(testData);
        await serverStream.FlushAsync();

        // Read from the inbound reader (data flows: TCP → Pipe → Channel)
        var readResult = await lease.Handle.InboundReader.ReadAsync();
        var (buffer, readableBytes) = readResult;
        try
        {
            Assert.True(readableBytes > 0);
            Assert.Equal(testData, buffer.Memory[..readableBytes].ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact(DisplayName = "TASK-026-003-004: Outbound data reaches server", Timeout = 5000)]
    public async Task EstablishAsync_OutboundDataReachesServer()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var acceptTask = _listener!.AcceptTcpClientAsync();

        using var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);

        using var serverClient = await acceptTask;
        var serverStream = serverClient.GetStream();

        // Write data to outbound → should arrive at server
        var testData = "Hello from client"u8.ToArray();
        var pooled = MemoryPool<byte>.Shared.Rent(testData.Length);
        testData.CopyTo(pooled.Memory.Span);
        Assert.True(lease.Handle.OutboundWriter.TryWrite((pooled, testData.Length)));

        // Read from server side
        var readBuf = new byte[1024];
        var bytesRead = await serverStream.ReadAsync(readBuf);

        Assert.Equal(testData, readBuf[..bytesRead]);
    }

    [Fact(DisplayName = "TASK-026-003-005: MaxConcurrentStreams matches version defaults", Timeout = 5000)]
    public async Task EstablishAsync_MaxConcurrentStreamsMatchesVersion()
    {
        var options = CreateOptions();

        // HTTP/1.0 → 1
        var endpoint10 = CreateEndpoint(_port, HttpVersion.Version10);
        using var lease10 = await DirectConnectionFactory.EstablishAsync(options, endpoint10);
        Assert.Equal(1, lease10.MaxConcurrentStreams);

        // HTTP/1.1 → 6
        var endpoint11 = CreateEndpoint(_port, HttpVersion.Version11);
        using var lease11 = await DirectConnectionFactory.EstablishAsync(options, endpoint11);
        Assert.Equal(6, lease11.MaxConcurrentStreams);

        // HTTP/2 → 100
        var endpoint20 = CreateEndpoint(_port, HttpVersion.Version20);
        using var lease20 = await DirectConnectionFactory.EstablishAsync(options, endpoint20);
        Assert.Equal(100, lease20.MaxConcurrentStreams);
    }

    #endregion

    #region Cancellation

    [Fact(DisplayName = "TASK-026-003-006: EstablishAsync throws on pre-cancelled token", Timeout = 5000)]
    public async Task EstablishAsync_ThrowsOnPreCancelledToken()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DirectConnectionFactory.EstablishAsync(options, endpoint, cts.Token));
    }

    [Fact(DisplayName = "TASK-026-003-007: EstablishAsync throws when cancelled during connect", Timeout = 5000)]
    public async Task EstablishAsync_ThrowsWhenCancelledDuringConnect()
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

    #endregion

    #region Connection refused

    [Fact(DisplayName = "TASK-026-003-008: EstablishAsync throws on connection refused", Timeout = 5000)]
    public async Task EstablishAsync_ThrowsOnConnectionRefused()
    {
        // Stop listener to ensure connection is refused
        _listener!.Stop();

        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        await Assert.ThrowsAnyAsync<SocketException>(
            () => DirectConnectionFactory.EstablishAsync(options, endpoint));
    }

    #endregion

    #region Dispose cleanup

    [Fact(DisplayName = "TASK-026-003-009: Disposing lease cancels its token", Timeout = 5000)]
    public async Task DisposingLease_CancelsToken()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);
        var token = lease.Token;

        Assert.False(token.IsCancellationRequested);

        lease.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact(DisplayName = "TASK-026-003-010: Disposing lease marks it not alive", Timeout = 5000)]
    public async Task DisposingLease_MarksNotAlive()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);

        Assert.True(lease.IsAlive);

        lease.Dispose();

        Assert.False(lease.IsAlive);
    }

    [Fact(DisplayName = "TASK-026-003-011: Server close triggers onClose and disposes lease", Timeout = 5000)]
    public async Task ServerClose_TriggersDisposal()
    {
        var options = CreateOptions();
        var endpoint = CreateEndpoint(_port);

        var acceptTask = _listener!.AcceptTcpClientAsync();

        var lease = await DirectConnectionFactory.EstablishAsync(options, endpoint);

        using var serverClient = await acceptTask;

        // Close server side — should trigger MoveStreamToPipe to see 0-byte read
        serverClient.Close();

        // Wait for the lease to be disposed by the onClose callback
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (lease.IsAlive && sw.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(50);
        }

        Assert.False(lease.IsAlive);
    }

    #endregion

    #region Null argument validation

    [Fact(DisplayName = "TASK-026-003-012: EstablishAsync throws on null options", Timeout = 5000)]
    public async Task EstablishAsync_ThrowsOnNullOptions()
    {
        var endpoint = CreateEndpoint(80);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DirectConnectionFactory.EstablishAsync(null!, endpoint));
    }

    #endregion
}
