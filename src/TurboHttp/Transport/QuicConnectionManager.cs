using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using TurboHttp.Diagnostics;
using TurboHttp.Internal;
using TurboHttp.Pooling;
using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Transport;

/// <summary>
/// Manages a shared <see cref="QuicClientProvider"/> and multiple concurrent QUIC streams
/// without actors. Replaces <see cref="Http3ConnectionActor"/> for the direct connection path.
/// Each QUIC stream gets its own <see cref="ConnectionLease"/> with independent channels,
/// while all streams share the underlying QUIC connection via the shared provider.
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
internal sealed class QuicConnectionManager : IAsyncDisposable
{
    /// <summary>
    /// Notification sent to the inbound subscriber when a server-initiated stream is accepted.
    /// </summary>
    public sealed record InboundStream(ConnectionLease Lease, InputStreamType StreamType);

    private readonly QuicOptions _options;
    private readonly RequestEndpoint _endpoint;
    private readonly List<ConnectionLease> _activeStreams = [];
    private readonly SemaphoreSlim _spawnLock = new(1, 1);
    private readonly List<InboundStream> _bufferedInboundStreams = [];
    private readonly object _lock = new();

    private IClientProvider? _sharedProvider;
    private CancellationTokenSource? _inboundLoopCts;
    private Action<InboundStream>? _inboundSubscriber;
    private volatile bool _disposed;
    private bool _skipPumps;

    public QuicConnectionManager(QuicOptions options, RequestEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _endpoint = endpoint;
    }

