using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

public sealed class Http3DuplicateStreamSpec
{
    private readonly FakeOps _ops = new();

    private StateMachine CreateMachine()
    {
        return new StateMachine(new TurboClientOptions(), _ops);
    }

    private static TransportBuffer BuildStreamTypeBuffer(StreamType type, byte[]? trailingData = null)
    {
        var typeBytes = QuicVarInt.EncodedLength((long)type);
        var totalSize = typeBytes + (trailingData?.Length ?? 0);
        var buf = TransportBuffer.Rent(totalSize);
        var span = buf.FullMemory.Span;
        var written = QuicVarInt.Encode((long)type, span);
        trailingData?.CopyTo(span[written..]);
        buf.Length = totalSize;
        return buf;
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void ResolveStreamId_should_accept_first_control_stream()
    {
        var sm = CreateMachine();
        sm.OnServerStreamOpened(1);

        var buf = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        var (logicalId, _) = sm.ResolveStreamId(1, buf);

        Assert.Equal(-2, logicalId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void ResolveStreamId_should_throw_on_duplicate_control_stream()
    {
        var sm = CreateMachine();

        sm.OnServerStreamOpened(1);
        var buf1 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.ResolveStreamId(1, buf1);

        sm.OnServerStreamOpened(5);
        var buf2 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);

        var ex = Assert.Throws<Http3Exception>(() => sm.ResolveStreamId(5, buf2));
        Assert.Contains("Duplicate stream type", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void ResolveStreamId_should_throw_on_duplicate_encoder_stream()
    {
        var sm = CreateMachine();

        sm.OnServerStreamOpened(1);
        var buf1 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);
        sm.ResolveStreamId(1, buf1);

        sm.OnServerStreamOpened(5);
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);

        Assert.Throws<Http3Exception>(() => sm.ResolveStreamId(5, buf2));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void ResolveStreamId_should_throw_on_duplicate_decoder_stream()
    {
        var sm = CreateMachine();

        sm.OnServerStreamOpened(1);
        var buf1 = BuildStreamTypeBuffer(StreamType.QpackDecoder, [0x00]);
        sm.ResolveStreamId(1, buf1);

        sm.OnServerStreamOpened(5);
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackDecoder, [0x00]);

        Assert.Throws<Http3Exception>(() => sm.ResolveStreamId(5, buf2));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void ResolveStreamId_should_allow_different_critical_stream_types()
    {
        var sm = CreateMachine();

        sm.OnServerStreamOpened(1);
        var buf1 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.ResolveStreamId(1, buf1);

        sm.OnServerStreamOpened(5);
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);
        var (logicalId, _) = sm.ResolveStreamId(5, buf2);

        Assert.Equal(-3, logicalId);
    }
}
