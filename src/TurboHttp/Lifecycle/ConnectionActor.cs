using System;
using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Lifecycle;

public sealed class ConnectionActor : ReceiveActor
{
    /// <summary>
    /// Sent to the parent actor when a TCP connection is established,
    /// providing direct Channel-based I/O access via <see cref="ConnectionHandle"/>.
    /// </summary>
    public sealed record ConnectionReady(ConnectionHandle Handle);

    /// <summary>
    /// Internal message used to trigger a scheduled reconnect attempt.
    /// </summary>
    private sealed record DoReconnect;

    private readonly TcpOptions _options;
    private readonly IActorRef _clientManager;
    private readonly RequestEndpoint _requestEndpoint;
    private readonly TurboClientOptions _config;

    private Channel<(IMemoryOwner<byte>, int)> _out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
    private Channel<(IMemoryOwner<byte>, int)> _in = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;
    private int _reconnectAttempt;

    public ConnectionActor(TcpOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
    {
        _options = options;
        _clientManager = clientManager;
        _requestEndpoint = requestEndpoint;
        _config = config;


        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<Terminated>(HandleTerminated);
        Receive<DoReconnect>(_ => AttemptReconnect());
        Receive<HostPool.MarkConnectionNoReuse>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.StreamCompleted>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.StreamAcquired>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.UpdateMaxConcurrentStreams>(msg => Context.Parent.Tell(msg));
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateRunnerWithChannels(_options, Self, _out, _in));
    }

    private void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _runner = Sender;
        _reconnectAttempt = 0;

        Context.Watch(_runner);

        // Send ConnectionReady with direct channel handles to parent
        var handle = new ConnectionHandle(msg.OutboundWriter, msg.InboundReader, _requestEndpoint, Self);
        Context.Parent.Tell(new ConnectionReady(handle));
    }

    private void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private void HandleTerminated(Terminated msg)
    {
        if (!msg.ActorRef.Equals(_runner)) return;
        _log.Warning("ClientRunner terminated");
        Reconnect();
    }

    private void Reconnect()
    {
        _runner = null;

        // Complete both channels so old pump tasks exit cleanly before new channels are created.
        // _in.Writer completion: MoveChannelToStream sees OutboundReader.Completion and exits its loop.
        // _out.Writer completion: ConnectionStage sees InboundReader.Completion (end-of-stream) and
        //   re-requests a fresh handle; MovePipeToChannel can no longer write stale data.
        _in.Writer.TryComplete();
        _out.Writer.TryComplete();

        // Notify parent of connection failure
        Context.Parent.Tell(new HostPool.ConnectionFailed(Self));

        if (_reconnectAttempt >= _config.MaxReconnectAttempts)
        {
            _log.Warning("Max reconnect attempts ({0}) reached for {1}:{2} — giving up",
                _config.MaxReconnectAttempts, _options.Host, _options.Port);
            return;
        }

        // Exponential backoff: base * 2^attempt (capped at 30s)
        var delay = TimeSpan.FromTicks(
            Math.Min(
                _config.ReconnectInterval.Ticks * (1L << _reconnectAttempt),
                TimeSpan.FromSeconds(30).Ticks));

        _reconnectAttempt++;

        _log.Debug("Scheduling reconnect attempt {0}/{1} in {2}",
            _reconnectAttempt, _config.MaxReconnectAttempts, delay);

        Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new DoReconnect(), Self);
    }

    private void AttemptReconnect()
    {
        // Create fresh channels — the previous _in.Writer was completed to signal stale handles.
        _out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        _in = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        Connect();
    }

    protected override void PostStop()
    {
        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
            // noop
        }
    }
}