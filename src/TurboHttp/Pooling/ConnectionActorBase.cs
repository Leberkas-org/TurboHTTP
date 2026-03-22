using System;
using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Pooling;

/// <summary>
/// Abstract base class for version-specific connection actors.
/// Encapsulates shared channel management, reconnection with exponential backoff,
/// and message forwarding to the parent <see cref="HostPool"/>.
/// </summary>
public abstract class ConnectionActorBase : ReceiveActor
{
    /// <summary>
    /// Sent to the parent actor when a TCP connection is established,
    /// providing direct Channel-based I/O access via <see cref="ConnectionHandle"/>.
    /// </summary>
    public sealed record ConnectionReady(ConnectionHandle Handle);

    /// <summary>
    /// Internal message used to trigger a scheduled reconnect attempt.
    /// </summary>
    private protected sealed record DoReconnect;

    private protected readonly TcpOptions _options;
    private protected readonly IActorRef _clientManager;
    private protected readonly RequestEndpoint _requestEndpoint;
    private protected readonly TurboClientOptions _config;
    private protected readonly ILoggingAdapter _log = Context.GetLogger();

    private protected Channel<(IMemoryOwner<byte>, int)> _out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
    private protected Channel<(IMemoryOwner<byte>, int)> _in = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    private protected IActorRef? _runner;
    private protected int _reconnectAttempt;

    private protected ConnectionActorBase(TcpOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
    {
        _options = options;
        _clientManager = clientManager;
        _requestEndpoint = requestEndpoint;
        _config = config;

        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<Terminated>(HandleTerminated);
        Receive<DoReconnect>(_ => AttemptReconnect());

        // Forward stream lifecycle messages to parent (HostPool)
        Receive<HostPool.MarkConnectionNoReuse>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.StreamCompleted>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.StreamAcquired>(msg => Context.Parent.Tell(msg));
        Receive<HostPool.UpdateMaxConcurrentStreams>(msg => Context.Parent.Tell(msg));
    }

    protected override void PreStart()
    {
        Connect();
    }

    /// <summary>
    /// Initiates the TCP/TLS/QUIC connection. Subclasses decide how to create the runner.
    /// </summary>
    private protected abstract void Connect();

    /// <summary>
    /// Called when a <see cref="ClientRunner.ClientConnected"/> message is received.
    /// Subclasses implement version-specific connection handling (e.g., multi-stream tracking).
    /// </summary>
    private protected abstract void HandleConnected(ClientRunner.ClientConnected msg);

    /// <summary>
    /// Called when a <see cref="ClientRunner.ClientDisconnected"/> message is received.
    /// Subclasses decide whether to reconnect immediately or defer (e.g., QUIC waits for all runners).
    /// </summary>
    private protected abstract void HandleDisconnected(ClientRunner.ClientDisconnected msg);

    /// <summary>
    /// Called when a <see cref="Terminated"/> message is received for a watched runner.
    /// Subclasses implement version-specific runner lifecycle handling.
    /// </summary>
    private protected abstract void HandleTerminated(Terminated msg);

    /// <summary>
    /// Initiates the reconnection process: completes old channels, notifies parent,
    /// and schedules a reconnect attempt with exponential backoff.
    /// Subclasses may override to add cleanup (e.g., disposing shared providers).
    /// </summary>
    private protected virtual void Reconnect()
    {
        _runner = null;

        // Complete both channels so old pump tasks exit cleanly before new channels are created.
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

    /// <summary>
    /// Handles a scheduled reconnect: creates fresh channels and calls <see cref="Connect"/>.
    /// </summary>
    private protected virtual void AttemptReconnect()
    {
        // Create fresh channels — the previous _in.Writer was completed to signal stale handles.
        _out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        _in = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        Connect();
    }

    /// <summary>
    /// Builds a <see cref="ConnectionHandle"/> from a <see cref="ClientRunner.ClientConnected"/> message.
    /// </summary>
    private protected ConnectionHandle BuildHandle(ClientRunner.ClientConnected msg)
    {
        return new ConnectionHandle(msg.OutboundWriter, msg.InboundReader, _requestEndpoint, Self);
    }

    /// <summary>
    /// Sends <see cref="ConnectionReady"/> to the parent with the given handle.
    /// </summary>
    private protected void NotifyParentReady(ConnectionHandle handle)
    {
        Context.Parent.Tell(new ConnectionReady(handle));
    }
}
