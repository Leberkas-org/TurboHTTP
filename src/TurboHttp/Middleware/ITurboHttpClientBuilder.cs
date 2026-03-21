using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp.Middleware;

public interface ITurboHttpClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
}
