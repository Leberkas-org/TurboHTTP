using System;
using System.Collections.Generic;
using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;
using Xunit;

namespace TurboHttp.Tests.RFC9114;

public sealed class FrameDecoderTests
{
    // ───────────────────────── Basic decoding ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-dec-001: Decode DATA frame from wire format")]
    public void Decode_data_frame()
    {
        var original = new Http3DataFrame(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE });
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out var consumed);

        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.NotNull(frame);
        var data = Assert.IsType<Http3DataFrame>(frame);
        Assert.Equal(original.Data.ToArray(), data.Data.ToArray());
        Assert.Equal(wire.Length, consumed);
    }

    [Fact(DisplayName = "RFC-9114-7-dec-002: Decode HEADERS frame from wire format")]
    public void Decode_headers_frame()
    {
        var headerBlock = new byte[] { 0x00, 0x00, 0x82, 0x87, 0x44, 0x88 };
        var original = new Http3HeadersFrame(headerBlock);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.Success, status);
        var headers = Assert.IsType<Http3HeadersFrame>(frame);
        Assert.Equal(headerBlock, headers.HeaderBlock.ToArray());
    }

    [Fact(DisplayName = "RFC-9114-7-dec-003: Decode CANCEL_PUSH frame from wire format")]
    public void Decode_cancel_push_frame()
    {
        var original = new Http3CancelPushFrame(16383);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.Success, status);
        var cp = Assert.IsType<Http3CancelPushFrame>(frame);
        Assert.Equal(16383, cp.PushId);
    }

    [Fact(DisplayName = "RFC-9114-7-dec-004: Decode SETTINGS frame with multiple parameters")]
    public void Decode_settings_frame()
    {
        var parameters = new List<(long, long)>
        {
            (0x06, 4096),   // MAX_FIELD_SECTION_SIZE
            (0x01, 100),    // QPACK_MAX_TABLE_CAPACITY
            (0x07, 50),     // QPACK_BLOCKED_STREAMS
        };
        var original = new Http3SettingsFrame(parameters);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.Success, status);
        var settings = Assert.IsType<Http3SettingsFrame>(frame);
        Assert.Equal(3, settings.Parameters.Count);
        Assert.Equal((0x06L, 4096L), settings.Parameters[0]);
        Assert.Equal((0x01L, 100L), settings.Parameters[1]);
        Assert.Equal((0x07L, 50L), settings.Parameters[2]);
    }

    [Fact(DisplayName = "RFC-9114-7-dec-005: Decode PUSH_PROMISE frame from wire format")]
    public void Decode_push_promise_frame()
    {
        var headerBlock = new byte[] { 0xAA, 0xBB, 0xCC };
        var original = new Http3PushPromiseFrame(42, headerBlock);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.Success, status);
        var pp = Assert.IsType<Http3PushPromiseFrame>(frame);
        Assert.Equal(42, pp.PushId);
        Assert.Equal(headerBlock, pp.HeaderBlock.ToArray());
    }

    [Fact(DisplayName = "RFC-9114-7-dec-006: Decode GOAWAY frame from wire format")]
    public void Decode_goaway_frame()
    {
        var original = new Http3GoAwayFrame(1_000_000);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.Success, status);
        var goaway = Assert.IsType<Http3GoAwayFrame>(frame);
        Assert.Equal(1_000_000, goaway.StreamId);
    }

    [Fact(DisplayName = "RFC-9114-7-dec-007: Decode MAX_PUSH_ID frame from wire format")]
    public void Decode_max_push_id_frame()
    {
        var original = new Http3MaxPushIdFrame(63);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.Success, status);
        var mp = Assert.IsType<Http3MaxPushIdFrame>(frame);
        Assert.Equal(63, mp.PushId);
    }

    // ───────────────────────── Partial frame handling ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-dec-008: Incomplete type varint returns NeedMoreData")]
    public void Partial_type_varint_returns_need_more_data()
    {
        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(ReadOnlySpan<byte>.Empty, out var frame, out _);

        Assert.Equal(Http3DecodeStatus.NeedMoreData, status);
        Assert.Null(frame);
    }

    [Fact(DisplayName = "RFC-9114-7-dec-009: Incomplete payload reassembles across calls")]
    public void Partial_payload_reassembles_across_calls()
    {
        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var original = new Http3DataFrame(payload);
        var wire = original.Serialize();

        // Split at midpoint
        var mid = wire.Length / 2;
        var part1 = wire.AsSpan(0, mid);
        var part2 = wire.AsSpan(mid);

        var decoder = new Http3FrameDecoder();

        // First call — partial data
        var status = decoder.TryDecode(part1, out var frame, out _);
        Assert.Equal(Http3DecodeStatus.NeedMoreData, status);
        Assert.True(decoder.HasRemainder);

        // Second call — complete the frame
        status = decoder.TryDecode(part2, out frame, out _);
        Assert.Equal(Http3DecodeStatus.Success, status);
        var data = Assert.IsType<Http3DataFrame>(frame);
        Assert.Equal(payload, data.Data.ToArray());
        Assert.False(decoder.HasRemainder);
    }

    [Fact(DisplayName = "RFC-9114-7-dec-010: Byte-at-a-time feeding reassembles correctly")]
    public void Byte_at_a_time_feeding()
    {
        var original = new Http3GoAwayFrame(256);
        var wire = original.Serialize();

        var decoder = new Http3FrameDecoder();
        Http3Frame? frame = null;

        for (var i = 0; i < wire.Length; i++)
        {
            var status = decoder.TryDecode(new ReadOnlySpan<byte>(wire, i, 1), out frame, out _);

            if (i < wire.Length - 1)
            {
                Assert.Equal(Http3DecodeStatus.NeedMoreData, status);
            }
            else
            {
                Assert.Equal(Http3DecodeStatus.Success, status);
            }
        }

        var goaway = Assert.IsType<Http3GoAwayFrame>(frame);
        Assert.Equal(256, goaway.StreamId);
    }

    // ───────────────────────── Unknown frame types ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-dec-011: Unknown frame type skipped gracefully")]
    public void Unknown_frame_type_skipped()
    {
        // Encode an unknown frame type (0xFF) with a 3-byte payload
        var buf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0xFF, buf.AsSpan(offset));  // Unknown type
        offset += QuicVarInt.Encode(3, buf.AsSpan(offset));     // Length = 3
        buf[offset++] = 0xAA;
        buf[offset++] = 0xBB;
        buf[offset++] = 0xCC;

        var decoder = new Http3FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(Http3DecodeStatus.Success, status);
        Assert.Null(frame); // Unknown type → null frame, but bytes consumed
        Assert.Equal(offset, consumed);
    }

    // ───────────────────────── DecodeAll ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7-dec-012: DecodeAll decodes multiple concatenated frames")]
    public void DecodeAll_multiple_frames()
    {
        var frames = new Http3Frame[]
        {
            new Http3DataFrame(new byte[] { 0x01 }),
            new Http3GoAwayFrame(0),
            new Http3MaxPushIdFrame(63),
            new Http3SettingsFrame(new List<(long, long)> { (0x06, 4096) }),
        };

        // Serialize all frames into a single buffer
        var totalSize = 0;
        foreach (var f in frames)
        {
            totalSize += f.SerializedSize;
        }

        var wire = new byte[totalSize];
        var offset = 0;
        foreach (var f in frames)
        {
            var span = wire.AsSpan(offset);
            offset += f.WriteTo(ref span);
        }

        var decoder = new Http3FrameDecoder();
        var decoded = decoder.DecodeAll(wire, out var consumed);

        Assert.Equal(4, decoded.Count);
        Assert.Equal(totalSize, consumed);
        Assert.IsType<Http3DataFrame>(decoded[0]);
        Assert.IsType<Http3GoAwayFrame>(decoded[1]);
        Assert.IsType<Http3MaxPushIdFrame>(decoded[2]);
        Assert.IsType<Http3SettingsFrame>(decoded[3]);
    }
}
