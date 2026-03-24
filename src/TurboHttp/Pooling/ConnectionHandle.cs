using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;

namespace TurboHttp.Pooling;

/// <summary>
/// Bundles the Channel read/write handles for a single TCP connection,
/// allowing ConnectionStage to get direct access to TCP I/O without actor messages.
/// </summary>
public sealed record ConnectionHandle(
    ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> OutboundWriter,
    ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)> InboundReader,
    RequestEndpoint Key,
    IActorRef ConnectionActor)
{
    private volatile int _maxConcurrentStreams = 100;
    private volatile TlsCloseKind _closeKind;

    public int MaxConcurrentStreams => _maxConcurrentStreams;

    public void UpdateMaxConcurrentStreams(int value) => _maxConcurrentStreams = value;

    /// <summary>
    /// Indicates how the transport connection was closed.
    /// Set by <see cref="TurboHttp.Transport.ClientByteMover"/> via <see cref="TurboHttp.Transport.ClientState"/>
    /// and read by <see cref="TurboHttp.Transport.ConnectionStage"/> when the inbound pump completes.
    /// </summary>
    public TlsCloseKind CloseKind => _closeKind;

    public void SetCloseKind(TlsCloseKind value) => _closeKind = value;

    /// <summary>
    /// Creates a <see cref="ConnectionHandle"/> for the direct (non-actor) connection path.
    /// Uses <see cref="ActorRefs.Nobody"/> as the connection actor since no actor is involved.
    /// </summary>
    public static ConnectionHandle CreateDirect(
        ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> outboundWriter,
        ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)> inboundReader,
        RequestEndpoint key)
    {
        return new ConnectionHandle(outboundWriter, inboundReader, key, ActorRefs.Nobody);
    }

    public bool Equals(ConnectionHandle? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return EqualityContract == other.EqualityContract
            && EqualityComparer<ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)>>.Default.Equals(OutboundWriter, other.OutboundWriter)
            && EqualityComparer<ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)>>.Default.Equals(InboundReader, other.InboundReader)
            && Key.Equals(other.Key)
            && EqualityComparer<IActorRef>.Default.Equals(ConnectionActor, other.ConnectionActor);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(EqualityContract, OutboundWriter, InboundReader, Key, ConnectionActor);
    }
}
