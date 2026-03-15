using System;
using Microsoft.Extensions.DependencyInjection;
using TurboHttp.Client;

namespace TurboHttp.Hosting;

public static class TurboClientServiceCollectionExtensions
{
    public static IServiceCollection AddTurboClient(this IServiceCollection services,
        Action<TurboClientOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ITurboHttpClientFactory, TurboHttpClientFactory>();
        return services;
    }
}