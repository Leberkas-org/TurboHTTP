using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP;

public interface ITurboHttpClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
}
