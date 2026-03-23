using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using TurboHttp.Internal;
using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;
using TurboHttp.Transport;

namespace TurboHttp.Pooling;

/// <summary>
/// Connection actor for HTTP/3 over QUIC.
/// Manages a shared <see cref="QuicClientProvider"/> and multiple concurrent typed stream runners.
/// Each QUIC stream gets its own <see cref="ClientRunner"/> and independent channel pair,
/// while all streams share the underlying QUIC connection via the shared provider.
/// Supports bidirectional request streams, write-only control/QPACK streams,
/// and read-only server-initiated streams.
/// </summary>
public sealed class Http3ConnectionActor : ConnectionActorBase
{
    /// <summary>
    /// Sent by <see cref="HostPool"/> to request a new typed QUIC stream on an existing connection.
    /// The requester receives a <see cref="TypedConnectionHandle"/> with a stream-specific handle.
    /// </summary>
    public sealed record OpenTypedStream(IActorRef Requester, OutputStreamType StreamType);

    /// <summary>
    /// Reply sent to the requester after a typed stream is opened, wrapping the
    /// <see cref="ConnectionHandle"/> with its <see cref="OutputStreamType"/>.
    /// </summary>
    public sealed record TypedConnectionHandle(ConnectionHandle Handle, OutputStreamType StreamType);

    /// <summary>
    /// Notification sent to the inbound subscriber when a server-initiated stream is accepted.
    /// </summary>
    public sealed record InboundStreamReady(ConnectionHandle Handle, InputStreamType StreamType);

    /// <summary>
    /// Sent by a stage to subscribe to server-initiated inbound streams.
    /// Buffered notifications are flushed immediately; future ones are forwarded.
    /// </summary>
    public sealed record SubscribeInboundStreams(IActorRef Subscriber);

    /// <summary>
    /// Internal message: an inbound stream has been accepted from the QUIC connection.
    /// </summary>
    private sealed record InboundStreamAccepted(Stream Stream);

    /// <summary>
    /// Internal message: the inbound acceptance loop encountered an error.
    /// </summary>
    private sealed record InboundStreamFailed(Exception Exception);

    /// <summary>
    /// Internal: tracks a pending spawn request in the sequential queue.
    /// </summary>
    private sealed record PendingSpawn(
        IActorRef Requester,
        OutputStreamType StreamType,
        StreamDirection Direction,
        Func<CancellationToken, Task<Stream>>? StreamFactory);

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
    private readonly Queue<PendingSpawn> _pendingStreamRequesters = new();

    /// <summary>
    /// Sequential spawn queue to avoid BecomeStacked nesting issues with concurrent stream opens.
    /// </summary>
    private readonly Queue<PendingSpawn> _spawnQueue = new();

    /// <summary>
    /// The spawn currently in-flight (waiting for ClientConnected).
    /// </summary>
    private PendingSpawn? _currentSpawn;

    /// <summary>
    /// Whether the initial bidirectional connection has been established.
    /// </summary>
    private bool _initialConnected;

    /// <summary>
    /// Subscriber for server-initiated inbound streams.
    /// </summary>
    private IActorRef? _inboundSubscriber;

    /// <summary>
    /// Inbound stream notifications buffered before a subscriber registered.
    /// </summary>
    private readonly List<InboundStreamReady> _bufferedInboundStreams = [];

    /// <summary>
    /// Cancellation for the inbound acceptance loop.
    /// </summary>
    private CancellationTokenSource? _inboundLoopCts;

    private protected override string ProtocolName => "HTTP/3";

    public Http3ConnectionActor(QuicOptions options, IActorRef clientManager, RequestEndpoint requestEndpoint, TurboClientOptions config)
        : base(options, clientManager, requestEndpoint, config)
    {
        Receive<OpenTypedStream>(HandleOpenTypedStream);
        Receive<SubscribeInboundStreams>(HandleSubscribeInboundStreams);
        Receive<InboundStreamAccepted>(HandleInboundStreamAccepted);
        Receive<InboundStreamFailed>(HandleInboundStreamFailed);
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
        if (!_initialConnected)
        {
            // First connection: the initial bidirectional stream
            _initialConnected = true;

            Log.Debug("HTTP/3 connected {0}", msg.RemoteEndPoint);

            Runner = Sender;
            ReconnectAttempt = 0;

            Context.Watch(Runner);
            _activeRunners.Add(Runner);

            NotifyParentReady(BuildHandle(msg));

            // Start the inbound acceptance loop now that the connection is alive
            StartInboundAcceptanceLoop();

            // Flush any pending stream requesters that arrived before initial connection was ready
            FlushPendingStreamRequesters();
            return;
        }

        // Subsequent connections: typed stream spawns via the spawn queue
        HandleSpawnedStreamConnected(msg);
    }

    private protected override void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        Log.Warning("HTTP/3 stream disconnected {0}", msg.RemoteEndPoint);

        _activeRunners.Remove(Sender);

