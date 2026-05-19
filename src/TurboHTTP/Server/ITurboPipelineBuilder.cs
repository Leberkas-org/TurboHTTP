namespace TurboHTTP.Server;

public interface ITurboPipelineBuilder
{
    ITurboPipelineBuilder Use(Func<TurboHttpContext, TurboRequestDelegate, Task> middleware);

    ITurboPipelineBuilder Use<T>() where T : class, ITurboMiddleware;

    ITurboPipelineBuilder Run(TurboRequestDelegate handler);

    ITurboPipelineBuilder Map(string pathPrefix, Action<ITurboPipelineBuilder> configure);

    ITurboPipelineBuilder MapWhen(Func<TurboHttpContext, bool> predicate, Action<ITurboPipelineBuilder> configure);
}
