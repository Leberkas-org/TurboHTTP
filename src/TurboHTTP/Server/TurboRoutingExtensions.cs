using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Routing;

namespace TurboHTTP.Server;

public static class TurboRoutingExtensions
{
    public static TurboRouteHandlerBuilder MapTurboGet(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("GET", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboPost(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("POST", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboPut(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("PUT", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboDelete(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("DELETE", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboPatch(this WebApplication app, string pattern, Delegate handler)
    {
        return app.Services.GetRequiredService<TurboRouteTable>().Add("PATCH", pattern, handler);
    }

    public static TurboRouteHandlerBuilder MapTurboMethods(
        this WebApplication app, string pattern, IEnumerable<string> methods, Delegate handler)
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

    public static TurboRouteHandlerBuilder MapTurboEntity(this WebApplication app, string pattern,
        Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern);
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(app.Services.GetRequiredService<TurboRouteTable>());
        return new TurboRouteHandlerBuilder();
    }
    public static TurboRouteHandlerBuilder MapTurboEntity<TActorKey>(this WebApplication app, string pattern,
        Action<TurboEntityBuilder> configure)
    {
        var entityBuilder = new TurboEntityBuilder(pattern).UseActorRef(x => x.Get<TActorKey>());
        configure(entityBuilder);
        entityBuilder.AddToRouteTable(app.Services.GetRequiredService<TurboRouteTable>());
        return new TurboRouteHandlerBuilder();
    }
    
}