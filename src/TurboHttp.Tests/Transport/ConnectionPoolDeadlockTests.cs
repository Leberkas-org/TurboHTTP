using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Tests.Transport;

/// <summary>
/// Tests transport-level starvation scenarios for <see cref="ConnectionPool"/>
/// and <see cref="ClientByteMover"/> to catch deadlocks in isolation.
/// </summary>
public sealed class ConnectionPoolDeadlockTests : IAsyncLifetime
{
    private TcpListener? _listener;
    private int _port;
    private readonly List<TcpClient> _acceptedClients = [];

    public ValueTask InitializeAsync()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var client in _acceptedClients)
        {
            client.Dispose();
        }

        _listener?.Stop();
        return ValueTask.CompletedTask;
    }

    private TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = _port,
        MaxFrameSize = 16384
    };

    private RequestEndpoint CreateEndpoint(Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = (ushort)_port,
        Scheme = "http",
        Version = version ?? HttpVersion.Version11
    };

    private async Task AcceptConnectionsAsync(int count, CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            var client = await _listener!.AcceptTcpClientAsync(ct);
            _acceptedClients.Add(client);
        }
    }

    #region DLTP-001: Semaphore released on abrupt close

    [Fact(Timeout = 5000, DisplayName = "DLTP-001: ConnectionPool semaphore released on abrupt close")]
    public async Task ConnectionPool_Semaphore_Released_On_AbruptClose()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        // Accept 2 connections in background (one for each acquire)
        var acceptTask = AcceptConnectionsAsync(2, cts.Token);

        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        // Acquire a connection
        var lease1 = await pool.AcquireAsync(options, endpoint, cts.Token);

        // Release with canReuse=false (simulating abrupt close)
        pool.Release(lease1, canReuse: false);

        // Second acquire should complete quickly — semaphore was released
        var secondAcquire = pool.AcquireAsync(options, endpoint, cts.Token);
        var completed = await Task.WhenAny(secondAcquire, Task.Delay(TimeSpan.FromSeconds(1), cts.Token));

        Assert.Same(secondAcquire, completed);

        var lease2 = await secondAcquire;
        pool.Release(lease2, canReuse: false);

        await acceptTask;
    }

    #endregion

    #region DLTP-002: AcquireAsync respects CancellationToken

    [Fact(Timeout = 5000, DisplayName = "DLTP-002: ConnectionPool AcquireAsync respects CancellationToken")]
    public async Task ConnectionPool_AcquireAsync_Respects_CancellationToken()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        // Accept 6 connections in background (fill all HTTP/1.1 slots)
        var acceptTask = AcceptConnectionsAsync(6, cts.Token);

        using var pool = new ConnectionPool(TimeSpan.FromSeconds(30));
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        // Fill all 6 connection slots
        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await pool.AcquireAsync(options, endpoint, cts.Token));
        }

        await acceptTask;

        // 7th acquire with 500ms timeout → OperationCanceledException, no indefinite wait
        using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => pool.AcquireAsync(options, endpoint, shortCts.Token));

        // Cleanup
        foreach (var lease in leases)
        {
            pool.Release(lease, canReuse: false);
        }
    }

    #endregion

    #region DLTP-003: ClientByteMover closeOnce fires when pump crashes

    [Fact(Timeout = 5000, DisplayName = "DLTP-003: ClientByteMover closeOnce fires when pump crashes")]
    public async Task ClientByteMover_CloseOnce_Fires_When_Pump_Crashes()
    {
        // Create a stream that throws on read — simulates crash in stream-to-pipe pump
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: new ThrowingStream(),
            inboundChannel: null,
            outboundChannel: null);

        using var cts = new CancellationTokenSource();
        var closeOnce = 0;
        var onClose = () =>
        {
            if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
            {
                cts.Cancel();
            }
        };

        // Start all 3 pump tasks
        var streamToPipe = ClientByteMover.MoveStreamToPipe(state, onClose, cts.Token);
        var pipeToChannel = ClientByteMover.MovePipeToChannel(state, onClose, cts.Token);
        var channelToStream = ClientByteMover.MoveChannelToStream(state, onClose, cts.Token);

        // All 3 tasks should complete (not hang)
        await Task.WhenAll(streamToPipe, pipeToChannel, channelToStream)
            .WaitAsync(TimeSpan.FromSeconds(2));

        // closeOnce fired — CancellationToken propagated to other pumps
        Assert.Equal(1, Volatile.Read(ref closeOnce));
        Assert.True(cts.IsCancellationRequested);
    }

    #endregion

    #region DLTP-004: All pumps exit on normal close

    [Fact(Timeout = 5000, DisplayName = "DLTP-004: ClientByteMover all pumps exit on normal close")]
    public async Task ClientByteMover_All_Pumps_Exit_On_Normal_Close()
    {
        // Empty MemoryStream → ReadAsync returns 0 (EOF) immediately
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: new MemoryStream(),
            inboundChannel: null,
            outboundChannel: null);

        using var cts = new CancellationTokenSource();
        var closeOnce = 0;
        var onClose = () =>
        {
            if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
            {
                cts.Cancel();
            }
        };

        var streamToPipe = ClientByteMover.MoveStreamToPipe(state, onClose, cts.Token);
        var pipeToChannel = ClientByteMover.MovePipeToChannel(state, onClose, cts.Token);
        var channelToStream = ClientByteMover.MoveChannelToStream(state, onClose, cts.Token);

        // All tasks should exit without hanging
        await Task.WhenAll(streamToPipe, pipeToChannel, channelToStream)
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    #endregion

    #region DLTP-005: ReadOnly ClientState write pump exits immediately

    [Fact(Timeout = 5000, DisplayName = "DLTP-005: ReadOnly ClientState write pump exits immediately")]
    public async Task ClientState_ReadOnly_Channel_PreCompleted_WriteExits_Immediately()
    {
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: new MemoryStream(),
            inboundChannel: null,
            outboundChannel: null,
            direction: StreamDirection.ReadOnly);

        using var cts = new CancellationTokenSource();
        var onClose = () => { };

        // Write pump should exit immediately — outbound channel is pre-completed
        var writePump = ClientByteMover.MoveChannelToStream(state, onClose, cts.Token);

        var completed = await Task.WhenAny(writePump, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(writePump, completed);

        // Pump completed without needing cancellation
        Assert.False(cts.IsCancellationRequested);
    }

    #endregion

    /// <summary>
    /// A stream that throws <see cref="IOException"/> on every read operation,
    /// simulating an abrupt connection failure in <see cref="ClientByteMover"/>.
    /// </summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Simulated connection failure");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Simulated connection failure"));

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }
}
