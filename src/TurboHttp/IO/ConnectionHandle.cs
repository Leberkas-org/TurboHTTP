using System.Buffers;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

/// <summary>
/// Bundles the Channel read/write handles for a single TCP connection,
/// allowing ConnectionStage to get direct access to TCP I/O without actor messages.
/// </summary>
public sealed record ConnectionHandle(
    ChannelWriter<(IMemoryOwner<byte> Buffer, int ReadableBytes)> OutboundWriter,
    ChannelReader<(IMemoryOwner<byte> Buffer, int ReadableBytes)> InboundReader,
    HostKey Key,
    IActorRef ConnectionActor);
