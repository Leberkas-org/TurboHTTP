using System;
using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TurboHttp.Client;

namespace TurboHttp.Hosting;

public static class TurboClientServiceCollectionExtensions
{
    public static IServiceCollection AddTurboHttpClientFactory(this IServiceCollection services,
        Action<TurboClientOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<ITurboHttpClientFactory>(provider =>
        {
            var system = provider.GetService<ActorSystem>();
            if (system is null)
            {
                // start our own local ActorSystem
                system = ActorSystem.Create("turbomqtt");
                system.Log.Info("Created new Akka.NET ActorSystem {0} - none found in IServiceCollection", system.Name);
            }

            var options = provider.GetRequiredService<IOptionsMonitor<TurboClientOptions>>();
            return new TurboHttpClientFactory(options, system);
        });

        return services;
    }
}