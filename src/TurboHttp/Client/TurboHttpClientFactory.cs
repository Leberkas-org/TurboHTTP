using System;
using Akka.Actor;
using Microsoft.Extensions.Options;
using TurboHttp.Middleware;

namespace TurboHttp.Client;

/// <summary>
/// Default implementation of <see cref="ITurboHttpClientFactory"/>.
/// Reads per-client configuration from <see cref="IOptionsMonitor{TOptions}"/> at
/// <see cref="CreateClient"/> time so that changes are picked up without restarting.
/// </summary>
internal sealed class TurboHttpClientFactory(
    IOptionsMonitor<TurboClientOptions> options,
    IOptionsMonitor<TurboClientDescriptor> descriptors,
    IServiceProvider provider,
    ActorSystem system)
    : ITurboHttpClientFactory
{
    /// <summary>
    /// Root service provider used to resolve per-client middleware instances (TASK-019).
    /// </summary>
    internal readonly IServiceProvider Provider = provider;

    public ITurboHttpClient CreateClient(string name)
    {
        var clientOptions = options.Get(name);
        var descriptor = descriptors.Get(name);
        return new TurboHttpClient(clientOptions, system);
    }
}
