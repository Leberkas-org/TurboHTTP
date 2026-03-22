using Akka.Actor;
using Akka.Event;
using TurboHttp.Client;
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

    private protected override void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateRunnerWithChannels(_options, Self, _out, _in));
    }

    private protected override void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("HTTP/2 connected {0}", msg.RemoteEndPoint);

        _runner = Sender;
        _reconnectAttempt = 0;

        Context.Watch(_runner);

        NotifyParentReady(BuildHandle(msg));
    }

    private protected override void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("HTTP/2 disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private protected override void HandleTerminated(Terminated msg)
    {
        if (!msg.ActorRef.Equals(_runner))
        {
            return;
        }

        _log.Warning("HTTP/2 ClientRunner terminated");
        Reconnect();
    }

    protected override void PostStop()
    {
        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
            // noop — runner may already be stopped
        }
    }
}
