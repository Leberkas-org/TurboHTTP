using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp;

public interface ITurboHttpClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
}
