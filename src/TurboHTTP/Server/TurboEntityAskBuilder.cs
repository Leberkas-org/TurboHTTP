using Microsoft.AspNetCore.Http;
using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public sealed class TurboEntityAskBuilder
{
    internal EntityResponseMapperCollection Mappers { get; } = new();
    internal TimeSpan? TimeoutOverride { get; private set; }

    public TurboEntityAskBuilder Handle<TResponse>(Func<TurboHttpContext, TResponse, Task> handler)
    {
        Mappers.Add(handler);
        return this;
    }

    public TurboEntityAskBuilder Produces<TResponse>(Func<TurboHttpContext, TResponse, ITurboResult> handler)
    {
        Mappers.Add<TResponse>(async (ctx, resp) => await handler(ctx, resp).ExecuteAsync(ctx));
        return this;
    }

    public TurboEntityAskBuilder WithTimeout(TimeSpan timeout)
    {
        TimeoutOverride = timeout;
        return this;
    }
}
