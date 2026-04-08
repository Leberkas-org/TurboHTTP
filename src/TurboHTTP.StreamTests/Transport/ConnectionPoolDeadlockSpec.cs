using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;
using TurboHTTP.Transport.Connection;

namespace TurboHTTP.StreamTests.Transport;

/// <summary>
/// Tests transport-level starvation scenarios for <see cref="ConnectionManagerActor"/>
/// and <see cref="ClientByteMover"/> to catch deadlocks in isolation.
/// </summary>
public sealed class ConnectionPoolDeadlockSpec : IAsyncLifetime
{
    private ActorSystem? _system;
    private TcpListener? _listener;
    private int _port;
    private readonly List<TcpClient> _acceptedClients = [];

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("connection-deadlock-tests");
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _acceptedClients)
        {
            client.Dispose();
        }

        _listener?.Stop();
        if (_system is not null)
        {
            await _system.Terminate();
        }
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

    private IActorRef CreateActor()
        => _system!.ActorOf(Props.Create(() => new ConnectionManagerActor(TimeSpan.FromSeconds(30))));

    private async Task AcceptConnectionsAsync(int count, CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            var client = await _listener!.AcceptTcpClientAsync(ct);
            _acceptedClients.Add(client);
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionManagerActor_should_free_slot_on_abrupt_close()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        var acceptTask = AcceptConnectionsAsync(2, cts.Token);

        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        actor.Tell(new ConnectionManagerActor.Release(lease1, CanReuse: false));

        var secondAcquire = ConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token);
        var completed = await Task.WhenAny(secondAcquire, Task.Delay(TimeSpan.FromSeconds(1), cts.Token));

        Assert.Same(secondAcquire, completed);

        var lease2 = await secondAcquire;
        actor.Tell(new ConnectionManagerActor.Release(lease2, CanReuse: false));

        await acceptTask;
    }

    [Fact(Timeout = 5000)]
    public async Task ConnectionManagerActor_should_respect_cancellation_token()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        var acceptTask = AcceptConnectionsAsync(6, cts.Token);

        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await ConnectionManagerActor.AcquireAsync(actor, options, endpoint, cts.Token));
        }

        await acceptTask;

        using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ConnectionManagerActor.AcquireAsync(actor, options, endpoint, shortCts.Token));

        foreach (var lease in leases)
        {
            actor.Tell(new ConnectionManagerActor.Release(lease, CanReuse: false));
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_fire_close_once_when_pump_crashes()
    {
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: new ThrowingStream(),
            inboundChannel: null,
            outboundChannel: null);

        using var byteMoverCts = new CancellationTokenSource();
        var closeOnce = 0;
        var onClose = () =>
        {
            if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
            {
                byteMoverCts.Cancel();
            }
        };

        var streamToChannel = ClientByteMover.MoveStreamToChannel(state, onClose, byteMoverCts.Token);
        var channelToStream = ClientByteMover.MoveChannelToStream(state, onClose, byteMoverCts.Token);

        await Task.WhenAll(streamToChannel, channelToStream)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref closeOnce));
        Assert.True(byteMoverCts.IsCancellationRequested);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_exit_all_pumps_on_normal_close()
    {
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: new MemoryStream(),
            inboundChannel: null,
            outboundChannel: null);

        using var byteMoverCts = new CancellationTokenSource();
        var closeOnce = 0;
        var onClose = () =>
        {
            if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
            {
                byteMoverCts.Cancel();
            }
        };

        var streamToChannel = ClientByteMover.MoveStreamToChannel(state, onClose, byteMoverCts.Token);
        var channelToStream = ClientByteMover.MoveChannelToStream(state, onClose, byteMoverCts.Token);

        await Task.WhenAll(streamToChannel, channelToStream)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task ClientState_should_exit_write_pump_immediately_when_read_only()
    {
        var state = new ClientState(
            maxFrameSize: 16384,
            stream: new MemoryStream(),
            inboundChannel: null,
            outboundChannel: null,
            direction: StreamDirection.ReadOnly);

        using var byteMoverCts = new CancellationTokenSource();
        var onClose = () => { };

        var writePump = ClientByteMover.MoveChannelToStream(state, onClose, byteMoverCts.Token);

        var completed = await Task.WhenAny(writePump,
            Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.Same(writePump, completed);

        Assert.False(byteMoverCts.IsCancellationRequested);
    }

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

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }
}
