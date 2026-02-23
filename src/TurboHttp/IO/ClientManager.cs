using System;
using Akka.Actor;
using Akka.Event;

namespace Servus.Akka.IO;

public sealed class ClientManager : ReceiveActor
{
    public sealed record CreateTcpRunner(TcpOptions Options, IActorRef Handler, IClientProvider? StreamProvider = null);

    public ClientManager()
    {
        Receive<CreateTcpRunner>(Handle);
        Receive<Terminated>(Handle);
    }

    private void Handle(CreateTcpRunner msg)
    {
        var provider = msg.StreamProvider ?? new TcpClientProvider(msg.Options);
        var host = msg.Options.Host;
        var port = msg.Options.Port;
        var name = $"tcp-runner-{host.Replace(".", "-")}-{port}-{Guid.NewGuid()}";
        var runner = Context.ResolveChildActor<ClientRunner>(name, provider, msg.Options, msg.Handler);
        Context.Watch(runner);
        Sender.Tell(runner);
    }

    private void Handle(Terminated msg)
    {
        Context.GetLogger().Error("Client dead: {0}", msg.ActorRef.Path);
    }
}