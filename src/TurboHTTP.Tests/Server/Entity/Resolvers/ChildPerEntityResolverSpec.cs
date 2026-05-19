using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit.Xunit;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Entity.Resolvers;

namespace TurboHTTP.Tests.Server.Entity.Resolvers;

public sealed class ChildPerEntityResolverSpec : TestKit
{
    private sealed class OrderActorKey;

    private sealed class ParentActor : ReceiveActor
    {
        public ParentActor()
        {
            Receive<ChildPerEntityResolver<OrderActorKey>.GetOrCreateChild>(msg =>
            {
                var child = Context.Child(msg.EntityKey);
                if (child.IsNobody())
                {
                    child = Context.ActorOf(Props.Create(() => new ChildActor()), msg.EntityKey);
                }
                Sender.Tell(child);
            });
        }
    }

    private sealed class ChildActor : ReceiveActor
    {
        public ChildActor()
        {
            ReceiveAny(_ => Sender.Tell("pong"));
        }
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveAsync_should_ask_parent_for_child_by_entity_key()
    {
        var parent = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent");

        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(parent);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var resolver = new ChildPerEntityResolver<OrderActorKey>();
        var child = await resolver.ResolveAsync("order-42", services, CancellationToken.None);

        Assert.NotNull(child);
        Assert.NotEqual(parent, child);

        child.Tell("ping", TestActor);
        ExpectMsg<string>("pong", cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task ResolveAsync_should_return_same_child_for_same_key()
    {
        var parent = Sys.ActorOf(Props.Create(() => new ParentActor()), "parent2");

        var registry = new ActorRegistry();
        registry.Register<OrderActorKey>(parent);

        var services = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();

        var resolver = new ChildPerEntityResolver<OrderActorKey>();
        var first = await resolver.ResolveAsync("order-1", services, CancellationToken.None);
        var second = await resolver.ResolveAsync("order-1", services, CancellationToken.None);

        Assert.Equal(first, second);
    }
}
