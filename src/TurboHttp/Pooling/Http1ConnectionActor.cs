using Akka.Actor;
using Akka.Event;
using TurboHttp.Client;
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

    private protected override void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateRunnerWithChannels(_options, Self, _out, _in));
    }

    private protected override void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _runner = Sender;
        _reconnectAttempt = 0;

        Context.Watch(_runner);

        NotifyParentReady(BuildHandle(msg));
    }

    private protected override void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private protected override void HandleTerminated(Terminated msg)
    {
        if (!msg.ActorRef.Equals(_runner))
        {
            return;
        }

        _log.Warning("ClientRunner terminated");
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
