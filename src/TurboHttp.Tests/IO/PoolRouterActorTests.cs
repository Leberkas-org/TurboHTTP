using System.Buffers;
using Akka.Actor;
using Akka.DependencyInjection;
using Akka.Event;
using Akka.TestKit.Xunit2;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class PoolRouterActorTests : TestKit
{
    public PoolRouterActorTests() : base(CreateSystem())
    {
    }

    private static ActorSystem CreateSystem()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TcpOptions { Host = "test", Port = 80 });
        services.AddSingleton(new PoolConfig());
        services.AddSingleton<IActorRef>(ActorRefs.Nobody);
        var provider = services.BuildServiceProvider();
        var diSetup = DependencyResolverSetup.Create(provider);

        return ActorSystem.Create(
            "pool-router-test-" + Guid.NewGuid().ToString("N")[..8],
            BootstrapSetup.Create().And(diSetup));
    }

    private static TcpOptions MakeOptions(string host = "localhost", int port = 80)
        => new() { Host = host, Port = port };

    private static DataItem MakeDataItem()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        return new DataItem(owner, 16);
    }

    private IActorRef CreateRouter()
        => Sys.ActorOf(Props.Create(() => new PoolRouterActor()));

    [Fact(DisplayName = "PRA-001: RegisterHost creates a child actor for the given PoolKey")]
    public void PRA_001_RegisterHost_CreatesChildActor()
    {
        var router = CreateRouter();
        var poolKey = "localhost:80";

        router.Tell(new PoolRouterActor.RegisterHost(poolKey, MakeOptions()));

        // Allow message processing
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Verify child exists via Identify
        Sys.ActorSelection(router.Path / poolKey).Tell(new Identify(1));
        var identity = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3));

        Assert.NotNull(identity.Subject);
    }

    [Fact(DisplayName = "PRA-002: Duplicate RegisterHost with same PoolKey is silently ignored")]
    public void PRA_002_DuplicateRegisterHost_SilentlyIgnored()
    {
        var router = CreateRouter();
        var poolKey = "localhost:80";
        var options = MakeOptions();

        router.Tell(new PoolRouterActor.RegisterHost(poolKey, options));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Get initial child ref
        Sys.ActorSelection(router.Path / poolKey).Tell(new Identify(1));
        var firstChild = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject;

        // Send duplicate registration
        router.Tell(new PoolRouterActor.RegisterHost(poolKey, options));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Child should be the exact same ref (no second child created)
        Sys.ActorSelection(router.Path / poolKey).Tell(new Identify(2));
        var secondChild = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject;

        Assert.NotNull(firstChild);
        Assert.Equal(firstChild, secondChild);
    }

    [Fact(DisplayName = "PRA-003: Different PoolKeys create different child actors")]
    public void PRA_003_DifferentPoolKeys_CreateDifferentChildren()
    {
        var router = CreateRouter();

        router.Tell(new PoolRouterActor.RegisterHost("host-a:80", MakeOptions("host-a")));
        router.Tell(new PoolRouterActor.RegisterHost("host-b:443", MakeOptions("host-b", 443)));

        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        Sys.ActorSelection(router.Path / "host-a:80").Tell(new Identify(1));
        var childA = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject;

        Sys.ActorSelection(router.Path / "host-b:443").Tell(new Identify(2));
        var childB = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject;

        Assert.NotNull(childA);
        Assert.NotNull(childB);
        Assert.NotEqual(childA, childB);
    }

    // --- TASK-004: Request Routing Tests ---

    [Fact(DisplayName = "PRA-004: SendRequest with registered PoolKey is forwarded to the correct HostPoolActor")]
    public void PRA_004_SendRequest_RegisteredPoolKey_ForwardedToHostPool()
    {
        var router = CreateRouter();
        var poolKey = "localhost:80";

        router.Tell(new PoolRouterActor.RegisterHost(poolKey, MakeOptions()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Resolve the child HostPoolActor ref
        Sys.ActorSelection(router.Path / poolKey).Tell(new Identify(1));
        var hostPool = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject!;

        // Subscribe to UnhandledMessage to observe forwarding
        // (HostPoolActor doesn't handle SendRequest, so it becomes unhandled)
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var data = MakeDataItem();
        router.Tell(new PoolRouterActor.SendRequest(poolKey, data, TestActor));

        var unhandled = ExpectMsg<UnhandledMessage>(TimeSpan.FromSeconds(3));
        Assert.IsType<PoolRouterActor.SendRequest>(unhandled.Message);
        Assert.Equal(hostPool, unhandled.Recipient);
    }

    [Fact(DisplayName = "PRA-005: SendRequest with unknown PoolKey replies with Status.Failure")]
    public void PRA_005_SendRequest_UnknownPoolKey_RepliesWithFailure()
    {
        var router = CreateRouter();
        var data = MakeDataItem();

        router.Tell(new PoolRouterActor.SendRequest("unknown:80", data, TestActor));

        var failure = ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(3));
        var ex = Assert.IsType<InvalidOperationException>(failure.Cause);
        Assert.Equal("Unknown host", ex.Message);
    }

    [Fact(DisplayName = "PRA-006: Multiple hosts — request routed to correct host based on PoolKey")]
    public void PRA_006_MultipleHosts_RequestRoutedToCorrectHost()
    {
        var router = CreateRouter();

        router.Tell(new PoolRouterActor.RegisterHost("host-a:80", MakeOptions("host-a")));
        router.Tell(new PoolRouterActor.RegisterHost("host-b:443", MakeOptions("host-b", 443)));
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Resolve both children
        Sys.ActorSelection(router.Path / "host-a:80").Tell(new Identify(1));
        var hostA = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject!;

        Sys.ActorSelection(router.Path / "host-b:443").Tell(new Identify(2));
        var hostB = ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(3)).Subject!;

        // Subscribe to UnhandledMessage
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        // Send to host-b
        var data = MakeDataItem();
        router.Tell(new PoolRouterActor.SendRequest("host-b:443", data, TestActor));

        var unhandled = ExpectMsg<UnhandledMessage>(TimeSpan.FromSeconds(3));
        Assert.IsType<PoolRouterActor.SendRequest>(unhandled.Message);
        Assert.Equal(hostB, unhandled.Recipient);
        Assert.NotEqual(hostA, unhandled.Recipient);
    }

    [Fact(DisplayName = "PRA-007: Sender is preserved through Forward — original sender receives failure")]
    public void PRA_007_SenderPreserved_OriginalSenderReceivesFailure()
    {
        var router = CreateRouter();
        var originalSender = CreateTestProbe();

        var data = MakeDataItem();

        // Send from a specific probe (not TestActor) to verify sender preservation
        router.Tell(new PoolRouterActor.SendRequest("not-registered:80", data, originalSender), originalSender);

        // The failure must arrive at the original sender, not TestActor
        var failure = originalSender.ExpectMsg<Status.Failure>(TimeSpan.FromSeconds(3));
        var ex = Assert.IsType<InvalidOperationException>(failure.Cause);
        Assert.Equal("Unknown host", ex.Message);

        // TestActor should NOT receive anything
        ExpectNoMsg(TimeSpan.FromMilliseconds(200));
    }
}
