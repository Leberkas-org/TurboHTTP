using TurboHTTP.Server;

namespace TurboHTTP.Routing;

internal sealed class DelegateDispatcher : IRouteDispatcher
{
    private readonly Func<TurboHttpContext, Task> _handler;

    public DelegateDispatcher(Func<TurboHttpContext, Task> handler)
    {
        _handler = handler;
    }

    public Task DispatchAsync(TurboHttpContext context, CancellationToken ct)
    {
        return _handler(context);
    }
}
