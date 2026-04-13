using System.Runtime.Versioning;
using TurboHTTP.Internal;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Transport.Quic;

// QUIC APIs are platform-guarded; usage is gated at runtime via QuicOptions.
#pragma warning disable CA1416

namespace TurboHTTP.Transport.Connection;

/// <summary>
/// Wraps a <see cref="IClientProvider"/> for a single QUIC connection and exposes
/// typed stream-opening and inbound-stream acceptance.
/// <para>
/// Mirrors <see cref="ConnectionHandle"/> structurally. Carries the QUIC-specific
/// <see cref="InboundStream"/> record (moved from the deleted <c>QuicConnectionManager</c>).
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macOS")]
[SupportedOSPlatform("windows")]
internal sealed class QuicConnectionHandle : IAsyncDisposable
{
    /// <summary>
    /// Notification produced when the inbound-accept loop receives a server-initiated stream.
    /// Equivalent to the old <c>QuicConnectionManager.InboundStream</c> record.
    /// </summary>
    public sealed record InboundStream(ConnectionLease Lease, InputStreamType StreamType);

    private readonly IClientProvider _provider;
    private readonly QuicOptions _options;

    public QuicConnectionHandle(IClientProvider provider, QuicOptions options, RequestEndpoint key)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(options);
        _provider = provider;
        _options = options;
        Key = key;
    }

    /// <summary>The connection target identity (scheme, host, port, version).</summary>
    public RequestEndpoint Key { get; }

    /// <summary>
    /// Opens a typed QUIC stream and returns a <see cref="ConnectionLease"/> for it.
    /// </summary>
    public async Task<ConnectionLease> OpenStreamAsLeaseAsync(
        OutputStreamType streamType, CancellationToken ct = default)
    {
        var (direction, streamFactory) = MapStreamType(streamType);
        var stream = await streamFactory(ct).ConfigureAwait(false);
        return CreateStreamLease(stream, direction);
    }

    /// <summary>
    /// Accepts one server-initiated inbound stream, reads the HTTP/3 stream-type varint,
    /// and wraps it in an <see cref="InboundStream"/>. Returns <c>null</c> when the stream
    /// is unknown or on any error — callers loop until cancelled.
    /// </summary>
    public async Task<InboundStream?> AcceptInboundStreamAsLeaseAsync(CancellationToken ct = default)
    {
        Stream stream;
        try
        {
            stream = await _provider.AcceptInboundStreamAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null; // connection may be dead — caller decides whether to retry
        }

        // Read the stream-type varint (first byte is sufficient for the leading octet decode)
        var typeBuf = new byte[8];
        int bytesRead;
        try
        {
            bytesRead = await stream.ReadAsync(typeBuf.AsMemory(0, 1), ct).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        if (bytesRead == 0)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        if (!QuicVarInt.TryDecode(typeBuf.AsSpan(0, bytesRead), out var streamTypeValue, out _))
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
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
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        var lease = CreateStreamLease(stream, StreamDirection.ReadOnly);
        return new InboundStream(lease, inputStreamType.Value);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>
    /// Creates a <see cref="ConnectionLease"/> for an already-opened QUIC stream,
    /// complete with channels and ByteMover pump tasks.
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
            Key);

        var lease = new ConnectionLease(handle, state);

        // on-close is a no-op: the QuicTransportStateMachine disposes leases via
        // CleanupTransport() on InboundComplete — no additional callback needed.
        _ = ClientByteMover.MoveStreamToChannel(state, static () => { }, lease.Token);
        _ = ClientByteMover.MoveChannelToStream(state, static () => { }, lease.Token);

        return lease;
    }

    private (StreamDirection Direction, Func<CancellationToken, Task<Stream>> StreamFactory)
        MapStreamType(OutputStreamType streamType)
    {
        return streamType switch
        {
            OutputStreamType.Request => (StreamDirection.Bidirectional, _provider.GetStreamAsync),
            OutputStreamType.Control => (StreamDirection.WriteOnly, _provider.GetUnidirectionalStreamAsync),
            OutputStreamType.QpackEncoder => (StreamDirection.WriteOnly, _provider.GetUnidirectionalStreamAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(streamType), streamType,
                "Unknown output stream type"),
        };
    }
}
