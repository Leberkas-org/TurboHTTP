using System;
using Akka.Actor;
using Akka.Event;
using Servus.Akka;

namespace TurboHttp.IO;

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
        var provider = msg.StreamProvider ?? msg.Options switch
        {
            TlsOptions tls => new TlsClientProvider(tls),
            _ => new TcpClientProvider(msg.Options)
        };
        var prefix = msg.Options is TlsOptions ? "TLS" : "TCP";
        var host = msg.Options.Host;
        var port = msg.Options.Port;
        var name = $"tcp-runner-{prefix}-{host.Replace(".", "-")}-{port}-{Guid.NewGuid()}";
        var runner = Context.ResolveChildActor<ClientRunner>(name, provider, msg.Handler, msg.Options.MaxFrameSize);
        Context.Watch(runner);
        Sender.Tell(runner);
    }

    private static void Handle(Terminated msg)
    {
        Context.GetLogger().Error("Client dead: {0}", msg.ActorRef.Path);
    }
}