using System;
using Akka.Actor;
using Microsoft.Extensions.Options;

namespace TurboHttp.Client;

public sealed class TurboHttpClientFactory(IOptionsMonitor<TurboClientOptions> options, ActorSystem system)
    : ITurboHttpClientFactory
{
    public ITurboHttpClient CreateClient(Action<TurboClientOptions>? configure = null)
    {
        var options1 = options.CurrentValue;
        configure?.Invoke(options1);
        return new TurboHttpClient(options1, system);
    }
}