    /// <summary>
    /// Opens a typed QUIC stream and returns a <see cref="ConnectionLease"/> for it.
    /// Sequential spawn is enforced via <see cref="_spawnLock"/>.
    /// </summary>
    public async Task<ConnectionLease> OpenStreamAsync(
        OutputStreamType streamType,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _spawnLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var provider = EnsureProvider();
            var (direction, streamFactory) = MapStreamType(streamType, provider);

            var stream = await streamFactory(ct).ConfigureAwait(false);
            var lease = CreateStreamLease(stream, direction);

            lock (_lock)
            {
                _activeStreams.Add(lease);
            }

            // Emit metrics
            TurboHttpEventSource.Log.ConnectionOpened(_endpoint.Host, _endpoint.Port, "HTTP/3");
            TurboHttpDiagnosticListener.OnConnectionOpened(_endpoint.Host, _endpoint.Port, "HTTP/3");

            return lease;
        }
        finally
        {
            _spawnLock.Release();
        }
    }

    /// <summary>
    /// Starts the background loop that accepts server-initiated inbound streams.
    /// Buffered notifications are flushed immediately to the subscriber; future
    /// inbound streams are forwarded as they arrive.
    /// </summary>
    public void StartInboundAcceptLoop(Action<InboundStream> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            _inboundSubscriber = subscriber;

            // Flush any buffered inbound stream notifications
            foreach (var buffered in _bufferedInboundStreams)
            {
                _inboundSubscriber(buffered);
            }

            _bufferedInboundStreams.Clear();
        }

        _inboundLoopCts = new CancellationTokenSource();
        _ = RunInboundAcceptLoopAsync(_inboundLoopCts.Token);
    }

    /// <summary>
    /// Disposes the manager: cancels the inbound loop, disposes all stream leases,
    /// and disposes the shared QUIC provider.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 1. Cancel and dispose the inbound acceptance loop
        if (_inboundLoopCts is not null)
        {
            await _inboundLoopCts.CancelAsync().ConfigureAwait(false);
            _inboundLoopCts.Dispose();
            _inboundLoopCts = null;
        }

        // 2. Dispose all active stream leases
        List<ConnectionLease> snapshot;
        lock (_lock)
        {
            snapshot = [.. _activeStreams];
            _activeStreams.Clear();
            _bufferedInboundStreams.Clear();
            _inboundSubscriber = null;
        }

        foreach (var lease in snapshot)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }

        // 3. Dispose the shared QUIC provider
        if (_sharedProvider is not null)
        {
            await _sharedProvider.DisposeAsync().ConfigureAwait(false);
            _sharedProvider = null;
        }

        _spawnLock.Dispose();
    }

    // ── Test seams ────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the shared provider and disables ByteMover pumps (test seam for injecting fakes).
    /// </summary>
    internal void SetProvider(IClientProvider provider)
    {
        _sharedProvider = provider;
        _skipPumps = true;
    }

    /// <summary>
    /// Adds a synthetic buffered inbound notification (test seam).
    /// Requires a lease to already exist (provider must be set).
    /// </summary>
    internal void AddBufferedInbound(InputStreamType streamType)
    {
        var stream = new System.IO.MemoryStream();
        var lease = CreateStreamLease(stream, StreamDirection.ReadOnly);
        lock (_lock)
        {
            _activeStreams.Add(lease);
            _bufferedInboundStreams.Add(new InboundStream(lease, streamType));
        }
    }

    /// <summary>
    /// Flushes buffered inbound notifications to the subscriber (test seam).
    /// </summary>
    internal void FlushBufferedToSubscriber(Action<InboundStream> subscriber)
    {
        lock (_lock)
        {
            foreach (var buffered in _bufferedInboundStreams)
            {
                subscriber(buffered);
            }

            _bufferedInboundStreams.Clear();
        }
    }

    // ── Private helpers ─────────────────────────────────────────────

    /// <summary>
    /// Creates the shared <see cref="QuicClientProvider"/> on first use.
    /// </summary>
    private IClientProvider EnsureProvider()
    {
        if (_sharedProvider is not null)
        {
            return _sharedProvider;
        }

#pragma warning disable CA1416 // QuicClientProvider is guarded by QuicOptions at runtime
        _sharedProvider = new QuicClientProvider(_options);
#pragma warning restore CA1416
        return _sharedProvider;
    }

    /// <summary>
    /// Maps an <see cref="OutputStreamType"/> to the correct <see cref="StreamDirection"/>
    /// and stream factory function.
    /// </summary>
    private static (StreamDirection Direction, Func<CancellationToken, Task<Stream>> StreamFactory) MapStreamType(
        OutputStreamType streamType,
        IClientProvider provider)
    {
        return streamType switch
        {
            OutputStreamType.Request => (StreamDirection.Bidirectional, provider.GetStreamAsync),
            OutputStreamType.Control => (StreamDirection.WriteOnly, provider.GetUnidirectionalStreamAsync),
            OutputStreamType.QpackEncoder => (StreamDirection.WriteOnly, provider.GetUnidirectionalStreamAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(streamType), streamType, "Unknown output stream type"),
        };
    }

    /// <summary>
    /// Creates a <see cref="ConnectionLease"/> for an already-opened QUIC stream,
    /// complete with channels, ByteMover pump tasks, and lifecycle management.
    /// </summary>
    private ConnectionLease CreateStreamLease(Stream stream, StreamDirection direction)
    {
        var state = new ClientState(
            maxFrameSize: _options.MaxFrameSize,
            stream: stream,
            inboundChannel: null,
            outboundChannel: null,
            direction: direction);

        var handle = ConnectionHandle.CreateDirect(
            state.OutboundWriter,
            state.InboundReader,
            _endpoint);

        var lease = new ConnectionLease(handle, state);

        // Spawn ByteMover pump tasks with callback-based close
        // (skipped when using test seam — fake streams would complete immediately)
        if (!_skipPumps)
        {
            var closeOnce = 0;
            Action onClose = () =>
            {
                if (Interlocked.CompareExchange(ref closeOnce, 1, 0) == 0)
                {
                    RemoveStream(lease);
                    _ = lease.DisposeAsync();
                }
            };

            _ = ClientByteMover.MoveStreamToPipe(state, onClose, log: null, lease.Token);
            _ = ClientByteMover.MovePipeToChannel(state, onClose, log: null, lease.Token);
            _ = ClientByteMover.MoveChannelToStream(state, onClose, log: null, lease.Token);
        }

        return lease;
    }

    /// <summary>
    /// Removes a stream lease from the active tracking list.
    /// </summary>
    private void RemoveStream(ConnectionLease lease)
    {
        lock (_lock)
        {
            _activeStreams.Remove(lease);
        }
    }

    /// <summary>
    /// Background loop that continuously accepts server-initiated inbound streams,
    /// reads the stream-type varint, creates a <see cref="ConnectionLease"/>,
    /// and notifies the subscriber.
    /// </summary>
    private async Task RunInboundAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Stream stream;
            try
            {
                var provider = _sharedProvider;
                if (provider is null)
                {
                    return;
                }

                stream = await provider.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Connection may be dead — if not cancelled, continue trying
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            // Read the stream type varint
            var typeBuf = new byte[8];
            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(typeBuf.AsMemory(0, 1), ct).ConfigureAwait(false);
            }
            catch
            {
                stream.Dispose();
                continue;
            }

            if (bytesRead == 0)
            {
                stream.Dispose();
                continue;
            }

            if (!QuicVarInt.TryDecode(typeBuf.AsSpan(0, bytesRead), out var streamTypeValue, out _))
            {
                stream.Dispose();
                continue;
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
                stream.Dispose();
                continue;
            }

            // Create a ReadOnly lease for the inbound stream
            var lease = CreateStreamLease(stream, StreamDirection.ReadOnly);
            lock (_lock)
            {
                _activeStreams.Add(lease);
            }

            var notification = new InboundStream(lease, inputStreamType.Value);

            lock (_lock)
            {
                if (_inboundSubscriber is not null)
                {
                    _inboundSubscriber(notification);
                }
                else
                {
                    _bufferedInboundStreams.Add(notification);
                }
            }
        }
    }
}
