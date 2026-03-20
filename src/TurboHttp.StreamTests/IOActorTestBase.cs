using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.Lifecycle;

namespace TurboHttp.StreamTests;

/// <summary>
/// Abstract base class for I/O actor tests.
/// Provides factory helpers for constructing <see cref="HostPool"/>, <see cref="ConnectionActor"/>, and <see cref="ConnectionHandle"/> test doubles.
/// </summary>
/// <remarks>
/// Inherits from TestKit; all derived tests use Akka's TestProbe infrastructure for actor interaction verification.
/// </remarks>
public abstract class IOActorTestBase : TestKit
{
    protected static readonly TcpOptions TestOptions = new() { Host = "localhost", Port = 8080 };

    protected static readonly RequestEndpoint Key10 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version10
    };

    protected static readonly RequestEndpoint Key11 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version11
    };

    protected static readonly RequestEndpoint Key20 = new()
    {
        Host = "localhost", Port = 8080, Scheme = "http", Version = HttpVersion.Version20
    };

    /// <summary>
    /// Minimal fake connection actor: reports its <see cref="IActorRef"/> to a control
    /// probe on startup, then stays idle (no reconnect logic).
    /// </summary>
    protected sealed class FakeConnectionActor : ReceiveActor
    {
        private readonly IActorRef _controlProbe;

        public FakeConnectionActor(IActorRef controlProbe)
        {
            _controlProbe = controlProbe;
        }

        protected override void PreStart()
        {
            _controlProbe.Tell(Self);
        }
    }

    /// <summary>Creates a <see cref="HostPool"/> whose children are fakes controlled via
    /// <paramref name="controlProbe"/>. Each new child sends its ref to the probe.</summary>
    protected IActorRef CreatePool(TestProbe controlProbe, RequestEndpoint key, TimeSpan? reconnectInterval = null)
    {
        var config = new TurboClientOptions
        {
            ReconnectInterval = reconnectInterval ?? TimeSpan.FromSeconds(60),
            IdleTimeout = TimeSpan.FromSeconds(60),
            MaxReconnectAttempts = 3
        };

        var hostConfig = new HostPool.HostPoolConfig(
            TestOptions,
            config,
            key,
            ConnectionFactory: () => Props.Create(() => new FakeConnectionActor(controlProbe.Ref)));

        return Sys.ActorOf(Props.Create(() => new HostPool(hostConfig)));
    }

    /// <summary>Builds a <see cref="ConnectionHandle"/> backed by in-memory channels.</summary>
    protected static ConnectionHandle CreateHandle(IActorRef connectionActor, RequestEndpoint key)
    {
        var outbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        return new ConnectionHandle(outbound.Writer, inbound.Reader, key, connectionActor);
    }

    /// <summary>
    /// Creates a pool, waits for the eagerly-spawned connection actor, delivers
    /// <see cref="ConnectionActor.ConnectionReady"/>, and returns all three pieces.
    /// </summary>
    protected (IActorRef Pool, IActorRef FakeConn, ConnectionHandle Handle) SetupReadyPool(
        TestProbe controlProbe, RequestEndpoint key, TimeSpan? reconnectInterval = null)
    {
        var pool = CreatePool(controlProbe, key, reconnectInterval);
        var fakeConn = controlProbe.ExpectMsg<IActorRef>(TimeSpan.FromSeconds(5));
        var handle = CreateHandle(fakeConn, key);
        pool.Tell(new ConnectionActor.ConnectionReady(handle), fakeConn);
        return (pool, fakeConn, handle);
    }
}
