using System.Net;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class PoolRouterActorTests : TestKit
{
    private static TcpOptions MakeOptions(string host = "localhost", int port = 8080)
        => new() { Host = host, Port = port };

    private static HostKey MakeKey(TcpOptions options)
        => new ConnectItem(options, HttpVersion.Version11).Key;

    // ── PR-001: EnsureHost creates a HostPoolActor ──────────────────────────

    [Fact(DisplayName = "PR-001: EnsureHost creates a HostPoolActor and forwards the message")]
    public void PR_001_EnsureHost_CreatesHostPoolActor()
    {
        var hostProbe = CreateTestProbe();
        var router = Sys.ActorOf(Props.Create(() =>
            new PoolRouterActor(null, (opts, cfg, key) => hostProbe.Ref)));

        var options = MakeOptions();
        var key = MakeKey(options);

        router.Tell(new PoolRouterActor.EnsureHost(key, options));

        // EnsureHost is forwarded to the host actor (for ConnectionHandle replies)
        hostProbe.ExpectMsg<PoolRouterActor.EnsureHost>(TimeSpan.FromSeconds(5));
    }

    // ── PR-002: Same key reuses the existing HostPoolActor ────────────────────

    [Fact(DisplayName = "PR-002: EnsureHost with the same key reuses the existing HostPoolActor (factory called once)")]
    public void PR_002_SameKey_ReusesHostPoolActor()
    {
        var factoryCallCount = 0;
        var hostProbe = CreateTestProbe();
        Func<TcpOptions, PoolConfig, HostKey, IActorRef> factory = (_, _, _) =>
        {
            Interlocked.Increment(ref factoryCallCount);
            return hostProbe.Ref;
        };
        var router = Sys.ActorOf(Props.Create(() => new PoolRouterActor(null, factory)));

        var options = MakeOptions();
        var key = MakeKey(options);

        router.Tell(new PoolRouterActor.EnsureHost(key, options));
        router.Tell(new PoolRouterActor.EnsureHost(key, options));

        // Both EnsureHost messages forwarded to the same host probe
        hostProbe.ExpectMsg<PoolRouterActor.EnsureHost>(TimeSpan.FromSeconds(5));
        hostProbe.ExpectMsg<PoolRouterActor.EnsureHost>(TimeSpan.FromSeconds(5));

        Assert.Equal(1, factoryCallCount);
    }

    // ── PR-003: Different keys create separate HostPoolActors ─────────────────

    [Fact(DisplayName = "PR-003: EnsureHost with different keys creates separate HostPoolActors")]
    public void PR_003_DifferentKeys_CreateSeparateActors()
    {
        var probeA = CreateTestProbe();
        var probeB = CreateTestProbe();
        var callCount = 0;

        Func<TcpOptions, PoolConfig, HostKey, IActorRef> factory =
            (_, _, _) => Interlocked.Increment(ref callCount) == 1 ? probeA.Ref : probeB.Ref;

        var router = Sys.ActorOf(Props.Create(() => new PoolRouterActor(null, factory)));

        var optionsA = MakeOptions("host-a", 80);
        var keyA = MakeKey(optionsA);
        var optionsB = MakeOptions("host-b", 80);
        var keyB = MakeKey(optionsB);

        router.Tell(new PoolRouterActor.EnsureHost(keyA, optionsA));
        router.Tell(new PoolRouterActor.EnsureHost(keyB, optionsB));

        // Each EnsureHost forwarded to its respective host probe
        probeA.ExpectMsg<PoolRouterActor.EnsureHost>(TimeSpan.FromSeconds(5));
        probeB.ExpectMsg<PoolRouterActor.EnsureHost>(TimeSpan.FromSeconds(5));

        Assert.Equal(2, callCount);
    }
}
