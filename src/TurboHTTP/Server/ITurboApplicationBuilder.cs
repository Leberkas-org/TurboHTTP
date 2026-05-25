namespace TurboHTTP.Server;

public interface ITurboApplicationBuilder
{
    ITurboApplicationBuilder Use(Func<TurboHttpContext, TurboRequestDelegate, Task> middleware);

    ITurboApplicationBuilder Use<T>() where T : class, ITurboMiddleware;

    ITurboApplicationBuilder Run(TurboRequestDelegate handler);

    ITurboApplicationBuilder Map(string pathPrefix, Action<ITurboApplicationBuilder> configure);

    ITurboApplicationBuilder MapWhen(Func<TurboHttpContext, bool> predicate, Action<ITurboApplicationBuilder> configure);
}
