using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using Akka.Actor;
using Akka.Event;
using TurboHttp.Client;
using TurboHttp.Internal;
using TurboHttp.Transport;

namespace TurboHttp.Pooling;

/// <summary>
/// Connection actor for HTTP/3 over QUIC.
/// Manages a shared <see cref="QuicClientProvider"/> and multiple concurrent stream runners.
/// Each QUIC stream gets its own <see cref="ClientRunner"/> and independent channel pair,
/// while all streams share the underlying QUIC connection via the shared provider.
/// </summary>
public sealed class Http3ConnectionActor : ConnectionActorBase
{
    /// <summary>
    /// Sent by <see cref="HostPool"/> to request a new QUIC stream on an existing connection.
    /// The requester receives a <see cref="ConnectionHandle"/> with a stream-specific handle.
    /// </summary>
    public sealed record OpenNewStream(IActorRef Requester);

    /// <summary>
    /// For QUIC: the shared provider that all streams on this connection use.
    /// </summary>
    private IClientProvider? _sharedProvider;

    /// <summary>
    /// Tracks all active stream runners so they can be stopped on connection teardown.
    /// </summary>
    private readonly List<IActorRef> _activeRunners = [];

    /// <summary>
    /// Requesters waiting for a stream while the initial connection is being established.
    /// </summary>
    private readonly Queue<IActorRef> _pendingStreamRequesters = new();

    public Http3ConnectionActor(QuicOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
        : base(options, clientManager, requestEndpoint, config)
    {
        Receive<OpenNewStream>(HandleOpenNewStream);
    }

    private protected override void Connect()
    {
        // Create and store the shared QUIC provider so subsequent streams reuse it.
#pragma warning disable CA1416 // QuicClientProvider is guarded by QuicOptions at runtime
        _sharedProvider = new QuicClientProvider((QuicOptions)Options);
#pragma warning restore CA1416
        ClientManager.Tell(new ClientManager.CreateRunnerWithChannels(Options, Self, Out, In, _sharedProvider));
    }

    private protected override void HandleConnected(ClientRunner.ClientConnected msg)
    {
        Log.Debug("HTTP/3 connected {0}", msg.RemoteEndPoint);

        Runner = Sender;
        ReconnectAttempt = 0;

        Context.Watch(Runner);
        _activeRunners.Add(Runner);

        NotifyParentReady(BuildHandle(msg));

        // Flush any pending stream requesters that arrived before initial connection was ready
        FlushPendingStreamRequesters();
    }

    private protected override void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        Log.Warning("HTTP/3 stream disconnected {0}", msg.RemoteEndPoint);

        _activeRunners.Remove(Sender);

        // Only reconnect if ALL runners are gone (connection-level failure)
        if (_activeRunners.Count > 0)
        {
            return;
        }

        Reconnect();
    }

    private protected override void HandleTerminated(Terminated msg)
    {
        _activeRunners.Remove(msg.ActorRef);

        if (msg.ActorRef.Equals(Runner))
        {
            Runner = null;
        }

        // Only reconnect if all runners are gone
        if (_activeRunners.Count > 0)
        {
            return;
        }

        Log.Warning("All HTTP/3 stream runners terminated");
        Reconnect();
    }

    /// <summary>
    /// Overrides base reconnection to dispose the shared QUIC provider and clear runner tracking.
    /// </summary>
    private protected override void Reconnect()
    {
        // Dispose the shared QUIC provider — the connection is dead
        _sharedProvider?.DisposeAsync().AsTask().ContinueWith(
            t => { if (t.IsFaulted) Log.Warning(t.Exception, "Failed to dispose QUIC provider"); },
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        _sharedProvider = null;
        _activeRunners.Clear();

        base.Reconnect();
    }

    protected override void PostStop()
    {
        // Stop all active QUIC stream runners
        foreach (var runner in _activeRunners)
        {
            try
            {
                runner.Tell(new DoClose());
            }
            catch
            {
                // noop — runner may already be stopped
            }
        }

        // Dispose the shared provider synchronously on stop
        _sharedProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _sharedProvider = null;
    }

    private void HandleOpenNewStream(OpenNewStream msg)
    {
        if (_sharedProvider is null)
        {
            // Connection not yet established — queue the requester
            _pendingStreamRequesters.Enqueue(msg.Requester);
            return;
        }

        SpawnStreamRunner(msg.Requester);
    }

    /// <summary>
    /// Spawns a new <see cref="ClientRunner"/> with the shared QUIC provider to open a new bidirectional stream.
    /// Uses <see cref="ReceiveActor.BecomeStacked"/> to temporarily intercept the next
    /// <see cref="ClientRunner.ClientConnected"/> for this specific stream.
    /// </summary>
    private void SpawnStreamRunner(IActorRef requester)
    {
        var streamOut = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var streamIn = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        // Spawn a new runner with the shared QUIC provider — opens a new bidirectional stream
        ClientManager.Tell(new ClientManager.CreateRunnerWithChannels(
            Options, Self, streamOut, streamIn, _sharedProvider));

        // Associate the requester with the upcoming ClientConnected message.
        // BecomeStacked temporarily intercepts the next ClientConnected for this stream.
        BecomeStacked(message =>
        {
            if (message is ClientRunner.ClientConnected connected)
            {
                UnbecomeStacked();

                var runner = Sender;
                Context.Watch(runner);
                _activeRunners.Add(runner);

                var handle = new ConnectionHandle(connected.OutboundWriter, connected.InboundReader, RequestEndpoint, Self);
                requester.Tell(handle);

                return;
            }

            if (message is ClientRunner.ClientDisconnected disconnected)
            {
                UnbecomeStacked();
                Log.Warning("HTTP/3 stream failed to open for {0}", disconnected.RemoteEndPoint);
                return;
            }

            // For any other message, use default handling
            UnbecomeStacked();
            OnReceive(message);
        });
    }

    private void FlushPendingStreamRequesters()
    {
        while (_pendingStreamRequesters.Count > 0)
        {
            var requester = _pendingStreamRequesters.Dequeue();
            SpawnStreamRunner(requester);
        }
    }
}
