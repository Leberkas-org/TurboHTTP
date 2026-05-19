using TurboHTTP.Server;

namespace TurboHTTP.Routing;

internal sealed class DelegateDispatcher(Func<TurboHttpContext, Task> handler) : IRouteDispatcher
{
    public Task DispatchAsync(TurboHttpContext context, CancellationToken ct)
    {
        return handler(context);
    }
}
