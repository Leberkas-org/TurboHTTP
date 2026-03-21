using System;
using System.Buffers;
using System.Collections.Generic;
using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class FrameEncoderTests
{
    // ───────────────────────── Encode(Http3Frame, Span) ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-enc-001: Encode writes all 7 frame types to span")]
    public void Encode_writes_all_frame_types()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        Http3Frame[] frames =
        [
            new Http3DataFrame(payload),
            new Http3HeadersFrame(payload),
            new Http3CancelPushFrame(42),
            new Http3SettingsFrame(new List<(long, long)> { (0x06, 4096) }),
            new Http3PushPromiseFrame(7, payload),
            new Http3GoAwayFrame(100),
            new Http3MaxPushIdFrame(15),
        ];

        foreach (var frame in frames)
        {
            var buf = new byte[frame.SerializedSize];
            var written = Http3FrameEncoder.Encode(frame, buf);
            Assert.Equal(frame.SerializedSize, written);

            // Verify matches frame.Serialize()
            Assert.Equal(frame.Serialize(), buf);
        }
    }

    [Fact(DisplayName = "RFC-9114-7-enc-002: Encode to span throws on insufficient space")]
    public void Encode_span_throws_on_small_buffer()
    {
        var frame = new Http3DataFrame(new byte[] { 0xCA, 0xFE });
        var buf = new byte[1]; // Too small
        Assert.Throws<ArgumentException>(() => Http3FrameEncoder.Encode(frame, buf));
    }

    // ───────────────────────── Encode(Http3Frame, IBufferWriter) ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-enc-003: Encode writes frame to IBufferWriter")]
    public void Encode_writes_to_buffer_writer()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var frame = new Http3DataFrame(payload);
        var writer = new ArrayBufferWriter<byte>();

        var written = Http3FrameEncoder.Encode(frame, writer);

        Assert.Equal(frame.SerializedSize, written);
        Assert.Equal(frame.SerializedSize, writer.WrittenCount);
        Assert.Equal(frame.Serialize(), writer.WrittenSpan.ToArray());
    }

    // ───────────────────────── EncodeAll ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-enc-004: EncodeAll writes multiple frames to IBufferWriter")]
    public void EncodeAll_writes_multiple_frames()
    {
        var frames = new Http3Frame[]
        {
            new Http3DataFrame(new byte[] { 0x01 }),
            new Http3GoAwayFrame(0),
            new Http3MaxPushIdFrame(63),
        };

        var writer = new ArrayBufferWriter<byte>();
        var total = Http3FrameEncoder.EncodeAll(frames, writer);

        var expectedSize = 0;
        foreach (var f in frames)
        {
            expectedSize += f.SerializedSize;
        }

        Assert.Equal(expectedSize, total);
        Assert.Equal(expectedSize, writer.WrittenCount);

        // Verify wire format by decoding type+length of each frame
        var span = writer.WrittenSpan;
        foreach (var f in frames)
        {
            QuicVarInt.TryDecode(span, out var frameType, out var consumed);
            Assert.Equal((long)f.Type, frameType);
            span = span[consumed..];

            QuicVarInt.TryDecode(span, out var length, out consumed);
            span = span[consumed..];
            span = span[(int)length..]; // skip payload
        }

        Assert.Equal(0, span.Length);
    }

    // ───────────────────────── Direct encoding methods ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-enc-005: EncodeData produces same wire format as Http3DataFrame")]
    public void EncodeData_matches_frame()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var expected = new Http3DataFrame(payload).Serialize();

        var buf = new byte[expected.Length];
        var written = Http3FrameEncoder.EncodeData(payload, buf);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, buf);
    }

    [Fact(DisplayName = "RFC-9114-7-enc-006: EncodeHeaders produces same wire format as Http3HeadersFrame")]
    public void EncodeHeaders_matches_frame()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x87 };
        var expected = new Http3HeadersFrame(headerBlock).Serialize();

        var buf = new byte[expected.Length];
        var written = Http3FrameEncoder.EncodeHeaders(headerBlock, buf);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, buf);
    }

    [Fact(DisplayName = "RFC-9114-7-enc-007: EncodeSettings produces same wire format as Http3SettingsFrame")]
    public void EncodeSettings_matches_frame()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096),  // MAX_FIELD_SECTION_SIZE
            (0x01, 100),   // QPACK_MAX_TABLE_CAPACITY
            (0x07, 50),    // QPACK_BLOCKED_STREAMS
        };
        var expected = new Http3SettingsFrame(parameters).Serialize();

        var buf = new byte[expected.Length];
        var written = Http3FrameEncoder.EncodeSettings(parameters, buf);

        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, buf);
    }

    [Fact(DisplayName = "RFC-9114-7-enc-008: EncodeCancelPush/GoAway/MaxPushId/PushPromise match frame objects")]
    public void Direct_encode_single_varint_frames_match()
    {
        // CancelPush
        var cpExpected = new Http3CancelPushFrame(16383).Serialize();
        var cpBuf = new byte[cpExpected.Length];
        Assert.Equal(cpExpected.Length, Http3FrameEncoder.EncodeCancelPush(16383, cpBuf));
        Assert.Equal(cpExpected, cpBuf);

        // GoAway
        var gaExpected = new Http3GoAwayFrame(1_000_000).Serialize();
        var gaBuf = new byte[gaExpected.Length];
        Assert.Equal(gaExpected.Length, Http3FrameEncoder.EncodeGoAway(1_000_000, gaBuf));
        Assert.Equal(gaExpected, gaBuf);

        // MaxPushId
        var mpExpected = new Http3MaxPushIdFrame(63).Serialize();
        var mpBuf = new byte[mpExpected.Length];
        Assert.Equal(mpExpected.Length, Http3FrameEncoder.EncodeMaxPushId(63, mpBuf));
        Assert.Equal(mpExpected, mpBuf);

        // PushPromise
        var headerBlock = new byte[] { 0xAA, 0xBB };
        var ppExpected = new Http3PushPromiseFrame(7, headerBlock).Serialize();
        var ppBuf = new byte[ppExpected.Length];
        Assert.Equal(ppExpected.Length, Http3FrameEncoder.EncodePushPromise(7, headerBlock, ppBuf));
        Assert.Equal(ppExpected, ppBuf);
    }

    [Fact(DisplayName = "RFC-9114-7-enc-009: Direct encode methods use QUIC varint for type and length")]
    public void Direct_encode_uses_quic_varint()
    {
        // Use a payload size > 63 to force 2-byte varint length
        var payload = new byte[100];
        var buf = new byte[256];
        var written = Http3FrameEncoder.EncodeData(payload, buf);

        var span = new ReadOnlySpan<byte>(buf, 0, written);

        // Decode type
        QuicVarInt.TryDecode(span, out var frameType, out var consumed);
        Assert.Equal((long)Http3FrameType.Data, frameType);
        span = span[consumed..];

        // Decode length — should be 100 (2-byte varint since > 63)
        QuicVarInt.TryDecode(span, out var length, out consumed);
        Assert.Equal(100, length);
        Assert.Equal(2, consumed); // 100 requires 2-byte varint
        span = span[consumed..];

        // Remaining is the payload
        Assert.Equal(100, span.Length);
    }

    [Fact(DisplayName = "RFC-9114-7-enc-010: Direct encode methods reject invalid arguments")]
    public void Direct_encode_rejects_invalid_args()
    {
        var buf = new byte[256];

        Assert.Throws<ArgumentOutOfRangeException>(() => Http3FrameEncoder.EncodeCancelPush(-1, buf));
        Assert.Throws<ArgumentOutOfRangeException>(() => Http3FrameEncoder.EncodeGoAway(-1, buf));
        Assert.Throws<ArgumentOutOfRangeException>(() => Http3FrameEncoder.EncodeMaxPushId(-1, buf));
        Assert.Throws<ArgumentOutOfRangeException>(() => Http3FrameEncoder.EncodePushPromise(-1, ReadOnlySpan<byte>.Empty, buf));

        // Insufficient buffer
        Assert.Throws<ArgumentException>(() => Http3FrameEncoder.EncodeData(new byte[10], new byte[1]));
        Assert.Throws<ArgumentException>(() => Http3FrameEncoder.EncodeHeaders(new byte[10], new byte[1]));
    }
}
