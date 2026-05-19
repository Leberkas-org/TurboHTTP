using Akka.Hosting;
using Akka.TestKit.Xunit;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Entity.Resolvers;

namespace TurboHTTP.Tests.Server.Entity.Resolvers;

public sealed class RegistryResolverSpec : TestKit
{
    private sealed class OrderActorKey;

    [Fact(Timeout = 5000)]
    public async Task ResolveAsync_should_return_actor_from_registry()
    {
        var probe = CreateTestProbe();
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(probe.Ref);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var resolver = new RegistryResolver<OrderActorKey>();
        var result = await resolver.ResolveAsync("any-key", services, CancellationToken.None);

        Assert.Equal(probe.Ref, result);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveAsync_should_ignore_entity_key()
    {
        var probe = CreateTestProbe();
        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(probe.Ref);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var resolver = new RegistryResolver<OrderActorKey>();
        var first = await resolver.ResolveAsync("key-1", services, CancellationToken.None);
        var second = await resolver.ResolveAsync("key-2", services, CancellationToken.None);

        Assert.Same(first, second);
    }
}