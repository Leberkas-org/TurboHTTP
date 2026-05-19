using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Routing;
using TurboHTTP.Routing.Builder;

namespace TurboHTTP.Hosting;

public static class TurboRoutingExtensions
{
    public static TurboRouteHandlerBuilder MapTurboGet(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add(HttpMethod.Get, pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboPost(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add(HttpMethod.Post, pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboPut(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add(HttpMethod.Put, pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboDelete(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add(HttpMethod.Delete, pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboPatch(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add(HttpMethod.Patch, pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboMethods(
        this WebApplication app, string pattern, IEnumerable<HttpMethod> methods, Delegate handler)
    {
        TurboRouteHandlerBuilder? last = null;
        foreach (var method in methods)
        {
            last = app.Services.GetRequiredService<TurboRouteTable>().Add(method, pattern, handler);
        }

        return last!;
    }

    public static TurboRouteGroupBuilder MapTurboGroup(this WebApplication app, string prefix)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().CreateGroup(prefix);
    }

    public static TurboRouteHandlerBuilder MapTurboEntity<TKey>(
        this WebApplication app, string pattern, Action<TurboEntityBuilder<TKey>> configure)
    {
        var entityBuilder = new TurboEntityBuilder<TKey>(pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(app.Services.GetRequiredService<TurboRouteTable>());
        return new TurboRouteHandlerBuilder();
    }
}
