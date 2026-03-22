using Microsoft.Extensions.DependencyInjection;

namespace TurboHttp;

internal sealed class TurboHttpClientBuilder(string name, IServiceCollection services) : ITurboHttpClientBuilder
{
    public string Name { get; } = name;
    public IServiceCollection Services { get; } = services;
}
