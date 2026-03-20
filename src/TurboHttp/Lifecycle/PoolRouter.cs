using System;
using System.Collections.Generic;
using Akka.Actor;
using Servus.Akka;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO;

namespace TurboHttp.Lifecycle;

public sealed class PoolRouter : ReceiveActor
{
    /// <summary>
    /// Sent by ConnectionStage on each ConnectItem to ensure a HostPool actor exists.
    /// The message is forwarded to the HostPool actor so it can reply with a ConnectionHandle.
    /// </summary>
    public sealed record EnsureHost(RequestEndpoint Key, TcpOptions Options);

    private readonly TurboClientOptions _config;
    private readonly Func<TcpOptions, TurboClientOptions, RequestEndpoint, IActorRef> _hostFactory;
    private readonly Dictionary<RequestEndpoint, IActorRef> _hosts = new();

    public PoolRouter(TurboClientOptions config,
        Func<TcpOptions, TurboClientOptions, RequestEndpoint, IActorRef>? hostFactory = null)
    {
        _config = config;
        _hostFactory = hostFactory ?? CreateHostPoolActor;

        Receive<EnsureHost>(HandleEnsureHost);
    }

    private void HandleEnsureHost(EnsureHost msg)
    {
        var hostActor = EnsureHostActor(msg.Key, msg.Options);

        // Forward preserves the original Sender so the HostPool actor can reply directly.
        hostActor.Forward(msg);
    }

    private IActorRef EnsureHostActor(RequestEndpoint key, TcpOptions options)
    {
        if (!_hosts.TryGetValue(key, out var hostActor))
        {
            hostActor = _hostFactory(options, _config, key);
            _hosts[key] = hostActor;
        }

        return hostActor;
    }

    private static IActorRef CreateHostPoolActor(TcpOptions options, TurboClientOptions config, RequestEndpoint key)
    {
        var hostConfig = new HostPool.HostPoolConfig(options, config, key);
        var name = Guid.NewGuid().ToString();
        return Context.ResolveChildActor<HostPool>(name, hostConfig);
    }
}