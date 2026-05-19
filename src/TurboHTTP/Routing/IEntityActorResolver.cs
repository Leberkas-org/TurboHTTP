using Akka.Actor;

namespace TurboHTTP.Routing;

public interface IEntityActorResolver
{
    ValueTask<IActorRef> ResolveAsync(string entityKey, IServiceProvider services, CancellationToken ct);
}