        // If this was a pending spawn, notify requester of failure
        if (_currentSpawn is not null)
        {
            Log.Warning("HTTP/3 typed stream ({0}) failed to open for {1}",
                _currentSpawn.StreamType, msg.RemoteEndPoint);
            _currentSpawn = null;
            ProcessNextSpawn();
        }

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
        // Cancel the inbound acceptance loop
        _inboundLoopCts?.Cancel();
        _inboundLoopCts?.Dispose();
        _inboundLoopCts = null;

        // Dispose the shared QUIC provider — the connection is dead
        _sharedProvider?.DisposeAsync().AsTask().ContinueWith(
            t => { if (t.IsFaulted) Log.Warning(t.Exception, "Failed to dispose QUIC provider"); },
            TaskContinuationOptions.OnlyOnFaulted);
        _sharedProvider = null;
        _activeRunners.Clear();

        // Reset spawn queue state
        _initialConnected = false;
        _currentSpawn = null;
        _spawnQueue.Clear();
        _bufferedInboundStreams.Clear();
        _inboundSubscriber = null;

        base.Reconnect();
    }

    protected override void PostStop()
    {
        // Cancel the inbound acceptance loop
        _inboundLoopCts?.Cancel();
        _inboundLoopCts?.Dispose();
        _inboundLoopCts = null;

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

    private void HandleOpenTypedStream(OpenTypedStream msg)
    {
        var (direction, streamFactory) = MapStreamType(msg.StreamType);
        var spawn = new PendingSpawn(msg.Requester, msg.StreamType, direction, streamFactory);

        if (_sharedProvider is null)
        {
            // Connection not yet established — queue the requester
            _pendingStreamRequesters.Enqueue(spawn);
            return;
        }

        EnqueueSpawn(spawn);
    }

    private void HandleSubscribeInboundStreams(SubscribeInboundStreams msg)
    {
        _inboundSubscriber = msg.Subscriber;

        // Flush any buffered inbound stream notifications
        foreach (var buffered in _bufferedInboundStreams)
        {
            _inboundSubscriber.Tell(buffered);
        }

        _bufferedInboundStreams.Clear();
    }

    private void HandleInboundStreamAccepted(InboundStreamAccepted msg)
    {
        if (_sharedProvider is null)
        {
            // Connection torn down — discard
            msg.Stream.Dispose();
            return;
        }

        // Read the stream type byte to determine what kind of stream this is
        var typeBuf = new byte[8]; // max QUIC varint size
        var bytesRead = msg.Stream.Read(typeBuf, 0, 1);
        if (bytesRead == 0)
        {
            Log.Warning("HTTP/3 inbound stream closed before type byte could be read");
            msg.Stream.Dispose();
            ContinueInboundAcceptanceLoop();
            return;
        }

        if (!QuicVarInt.TryDecode(typeBuf.AsSpan(0, bytesRead), out var streamTypeValue, out _))
        {
            Log.Warning("HTTP/3 inbound stream: failed to decode stream type varint");
            msg.Stream.Dispose();
            ContinueInboundAcceptanceLoop();
            return;
        }

        var inputStreamType = (Http3StreamType)streamTypeValue switch
        {
            Http3StreamType.Control => InputStreamType.Control,
            Http3StreamType.QpackEncoder => InputStreamType.QpackEncoder,
            Http3StreamType.QpackDecoder => InputStreamType.QpackDecoder,
            _ => (InputStreamType?)null,
        };

        if (inputStreamType is null)
        {
            Log.Warning("HTTP/3 inbound stream: unknown stream type 0x{0:X}", streamTypeValue);
            msg.Stream.Dispose();
            ContinueInboundAcceptanceLoop();
            return;
        }

        // Spawn a ReadOnly runner for the accepted inbound stream
        SpawnInboundStreamRunner(msg.Stream, inputStreamType.Value);

        // Continue accepting
        ContinueInboundAcceptanceLoop();
    }

    private void HandleInboundStreamFailed(InboundStreamFailed msg)
    {
        if (_sharedProvider is null)
        {
            // Connection torn down — expected
            return;
        }

        Log.Warning(msg.Exception, "HTTP/3 inbound stream acceptance failed");

        // If the connection is still alive, try again
        ContinueInboundAcceptanceLoop();
    }

    /// <summary>
    /// Maps an <see cref="OutputStreamType"/> to the correct <see cref="StreamDirection"/>
    /// and stream factory function.
    /// </summary>
    private (StreamDirection Direction, Func<CancellationToken, Task<Stream>>? StreamFactory) MapStreamType(OutputStreamType streamType)
    {
        return streamType switch
        {
            OutputStreamType.Request => (StreamDirection.Bidirectional, null), // default GetStreamAsync
            OutputStreamType.Control => (StreamDirection.WriteOnly, _sharedProvider!.GetUnidirectionalStreamAsync),
            OutputStreamType.QpackEncoder => (StreamDirection.WriteOnly, _sharedProvider!.GetUnidirectionalStreamAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(streamType), streamType, "Unknown output stream type"),
        };
    }

    /// <summary>
    /// Enqueues a spawn and processes it immediately if no other spawn is in-flight.
    /// </summary>
    private void EnqueueSpawn(PendingSpawn spawn)
    {
        _spawnQueue.Enqueue(spawn);

        if (_currentSpawn is null)
        {
            ProcessNextSpawn();
        }
    }

    /// <summary>
    /// Processes the next spawn in the queue, if any.
    /// </summary>
    private void ProcessNextSpawn()
    {
        if (_spawnQueue.Count == 0)
        {
            _currentSpawn = null;
            return;
        }

        _currentSpawn = _spawnQueue.Dequeue();
        SpawnStreamRunner(_currentSpawn);
    }

    /// <summary>
    /// Spawns a new <see cref="ClientRunner"/> with the shared QUIC provider for a typed stream.
    /// </summary>
    private void SpawnStreamRunner(PendingSpawn spawn)
    {
        var streamOut = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var streamIn = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        ClientManager.Tell(new ClientManager.CreateRunnerWithChannels(
            Options, Self, streamOut, streamIn, _sharedProvider,
            spawn.Direction, spawn.StreamFactory));
    }

    /// <summary>
    /// Handles <see cref="ClientRunner.ClientConnected"/> for typed stream spawns.
    /// </summary>
    private void HandleSpawnedStreamConnected(ClientRunner.ClientConnected msg)
    {
        var runner = Sender;
        Context.Watch(runner);
        _activeRunners.Add(runner);

        if (_currentSpawn is null)
        {
            // Unexpected — log and treat as generic stream
            Log.Warning("HTTP/3 received ClientConnected with no pending spawn");
            ProcessNextSpawn();
            return;
        }

        var spawn = _currentSpawn;
        var handle = new ConnectionHandle(msg.OutboundWriter, msg.InboundReader, RequestEndpoint, Self);
        spawn.Requester.Tell(new TypedConnectionHandle(handle, spawn.StreamType));

        // Process the next queued spawn
        ProcessNextSpawn();
    }

    /// <summary>
    /// Spawns a <see cref="ClientRunner"/> with <see cref="StreamDirection.ReadOnly"/>
    /// for a server-initiated inbound stream.
    /// </summary>
    private void SpawnInboundStreamRunner(Stream stream, InputStreamType streamType)
    {
        var streamOut = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();
        var streamIn = Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

        // The stream is already accepted — wrap it in a factory that returns it immediately
        var alreadyAcceptedStream = stream;
        ClientManager.Tell(new ClientManager.CreateRunnerWithChannels(
            Options, Self, streamOut, streamIn, _sharedProvider,
            StreamDirection.ReadOnly,
            StreamFactory: _ => Task.FromResult(alreadyAcceptedStream)));

        // Use BecomeStacked to intercept the next ClientConnected for this inbound runner
        BecomeStacked(message =>
        {
            if (message is ClientRunner.ClientConnected connected)
            {
                UnbecomeStacked();

                var runner = Sender;
                Context.Watch(runner);
                _activeRunners.Add(runner);

                var handle = new ConnectionHandle(connected.OutboundWriter, connected.InboundReader, RequestEndpoint, Self);
                var notification = new InboundStreamReady(handle, streamType);

                if (_inboundSubscriber is not null)
                {
                    _inboundSubscriber.Tell(notification);
                }
                else
                {
                    _bufferedInboundStreams.Add(notification);
                }

                return;
            }

            if (message is ClientRunner.ClientDisconnected disconnected)
            {
                UnbecomeStacked();
                Log.Warning("HTTP/3 inbound stream runner failed to start for {0}", disconnected.RemoteEndPoint);
                return;
            }

            // For any other message, use default handling
            UnbecomeStacked();
            OnReceive(message);
        });
    }

    /// <summary>
    /// Starts the background loop that accepts server-initiated inbound streams.
    /// </summary>
    private void StartInboundAcceptanceLoop()
    {
        _inboundLoopCts = new CancellationTokenSource();
        ContinueInboundAcceptanceLoop();
    }

    /// <summary>
    /// Issues the next <see cref="IClientProvider.AcceptInboundStreamAsync"/> call.
    /// </summary>
    private void ContinueInboundAcceptanceLoop()
    {
        if (_inboundLoopCts is null || _inboundLoopCts.IsCancellationRequested || _sharedProvider is null)
        {
            return;
        }

        var self = Self;
        _sharedProvider.AcceptInboundStreamAsync(_inboundLoopCts.Token)
            .PipeTo(self,
                success: stream => new InboundStreamAccepted(stream),
                failure: ex => new InboundStreamFailed(ex));
    }

    private void FlushPendingStreamRequesters()
    {
        while (_pendingStreamRequesters.Count > 0)
        {
            var spawn = _pendingStreamRequesters.Dequeue();
            EnqueueSpawn(spawn);
        }
    }
}
