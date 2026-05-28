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

        // Server opens unidirectional stream 1 as control stream
        sm.DecodeServerData(new ServerStreamAccepted(1, StreamDirection.Unidirectional));

        var buf = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf, 1));

        // Should process without errors
        Assert.True(true);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_reject_duplicate_control_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();

        // First control stream on QUIC stream 1
        sm.DecodeServerData(new ServerStreamAccepted(1, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 1));

        // Second control stream on QUIC stream 5 should be rejected
        sm.DecodeServerData(new ServerStreamAccepted(5, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);

        // This should throw an Http3Exception due to duplicate control stream
        var ex = Assert.Throws<HttpProtocolException>(() => sm.DecodeServerData(new MultiplexedData(buf2, 5)));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_reject_duplicate_encoder_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();

        // First encoder stream on QUIC stream 1
        sm.DecodeServerData(new ServerStreamAccepted(1, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 1));

        // Second encoder stream on QUIC stream 5 should be rejected
        sm.DecodeServerData(new ServerStreamAccepted(5, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);

        // This should throw an Http3Exception due to duplicate encoder stream
        var ex = Assert.Throws<HttpProtocolException>(() => sm.DecodeServerData(new MultiplexedData(buf2, 5)));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_reject_duplicate_decoder_stream()
    {
        var sm = CreateMachine();
        sm.PreStart();

        // First decoder stream on QUIC stream 1
        sm.DecodeServerData(new ServerStreamAccepted(1, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.QpackDecoder, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 1));

        // Second decoder stream on QUIC stream 5 should be rejected
        sm.DecodeServerData(new ServerStreamAccepted(5, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackDecoder, [0x00]);

        // This should throw an Http3Exception due to duplicate decoder stream
        var ex = Assert.Throws<HttpProtocolException>(() => sm.DecodeServerData(new MultiplexedData(buf2, 5)));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void DecodeServerData_should_allow_different_critical_stream_types()
    {
        var sm = CreateMachine();
        sm.PreStart();
        _ops.Outbound.Clear();

        // Control stream on QUIC stream 1
        sm.DecodeServerData(new ServerStreamAccepted(1, StreamDirection.Unidirectional));
        var buf1 = BuildStreamTypeBuffer(StreamType.Control, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf1, 1));

        // Encoder stream on QUIC stream 5
        sm.DecodeServerData(new ServerStreamAccepted(5, StreamDirection.Unidirectional));
        var buf2 = BuildStreamTypeBuffer(StreamType.QpackEncoder, [0x00]);
        sm.DecodeServerData(new MultiplexedData(buf2, 5));

        // Should accept both without errors
        Assert.True(true);
    }
}