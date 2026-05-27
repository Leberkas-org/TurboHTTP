using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

public sealed class EntityAskBuilder
{
    internal EntityResponseMapperCollection Mappers { get; } = new();
    internal TimeSpan? TimeoutOverride { get; private set; }

    public EntityAskBuilder Handle<TResponse>(Func<HttpContext, TResponse, Task> handler)
    {
        Mappers.Add(handler);
        return this;
    }

    public EntityAskBuilder WithTimeout(TimeSpan timeout)
    {
        TimeoutOverride = timeout;
        return this;
    }
}
