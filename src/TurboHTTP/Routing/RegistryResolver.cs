using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Routing.Resolvers;

public sealed class RegistryResolver<TKey> : IEntityActorResolver
{
    public ValueTask<IActorRef> ResolveAsync(string entityKey, IServiceProvider services, CancellationToken ct)
    {
        var registry = services.GetRequiredService<ActorRegistry>();
        return ValueTask.FromResult(registry.Get<TKey>());
    }
}
