using Akka.Actor;
using Akka.Event;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Pooling;

/// <summary>
/// Connection actor for HTTP/1.0 and HTTP/1.1.
/// Simple single-connection TCP lifecycle — no multi-stream branching.
/// </summary>
public sealed class Http1ConnectionActor : ConnectionActorBase
{
    public Http1ConnectionActor(TcpOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
        : base(options, clientManager, requestEndpoint, config)
    {
    }

    private protected override string ProtocolName => "HTTP/1.1";

    private protected override void Connect()
    {
        ClientManager.Tell(new ClientManager.CreateRunnerWithChannels(Options, Self, Out, In));
    }

    private protected override void HandleConnected(ClientRunner.ClientConnected msg)
    {
        Log.Debug("Connected {0}", msg.RemoteEndPoint);

        Runner = Sender;
        ReconnectAttempt = 0;

        Context.Watch(Runner);

        NotifyParentReady(BuildHandle(msg));
    }

    private protected override void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        Log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private protected override void HandleTerminated(Terminated msg)
    {
        if (!msg.ActorRef.Equals(Runner))
        {
            return;
        }

        Log.Warning("ClientRunner terminated");
        Reconnect();
    }

    protected override void PostStop()
    {
        try
        {
            Runner?.Tell(new DoClose());
        }
        catch
        {
            // noop — runner may already be stopped
        }
    }
}
