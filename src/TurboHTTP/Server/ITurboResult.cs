namespace TurboHTTP.Server;

public interface ITurboResult
{
    Task ExecuteAsync(TurboHttpContext httpContext);
}
