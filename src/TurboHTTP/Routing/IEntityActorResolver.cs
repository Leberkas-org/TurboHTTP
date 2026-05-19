using Akka.Actor;

namespace TurboHTTP.Routing;

public interface IEntityActorResolver
{
    ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken);
}