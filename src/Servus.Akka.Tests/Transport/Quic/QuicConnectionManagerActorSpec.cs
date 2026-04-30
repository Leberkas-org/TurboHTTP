using Akka.Actor;
using Akka.TestKit.Xunit;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

[Collection("TransportBuffer")]
public sealed class QuicConnectionManagerActorSpec : TestKit
{
    private static QuicTransportOptions CreateOptions() => new()
    {
        Host = "localhost",
        Port = 443
    };

    private static IQuicConnectionFactory CreateMockFactory(bool shouldFail = false, int maxStreams = 100)
    {
        return new MockFactory(shouldFail, maxStreams);
    }

    private IActorRef CreateActor(IQuicConnectionFactory? factory = null)
    {
        var f = factory ?? CreateMockFactory();
        return Sys.ActorOf(Props.Create(() => new QuicConnectionManagerActor(f)));
    }

    private sealed class MockFactory : IQuicConnectionFactory
    {
        private readonly bool _shouldFail;
        private readonly int _maxStreams;

        public int EstablishCount { get; private set; }

        public MockFactory(bool shouldFail = false, int maxStreams = 100)
        {
            _shouldFail = shouldFail;
            _maxStreams = maxStreams;
        }

        public Task<QuicConnectionLease> EstablishAsync(QuicTransportOptions options, CancellationToken ct = default)
        {
            EstablishCount++;
            if (_shouldFail)
            {
                return Task.FromException<QuicConnectionLease>(new IOException("Simulated failure"));
            }

            var handle = new QuicConnectionHandle(
                openStream: (_, _) => Task.FromResult((Stream: (Stream)new MemoryStream(), StreamId: 0L)),
                acceptInboundStream: _ => Task.FromResult<(Stream, long)?>(null),
                getLocalEndPoint: () => null,
                dispose: () => ValueTask.CompletedTask);
            return Task.FromResult(new QuicConnectionLease(handle, options.MaxBidirectionalStreams));
        }
    }

    [Fact(Timeout = 5000)]
    public async Task AcquireAsync_should_return_lease()
    {
        var actor = CreateActor();
        var options = CreateOptions();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options,
            TestContext.Current.CancellationToken);

        Assert.NotNull(lease);
        Assert.True(lease.IsAlive());

        await lease.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task AcquireAsync_should_call_factory_EstablishAsync()
    {
        var factory = new MockFactory();
        var actor = CreateActor(factory);
        var options = CreateOptions();

        Assert.Equal(0, factory.EstablishCount);

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, factory.EstablishCount);

        await lease.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task AcquireAsync_should_fail_when_factory_throws()
    {
        var factory = new MockFactory(shouldFail: true);
        var actor = CreateActor(factory);
        var options = CreateOptions();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            QuicConnectionManagerActor.AcquireAsync(actor, options, TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 5000)]
    public async Task Release_with_CanReuse_true_should_not_dispose()
    {
        var actor = CreateActor();
        var options = CreateOptions();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options,
            TestContext.Current.CancellationToken);

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: true));

        Assert.True(lease.IsAlive());

        await lease.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Release_with_CanReuse_false_should_dispose()
    {
        var actor = CreateActor();
        var options = CreateOptions();

        var lease = await QuicConnectionManagerActor.AcquireAsync(actor, options,
            TestContext.Current.CancellationToken);

        actor.Tell(new QuicConnectionManagerActor.Release(lease, CanReuse: false));

        AwaitCondition(() => !lease.IsAlive(), TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(lease.IsAlive());
    }

    [Fact(Timeout = 5000)]
    public async Task Multiple_acquires_should_create_multiple_connections()
    {
        var factory = new MockFactory();
        var actor = CreateActor(factory);
        var options = CreateOptions() with
        {
            MaxBidirectionalStreams = 1,
            MaxConnectionsPerHost = 2
        };

        var lease1 = await QuicConnectionManagerActor.AcquireAsync(actor, options,
            TestContext.Current.CancellationToken);
        var lease2 = await QuicConnectionManagerActor.AcquireAsync(actor, options,
            TestContext.Current.CancellationToken);

        Assert.NotSame(lease1, lease2);
        Assert.Equal(2, factory.EstablishCount);

        await lease1.DisposeAsync();
        await lease2.DisposeAsync();
    }

    [Fact(Timeout = 5000)]
    public async Task Acquire_should_respect_cancellation()
    {
        var actor = CreateActor();
        var options = CreateOptions();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            QuicConnectionManagerActor.AcquireAsync(actor, options, cts.Token));
    }
}
