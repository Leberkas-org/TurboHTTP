using Servus.Akka.Transport;
using TurboHTTP.Protocol.Http3;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Http3.Connection;

/// <summary>
/// Tests for HTTP/3 stream type uniqueness validation.
///
/// RFC 9114 §6.2.1 requires that control, encoder, and decoder streams be unique.
/// The new StateMachine API delegates stream type resolution to the internal ProtocolHandler,
/// which is triggered when DecodeServerData receives a ServerStreamAccepted event followed by
/// stream data containing the stream type byte.
///
/// These tests verify that duplicate stream type declarations are rejected by observing
/// Outbound warnings or connection failures.
/// </summary>
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
        // No errors should be logged
        var warnings = _ops.Warnings.ToList();
        Assert.Empty(warnings);
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
        var ex = Assert.Throws<Http3Exception>(() => sm.DecodeServerData(new MultiplexedData(buf2, 5)));
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
        var ex = Assert.Throws<Http3Exception>(() => sm.DecodeServerData(new MultiplexedData(buf2, 5)));
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
        var ex = Assert.Throws<Http3Exception>(() => sm.DecodeServerData(new MultiplexedData(buf2, 5)));
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
        var warnings = _ops.Warnings.ToList();
        var hasError = warnings.Any(w => w.Contains("Duplicate"));
        Assert.False(hasError, "Should not reject different stream types");
    }
}
