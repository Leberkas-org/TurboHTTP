using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Server.Middleware;

public sealed class TurboPipelineBuilder : ITurboApplicationBuilder
{
    private readonly List<Func<TurboRequestDelegate, TurboRequestDelegate>> _components = [];

    public ITurboApplicationBuilder Use(Func<TurboHttpContext, TurboRequestDelegate, Task> middleware)
    {
        _components.Add(next => ctx => middleware(ctx, next));
        return this;
    }

    public ITurboApplicationBuilder Use<T>() where T : class, ITurboMiddleware
    {
        _components.Add(next => ctx =>
        {
            var mw = ctx.RequestServices.GetRequiredService<T>();
            return mw.InvokeAsync(ctx, next);
        });
        return this;
    }

    public ITurboApplicationBuilder Run(TurboRequestDelegate handler)
    {
        _components.Add(_ => handler);
        return this;
    }

    public ITurboApplicationBuilder Map(string pathPrefix, Action<ITurboApplicationBuilder> configure)
    {
        var branch = new TurboPipelineBuilder();
        configure(branch);

        _components.Add(next =>
        {
            TurboRequestDelegate? builtBranch = null;
            return ctx =>
            {
                var path = ctx.Request.Path ?? string.Empty;
                if (path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    builtBranch ??= branch.BuildDelegate(next);
                    return builtBranch(ctx);
                }

                return next(ctx);
            };
        });
        return this;
    }

    public ITurboApplicationBuilder MapWhen(Func<TurboHttpContext, bool> predicate,
        Action<ITurboApplicationBuilder> configure)
    {
        var branch = new TurboPipelineBuilder();
        configure(branch);

        _components.Add(next =>
        {
            TurboRequestDelegate? builtBranch = null;
            return ctx =>
            {
                if (predicate(ctx))
                {
                    builtBranch ??= branch.BuildDelegate(next);
                    return builtBranch(ctx);
                }

                return next(ctx);
            };
        });
        return this;
    }

    internal TurboRequestDelegate Build()
    {
        return BuildDelegate(Terminal);
        Task Terminal(TurboHttpContext _) => Task.CompletedTask;
    }

    private TurboRequestDelegate BuildDelegate(TurboRequestDelegate terminal)
    {
        var pipeline = terminal;
        for (var i = _components.Count - 1; i >= 0; i--)
        {
            pipeline = _components[i](pipeline);
        }

        return pipeline;
    }
}