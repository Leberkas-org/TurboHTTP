using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.Internal;

namespace TurboHttp.Lifecycle;

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

    public int MaxConcurrentStreams => _maxConcurrentStreams;

    public void UpdateMaxConcurrentStreams(int value) => _maxConcurrentStreams = value;

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
