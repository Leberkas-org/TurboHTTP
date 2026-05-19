using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Server.Middleware;

public sealed class TurboPipelineBuilder : ITurboPipelineBuilder
{
    private readonly List<Func<TurboRequestDelegate, TurboRequestDelegate>> _components = [];

    public ITurboPipelineBuilder Use(Func<TurboHttpContext, TurboRequestDelegate, Task> middleware)
    {
        _components.Add(next => ctx => middleware(ctx, next));
        return this;
    }

    public ITurboPipelineBuilder Use<T>() where T : class, ITurboMiddleware
    {
        _components.Add(next => ctx =>
        {
            var mw = ctx.RequestServices.GetRequiredService<T>();
            return mw.InvokeAsync(ctx, next);
        });
        return this;
    }

    public ITurboPipelineBuilder Run(TurboRequestDelegate handler)
    {
        _components.Add(_ => handler);
        return this;
    }

    public ITurboPipelineBuilder Map(string pathPrefix, Action<ITurboPipelineBuilder> configure)
    {
        var branch = new TurboPipelineBuilder();
        configure(branch);

        _components.Add(next => ctx =>
        {
            var path = ctx.Request.Path.Value ?? string.Empty;
            if (path.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var branchPipeline = branch.BuildDelegate(next);
                return branchPipeline(ctx);
            }

            return next(ctx);
        });
        return this;
    }

    public ITurboPipelineBuilder MapWhen(Func<TurboHttpContext, bool> predicate,
        Action<ITurboPipelineBuilder> configure)
    {
        var branch = new TurboPipelineBuilder();
        configure(branch);

        _components.Add(next => ctx =>
        {
            if (predicate(ctx))
            {
                var branchPipeline = branch.BuildDelegate(next);
                return branchPipeline(ctx);
            }

            return next(ctx);
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