using TurboHTTP.Server;

namespace TurboHTTP.Routing;

internal interface IRouteDispatcher
{
    Task DispatchAsync(TurboHttpContext context, CancellationToken ct);
}
