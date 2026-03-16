using System.Buffers;
using System.Net;
using System.Threading.Channels;
using Akka.Actor;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class ConnectionHandleTests
{
    private static readonly HostKey TestKey = new()
    {
        Scheme = "https",
        Host = "example.com",
        Port = 443,
        Version = HttpVersion.Version11
    };

    [Fact]
    public void CH_001_CanConstruct_And_ReadBackProperties()
    {
        var outboundChannel = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inboundChannel = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();

        var handle = new ConnectionHandle(
            OutboundWriter: outboundChannel.Writer,
            InboundReader: inboundChannel.Reader,
            Key: TestKey,
            ConnectionActor: ActorRefs.Nobody);

        Assert.Same(outboundChannel.Writer, handle.OutboundWriter);
        Assert.Same(inboundChannel.Reader, handle.InboundReader);
        Assert.Equal(TestKey, handle.Key);
        Assert.Same(ActorRefs.Nobody, handle.ConnectionActor);
    }

    [Fact]
    public void CH_002_Equality_SameChannels_AreEqual()
    {
        var outboundChannel = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inboundChannel = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();

        var handle1 = new ConnectionHandle(outboundChannel.Writer, inboundChannel.Reader, TestKey, ActorRefs.Nobody);
        var handle2 = new ConnectionHandle(outboundChannel.Writer, inboundChannel.Reader, TestKey, ActorRefs.Nobody);

        Assert.Equal(handle1, handle2);
    }

    [Fact]
    public void CH_003_Inequality_DifferentChannels_AreNotEqual()
    {
        var outbound1 = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var outbound2 = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();
        var inbound = Channel.CreateUnbounded<(IMemoryOwner<byte> Buffer, int ReadableBytes)>();

        var handle1 = new ConnectionHandle(outbound1.Writer, inbound.Reader, TestKey, ActorRefs.Nobody);
        var handle2 = new ConnectionHandle(outbound2.Writer, inbound.Reader, TestKey, ActorRefs.Nobody);

        Assert.NotEqual(handle1, handle2);
    }
}
