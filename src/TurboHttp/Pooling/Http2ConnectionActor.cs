using Akka.Actor;
using Akka.Event;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Pooling;

/// <summary>
/// Connection actor for HTTP/2 over TCP (h2c) or TLS (h2).
/// Uses a single TCP connection with multiplexed streams.
/// Stream accounting is handled exclusively via <see cref="HostPool.StreamAcquired"/>
/// and <see cref="HostPool.StreamCompleted"/> signals from the stage layer —
/// no <c>MarkBusy()</c> is triggered from this actor.
/// </summary>
public sealed class Http2ConnectionActor : ConnectionActorBase
{
    public Http2ConnectionActor(TcpOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
        : base(options, clientManager, requestEndpoint, config)
    {
    }

    private protected override string ProtocolName => "HTTP/2";

    private protected override void Connect()
    {
        ClientManager.Tell(new ClientManager.CreateRunnerWithChannels(Options, Self, Out, In));
    }

    private protected override void HandleConnected(ClientRunner.ClientConnected msg)
    {
        Log.Debug("HTTP/2 connected {0}", msg.RemoteEndPoint);

        Runner = Sender;
        ReconnectAttempt = 0;

        Context.Watch(Runner);

        NotifyParentReady(BuildHandle(msg));
    }

    private protected override void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        Log.Warning("HTTP/2 disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private protected override void HandleTerminated(Terminated msg)
    {
        if (!msg.ActorRef.Equals(Runner))
        {
            return;
        }

        Log.Warning("HTTP/2 ClientRunner terminated");
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
