using System;
using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class UniStreamTests
{
    private static byte[] EncodeStreamType(long type)
    {
        var buf = new byte[QuicVarInt.EncodedLength(type)];
        QuicVarInt.Encode(type, buf);
        return buf;
    }

    // --- Stream Type Identification ---

    [Theory(DisplayName = "RFC9114-6.2-US-001: Known stream types are identified correctly")]
    [InlineData(0x00L, UniStreamRouting.Control)]
    [InlineData(0x01L, UniStreamRouting.Push)]
    [InlineData(0x02L, UniStreamRouting.QpackEncoder)]
    [InlineData(0x03L, UniStreamRouting.QpackDecoder)]
    public void TryIdentify_KnownTypes_RoutesCorrectly(long typeValue, UniStreamRouting expected)
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType(typeValue);

        var result = handler.TryIdentify(data, out var routing, out var streamType, out var consumed);

        Assert.True(result);
        Assert.Equal(expected, routing);
        Assert.Equal(typeValue, streamType);
        Assert.Equal(data.Length, consumed);
    }

    [Fact(DisplayName = "RFC9114-6.2-US-002: Unknown stream types are ignored, not errors")]
    public void TryIdentify_UnknownType_ReturnsUnknown()
    {
        var handler = new Http3UniStream();
        // 0x21 is a reserved/unknown stream type (not 0x00-0x03)
        var data = EncodeStreamType(0x21);

        var result = handler.TryIdentify(data, out var routing, out var streamType, out var consumed);

        Assert.True(result);
        Assert.Equal(UniStreamRouting.Unknown, routing);
        Assert.Equal(0x21L, streamType);
        Assert.True(consumed > 0);
    }

    [Theory(DisplayName = "RFC9114-6.2-US-003: Multiple unknown stream types are all ignored")]
    [InlineData(0x21L)]
    [InlineData(0x42L)]
    [InlineData(0xFF_FFL)]
    [InlineData(0x1FL)]
    public void TryIdentify_VariousUnknownTypes_AllIgnored(long unknownType)
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType(unknownType);

        var result = handler.TryIdentify(data, out var routing, out _, out _);

        Assert.True(result);
        Assert.Equal(UniStreamRouting.Unknown, routing);
    }

    // --- QPACK Stream Routing ---

    [Fact(DisplayName = "RFC9114-6.2-US-004: QPACK encoder stream routes correctly and tracks state")]
    public void TryIdentify_QpackEncoder_TracksState()
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType((long)Http3StreamType.QpackEncoder);

        Assert.False(handler.QpackEncoderStreamReceived);

        handler.TryIdentify(data, out var routing, out _, out _);

        Assert.Equal(UniStreamRouting.QpackEncoder, routing);
        Assert.True(handler.QpackEncoderStreamReceived);
    }

    [Fact(DisplayName = "RFC9114-6.2-US-005: QPACK decoder stream routes correctly and tracks state")]
    public void TryIdentify_QpackDecoder_TracksState()
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType((long)Http3StreamType.QpackDecoder);

        Assert.False(handler.QpackDecoderStreamReceived);

        handler.TryIdentify(data, out var routing, out _, out _);

        Assert.Equal(UniStreamRouting.QpackDecoder, routing);
        Assert.True(handler.QpackDecoderStreamReceived);
    }

    // --- Duplicate Critical Stream Detection ---

    [Fact(DisplayName = "RFC9114-6.2-US-006: Duplicate control stream is a connection error")]
    public void TryIdentify_DuplicateControlStream_Throws()
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType((long)Http3StreamType.Control);

        handler.TryIdentify(data, out _, out _, out _);

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.TryIdentify(data, out _, out _, out _));
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-6.2-US-007: Duplicate QPACK encoder stream is a connection error")]
    public void TryIdentify_DuplicateQpackEncoder_Throws()
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType((long)Http3StreamType.QpackEncoder);

        handler.TryIdentify(data, out _, out _, out _);

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.TryIdentify(data, out _, out _, out _));
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-6.2-US-008: Duplicate QPACK decoder stream is a connection error")]
    public void TryIdentify_DuplicateQpackDecoder_Throws()
    {
        var handler = new Http3UniStream();
        var data = EncodeStreamType((long)Http3StreamType.QpackDecoder);

        handler.TryIdentify(data, out _, out _, out _);

        var ex = Assert.Throws<Http3ConnectionException>(
            () => handler.TryIdentify(data, out _, out _, out _));
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    // --- Buffer Handling ---

    [Fact(DisplayName = "RFC9114-6.2-US-009: Empty buffer returns false (incomplete)")]
    public void TryIdentify_EmptyBuffer_ReturnsFalse()
    {
        var handler = new Http3UniStream();

        var result = handler.TryIdentify(ReadOnlySpan<byte>.Empty, out var routing, out var streamType, out var consumed);

        Assert.False(result);
        Assert.Equal(UniStreamRouting.Unknown, routing);
        Assert.Equal(-1L, streamType);
        Assert.Equal(0, consumed);
    }
}
