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
    protected sealed record DoReconnect;

    protected readonly TcpOptions Options;
    protected readonly IActorRef ClientManager;
    protected readonly RequestEndpoint RequestEndpoint;
    protected readonly TurboClientOptions Config;
    protected readonly ILoggingAdapter Log = Context.GetLogger();

    protected Channel<(IMemoryOwner<byte>, int)> Out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
    protected Channel<(IMemoryOwner<byte>, int)> In = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    protected IActorRef? Runner;
    protected int ReconnectAttempt;

    private protected ConnectionActorBase(TcpOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint,
        TurboClientOptions config)
    {
        Options = options;
        ClientManager = clientManager;
        RequestEndpoint = requestEndpoint;
        Config = config;

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
        Runner = null;

        // Complete both channels so old pump tasks exit cleanly before new channels are created.
        In.Writer.TryComplete();
        Out.Writer.TryComplete();

        // Notify parent of connection failure
        Context.Parent.Tell(new HostPool.ConnectionFailed(Self));

        if (ReconnectAttempt >= Config.MaxReconnectAttempts)
        {
            Log.Warning("Max reconnect attempts ({0}) reached for {1}:{2} — giving up",
                Config.MaxReconnectAttempts, Options.Host, Options.Port);
            return;
        }

        // Exponential backoff: base * 2^attempt (capped at 30s)
        var delay = TimeSpan.FromTicks(
            Math.Min(
                Config.ReconnectInterval.Ticks * (1L << ReconnectAttempt),
                TimeSpan.FromSeconds(30).Ticks));

        ReconnectAttempt++;

        Log.Debug("Scheduling reconnect attempt {0}/{1} in {2}",
            ReconnectAttempt, Config.MaxReconnectAttempts, delay);

        Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new DoReconnect(), Self);
    }

    /// <summary>
    /// Handles a scheduled reconnect: creates fresh channels and calls <see cref="Connect"/>.
    /// </summary>
    private protected virtual void AttemptReconnect()
    {
        // Create fresh channels — the previous _in.Writer was completed to signal stale handles.
        Out = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        In = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        Connect();
    }

    /// <summary>
    /// Builds a <see cref="ConnectionHandle"/> from a <see cref="ClientRunner.ClientConnected"/> message.
    /// </summary>
    private protected ConnectionHandle BuildHandle(ClientRunner.ClientConnected msg)
    {
        return new ConnectionHandle(msg.OutboundWriter, msg.InboundReader, RequestEndpoint, Self);
    }

    /// <summary>
    /// Sends <see cref="ConnectionReady"/> to the parent with the given handle.
    /// </summary>
    private protected void NotifyParentReady(ConnectionHandle handle)
    {
        Context.Parent.Tell(new ConnectionReady(handle));
    }
}