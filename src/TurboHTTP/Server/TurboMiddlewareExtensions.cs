using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public static class TurboMiddlewareExtensions
{
    public static WebApplication UseTurbo(
        this WebApplication app,
        Func<TurboHttpContext, TurboRequestDelegate, Task> middleware)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Use(middleware);
        return app;
    }

    public static WebApplication UseTurbo<T>(this WebApplication app)
        where T : class, ITurboMiddleware
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Use<T>();
        return app;
    }

    public static WebApplication RunTurbo(
        this WebApplication app,
        TurboRequestDelegate handler)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Run(handler);
        return app;
    }

    public static WebApplication MapTurbo(
        this WebApplication app,
        string pathPrefix,
        Action<ITurboPipelineBuilder> configure)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .Map(pathPrefix, configure);
        return app;
    }

    public static WebApplication MapTurboWhen(
        this WebApplication app,
        Func<TurboHttpContext, bool> predicate,
        Action<ITurboPipelineBuilder> configure)
    {
        app.Services.GetRequiredService<TurboPipelineBuilder>()
            .MapWhen(predicate, configure);
        return app;
    }
}