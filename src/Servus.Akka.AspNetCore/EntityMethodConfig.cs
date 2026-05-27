using Microsoft.AspNetCore.Http;

namespace Servus.Akka.AspNetCore;

internal sealed record EntityMethodConfig(
    Delegate MessageFactory,
    bool IsTell,
    TimeSpan? TimeoutOverride,
    EntityResponseMapperCollection? EndpointMappers,
    Func<HttpContext, Task>? TellResponseHandler);
