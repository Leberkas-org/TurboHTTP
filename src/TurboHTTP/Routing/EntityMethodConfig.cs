using TurboHTTP.Server;

namespace TurboHTTP.Routing;

internal sealed record EntityMethodConfig(
    Func<TurboHttpContext, IServiceProvider, ValueTask<object>> MessageFactory,
    bool IsTell,
    TimeSpan? TimeoutOverride,
    EntityResponseMapperCollection? EndpointMappers,
    Func<TurboHttpContext, Task>? TellResponseHandler);
