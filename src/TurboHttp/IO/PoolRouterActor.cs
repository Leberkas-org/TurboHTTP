using System;
using System.Buffers;
using System.Collections.Generic;
using Akka.Actor;
using Servus.Akka;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    public sealed record RegisterHost(string PoolKey, TcpOptions Options);

    public sealed record SendRequest(string PoolKey, DataItem Data, IActorRef ReplyTo);

    public sealed record Response(string PoolKey, IMemoryOwner<byte> Memory, int Length);

    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection, Exception? Cause);

    private readonly Dictionary<string, IActorRef> _hosts = new();

    public PoolRouterActor()
    {
        Receive<RegisterHost>(msg =>
        {
            if (_hosts.ContainsKey(msg.PoolKey))
            {
                return;
            }

            var host = Context.ResolveChildActor<HostPoolActor>(msg.PoolKey, msg.Options);
            _hosts[msg.PoolKey] = host;
        });

        Receive<SendRequest>(msg =>
        {
            if (!_hosts.TryGetValue(msg.PoolKey, out var host))
            {
                Sender.Tell(new Status.Failure(new InvalidOperationException("Unknown host")));
                return;
            }

            host.Forward(msg);
        });
    }
}
