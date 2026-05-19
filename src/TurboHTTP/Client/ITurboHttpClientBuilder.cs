using Microsoft.Extensions.DependencyInjection;

namespace TurboHTTP.Client;

public interface ITurboHttpClientBuilder
{
    string Name { get; }
    IServiceCollection Services { get; }
}
