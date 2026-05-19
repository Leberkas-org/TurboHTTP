using TurboHTTP.Server;
using TurboHTTP.Server.Binding;

namespace TurboHTTP.Routing;

public sealed class TurboRouteTable
{
    private readonly List<RouteEntry> _entries = [];
    private RouteTable? _frozen;

    public TurboRouteHandlerBuilder Add(HttpMethod method, string pattern, Func<TurboHttpContext, Task> handler)
    {
        var dispatcher = new DelegateDispatcher(handler);
        _entries.Add(new RouteEntry(method, pattern, dispatcher));
        return new TurboRouteHandlerBuilder();
    }

    public TurboRouteHandlerBuilder Add(HttpMethod method, string pattern, Delegate handler)
    {
        var bound = DelegateHandlerBinder.Bind(pattern, handler);
        var dispatcher = new DelegateDispatcher((ctx) => bound(ctx, ctx.RequestServices));
        _entries.Add(new RouteEntry(method, pattern, dispatcher));
        return new TurboRouteHandlerBuilder();
    }

    internal TurboRouteHandlerBuilder AddWithDispatcher(HttpMethod method, string pattern, IRouteDispatcher dispatcher)
    {
        _entries.Add(new RouteEntry(method, pattern, dispatcher));
        return new TurboRouteHandlerBuilder();
    }

    public TurboRouteGroupBuilder CreateGroup(string prefix)
    {
        return new TurboRouteGroupBuilder(prefix, this);
    }

    internal RouteTable Freeze()
    {
        if (_frozen is not null)
        {
            return _frozen;
        }

        _frozen = new RouteTable([.. _entries]);
        return _frozen;
    }
}
