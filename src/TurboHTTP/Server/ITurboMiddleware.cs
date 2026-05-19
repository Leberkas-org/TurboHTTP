using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Server;

public interface ITurboMiddleware
{
    Task InvokeAsync(TurboHttpContext context, TurboRequestDelegate next);
}