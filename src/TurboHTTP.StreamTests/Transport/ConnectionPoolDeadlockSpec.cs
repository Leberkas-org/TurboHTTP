using System.Net;
using Akka.Actor;
using TurboHTTP.Internal;
using TurboHTTP.Tests.Shared;
using TurboHTTP.Transport.Connection;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.StreamTests.Transport;

/// <summary>
/// Tests transport-level starvation scenarios for <see cref="TcpConnectionManagerActor"/>
/// and <see cref="ClientByteMover"/> to catch deadlocks in isolation.
/// </summary>
public sealed class ConnectionPoolDeadlockSpec : IAsyncLifetime
{
    private ActorSystem? _system;
    private InMemoryConnectionFactory _factory = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("connection-deadlock-tests");
        _factory = new InMemoryConnectionFactory();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_system is not null)
        {
            await _system.Terminate();
        }
    }

    private static TcpOptions CreateOptions() => new()
    {
        Host = "127.0.0.1",
        Port = 8080
    };

    private static RequestEndpoint CreateEndpoint(Version? version = null) => new()
    {
        Host = "127.0.0.1",
        Port = 8080,
        Scheme = "http",
        Version = version ?? HttpVersion.Version11
    };

    private IActorRef CreateActor()
        => _system!.ActorOf(Props.Create(() =>
            new TcpConnectionManagerActor(_factory, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan)));

    [Fact(Timeout = 5000)]
    public async Task TcpConnectionManagerActor_should_free_slot_on_abrupt_close()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var lease1 = await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        actor.Tell(new TcpConnectionManagerActor.Release(lease1, CanReuse: false));

        var secondAcquire = TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(secondAcquire, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        Assert.Same(secondAcquire, completed);

        var lease2 = await secondAcquire;
        actor.Tell(new TcpConnectionManagerActor.Release(lease2, CanReuse: false));
    }

    [Fact(Timeout = 5000)]
    public async Task TcpConnectionManagerActor_should_respect_cancellation_token()
    {
        var actor = CreateActor();
        var options = CreateOptions();
        var endpoint = CreateEndpoint(HttpVersion.Version11);

        var leases = new List<ConnectionLease>();
        for (var i = 0; i < 6; i++)
        {
            leases.Add(await TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, TestContext.Current.CancellationToken));
        }

        using var shortCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TcpConnectionManagerActor.AcquireAsync(actor, options, endpoint, shortCts.Token));

        foreach (var lease in leases)
        {
            actor.Tell(new TcpConnectionManagerActor.Release(lease, CanReuse: false));
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ClientByteMover_should_fire_close_once_when_pump_crashes()
    {
        var state = new ClientState(
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
