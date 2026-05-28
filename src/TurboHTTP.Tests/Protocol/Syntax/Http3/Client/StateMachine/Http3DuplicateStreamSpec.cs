using Servus.Akka.Transport;
using TurboHTTP.Client;
using TurboHTTP.Protocol.Syntax.Http3;
using TurboHTTP.Protocol.Syntax.Http3.Client;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Client.StateMachine;

public sealed class Http3DuplicateStreamSpec
{
    private readonly FakeOps _ops = new();

    private Http3ClientStateMachine CreateMachine()
    {
        return new Http3ClientStateMachine(new TurboClientOptions(), _ops);
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
    public void DecodeServerData_should_accept_control_stream_opening()
    {
        var sm = CreateMachine();
        sm.PreStart();
        _ops.Outbound.Clear();

        sm.DecodeServerData(new ServerStreamAccepted(3, StreamDirection.Unidirectional));

        var buf = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf, 3));

        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_reject_duplicate_control_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 3));

        sm.DecodeServerData(new ServerStreamAccepted(7, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);

        var ex = Assert.Throws<HttpProtocolException>(() => sm.DecodeServerData(new MultiplexedData(buf2, 7)));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_reject_duplicate_encoder_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 3));

        sm.DecodeServerData(new ServerStreamAccepted(7, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);

        var ex = Assert.Throws<HttpProtocolException>(() => sm.DecodeServerData(new MultiplexedData(buf2, 7)));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_reject_duplicate_decoder_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();

        sm.DecodeServerData(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.QpackDecoder, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 3));

        sm.DecodeServerData(new ServerStreamAccepted(7, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackDecoder, [0x00]);

        var ex = Assert.Throws<HttpProtocolException>(() => sm.DecodeServerData(new MultiplexedData(buf2, 7)));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_allow_different_critical_stream_types()
    {
        var sm = CreateMachine();
        sm.PreStart();
        _ops.Outbound.Clear();

        sm.DecodeServerData(new ServerStreamAccepted(3, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 3));

        sm.DecodeServerData(new ServerStreamAccepted(7, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf2, 7));

        Assert.True(true);
    }
}
