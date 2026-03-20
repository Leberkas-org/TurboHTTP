using System;
using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using Servus.Akka;

namespace TurboHttp.IO;

public sealed class ClientManager : ReceiveActor
{
    public record CreateRunner(TcpOptions Options, IActorRef Handler, IClientProvider? StreamProvider = null);

    public record CreateRunnerWithChannels(
        TcpOptions Options,
        IActorRef Handler,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)> InboundChannel,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)> OutboundChannel,
        IClientProvider? StreamProvider = null)
        : CreateRunner(Options, Handler, StreamProvider);

    public ClientManager()
    {
        Receive<CreateRunner>(Handle);
        Receive<Terminated>(Handle);
    }

    private static void Handle(CreateRunner msg)
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
        IActorRef runner;
        if (msg is CreateRunnerWithChannels msgWithChannels)
        {
            runner = Context
                .ResolveChildActor<ClientRunner>(name, provider, msg.Handler,
                    msg.Options.MaxFrameSize,
                    msgWithChannels.InboundChannel,
                    msgWithChannels.OutboundChannel);
        }
        else
        {
            runner = Context
                .ResolveChildActor<ClientRunner>(name, provider, msg.Handler,
                    msg.Options.MaxFrameSize);
        }

        Context.Watch(runner);
    }

    private static void Handle(Terminated msg)
    {
        Context.GetLogger().Error("Client dead: {0}", msg.ActorRef.Path);
    }
}