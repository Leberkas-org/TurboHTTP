using Akka.Actor;

namespace Servus.Akka.AspNetCore;

public interface IEntityActorResolver
{
    ValueTask<IActorRef> ResolveAsync(IServiceProvider services, CancellationToken cancellationToken);
}
