using System;
using Akka.Actor;
using Microsoft.Extensions.Options;

namespace TurboHttp.Client;

/// <summary>
/// Default implementation of <see cref="ITurboHttpClientFactory"/>.
/// Reads the current <see cref="TurboClientOptions"/> snapshot from <see cref="IOptionsMonitor{TOptions}"/>
/// so that options changes are picked up without restarting the application.
/// </summary>
public sealed class TurboHttpClientFactory(IOptionsMonitor<TurboClientOptions> options, ActorSystem system)
    : ITurboHttpClientFactory
{
    /// <inheritdoc />
    public ITurboHttpClient CreateClient(Action<TurboClientOptions>? configure = null)
    {
        var copy = options.CurrentValue with { };
        configure?.Invoke(copy);
        return new TurboHttpClient(copy, system);
    }
}