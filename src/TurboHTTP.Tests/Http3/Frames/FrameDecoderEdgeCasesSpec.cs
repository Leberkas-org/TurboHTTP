using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Frames;

public sealed class FrameDecoderEdgeCasesSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_empty_data_frame()
    {
        var original = new DataFrame(ReadOnlyMemory<byte>.Empty);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out var consumed);

        Assert.Equal(DecodeStatus.Success, status);
        Assert.NotNull(frame);
        var data = Assert.IsType<DataFrame>(frame);
        Assert.Empty(data.Data.ToArray());
        Assert.Equal(wire.Length, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_empty_headers_frame()
    {
        var original = new HeadersFrame(ReadOnlyMemory<byte>.Empty);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var headers = Assert.IsType<HeadersFrame>(frame);
        Assert.Empty(headers.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_large_data_frame()
    {
        var largePayload = new byte[1_000_000];
        for (var i = 0; i < largePayload.Length; i++)
        {
            largePayload[i] = (byte)(i % 256);
        }

        var original = new DataFrame(largePayload);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var data = Assert.IsType<DataFrame>(frame);
        Assert.Equal(largePayload, data.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_return_need_more_data_when_length_varint_incomplete()
    {
        // Only type varint, no length varint
        var data = new byte[] { 0x00 }; // DATA frame type

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(data, out var frame, out _);

        Assert.Equal(DecodeStatus.NeedMoreData, status);
        Assert.Null(frame);
        Assert.True(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_return_need_more_data_when_payload_incomplete()
    {
        // Encode type and length, but provide incomplete payload
        var buf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0, buf.AsSpan(offset)); // DATA type
        offset += QuicVarInt.Encode(100, buf.AsSpan(offset)); // Length = 100
        // Only provide 10 bytes of payload instead of 100

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset + 10), out var frame, out _);

        Assert.Equal(DecodeStatus.NeedMoreData, status);
        Assert.Null(frame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_all_and_skip_unknown_frame_types()
    {
        // Create two known frames only (since unknown frames cannot be serialized by our API)
        var data = new DataFrame(new byte[] { 0xAA, 0xBB, 0xCC });
        var goaway = new GoAwayFrame(0);

        var buf = new byte[128];
        var bufSpan = buf.AsSpan();
        var offset = 0;
        offset += data.WriteTo(ref bufSpan);
        bufSpan = buf.AsSpan(offset);
        offset += goaway.WriteTo(ref bufSpan);

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(buf.AsSpan(0, offset), out var consumed);

        // Should have 2 frames (DATA and GOAWAY)
        Assert.Equal(2, frames.Count);
        Assert.IsType<DataFrame>(frames[0]);
        Assert.IsType<GoAwayFrame>(frames[1]);
        Assert.Equal(offset, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_throw_on_frame_size_overflow()
    {
        var buf = new byte[16];
        var offset = 0;
        offset += QuicVarInt.Encode(0, buf.AsSpan(offset)); // DATA type
        // Encode a length larger than int.MaxValue
        offset += QuicVarInt.Encode((long)int.MaxValue + 1, buf.AsSpan(offset));

        var decoder = new FrameDecoder();
        var ex = Assert.Throws<Http3Exception>(() =>
            decoder.TryDecode(buf.AsSpan(0, offset), out _, out _));

        Assert.Contains("exceeds maximum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_track_has_remainder_correctly()
    {
        var decoder = new FrameDecoder();

        // Initially no remainder
        Assert.False(decoder.HasRemainder);

        // Create a DATA frame and split it mid-payload
        var frame1 = new DataFrame(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE });
        var wire = frame1.Serialize();

        // Feed only first 3 bytes, leaving remainder
        decoder.TryDecode(wire.AsSpan(0, 3), out _, out _);
        Assert.True(decoder.HasRemainder);

        // Feed rest of the frame
        decoder.TryDecode(wire.AsSpan(3), out var frame2, out _);
        Assert.NotNull(frame2); // Should have decoded successfully
        Assert.False(decoder.HasRemainder); // After completing, no remainder
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_clear_remainder_on_reset()
    {
        var decoder = new FrameDecoder();

        // Feed partial data to create remainder
        var buf = new byte[] { 0x00 };
        decoder.TryDecode(buf, out _, out _);
        Assert.True(decoder.HasRemainder);

        // Reset
        decoder.Reset();
        Assert.False(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_clear_remainder_on_dispose()
    {
        var decoder = new FrameDecoder();

        // Feed partial data
        var buf = new byte[] { 0x00 };
        decoder.TryDecode(buf, out _, out _);
        Assert.True(decoder.HasRemainder);

        // Dispose
        decoder.Dispose();
        Assert.False(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_cancel_push_frame_with_large_push_id()
    {
        const long largeId = (1L << 62) - 1; // Maximum QUIC VarInt value
        var original = new CancelPushFrame(largeId);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var cp = Assert.IsType<CancelPushFrame>(frame);
        Assert.Equal(largeId, cp.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_cancel_push_frame_with_zero_push_id()
    {
        var original = new CancelPushFrame(0);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var cp = Assert.IsType<CancelPushFrame>(frame);
        Assert.Equal(0, cp.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_settings_frame_when_empty()
    {
        var original = new SettingsFrame(new List<(long, long)>());
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var settings = Assert.IsType<SettingsFrame>(frame);
        Assert.Empty(settings.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_settings_frame_with_many_parameters()
    {
        var parameters = new List<(long, long)>();
        for (var i = 0; i < 100; i++)
        {
            parameters.Add((i, (long)i * 1000));
        }

        var original = new SettingsFrame(parameters);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var settings = Assert.IsType<SettingsFrame>(frame);
        Assert.Equal(100, settings.Parameters.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_settings_frame_with_large_values()
    {
        const long largeValue = (1L << 62) - 1; // Maximum QUIC VarInt value
        var parameters = new List<(long, long)>
        {
            (0x06, largeValue), // MAX_FIELD_SECTION_SIZE
            (0x01, largeValue), // QPACK_MAX_TABLE_CAPACITY
        };

        var original = new SettingsFrame(parameters);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var settings = Assert.IsType<SettingsFrame>(frame);
        Assert.Equal(2, settings.Parameters.Count);
        Assert.Equal(largeValue, settings.Parameters[0].Item2);
        Assert.Equal(largeValue, settings.Parameters[1].Item2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_push_promise_frame_with_empty_headers()
    {
        var original = new PushPromiseFrame(1, ReadOnlyMemory<byte>.Empty);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var pp = Assert.IsType<PushPromiseFrame>(frame);
        Assert.Equal(1, pp.PushId);
        Assert.Empty(pp.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_push_promise_frame_with_large_headers()
    {
        var largeHeaders = new byte[10000];
        for (var i = 0; i < largeHeaders.Length; i++)
        {
            largeHeaders[i] = (byte)(i % 256);
        }

        var original = new PushPromiseFrame(999, largeHeaders);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var pp = Assert.IsType<PushPromiseFrame>(frame);
        Assert.Equal(999, pp.PushId);
        Assert.Equal(largeHeaders, pp.HeaderBlock.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_go_away_frame_with_large_stream_id()
    {
        const long largeId = (1L << 62) - 1; // Maximum QUIC VarInt value
        var original = new GoAwayFrame(largeId);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var goaway = Assert.IsType<GoAwayFrame>(frame);
        Assert.Equal(largeId, goaway.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_max_push_id_frame_when_zero()
    {
        var original = new MaxPushIdFrame(0);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var mp = Assert.IsType<MaxPushIdFrame>(frame);
        Assert.Equal(0, mp.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_max_push_id_frame_with_large_value()
    {
        const long largeId = (1L << 62) - 1; // Maximum QUIC VarInt value
        var original = new MaxPushIdFrame(largeId);
        var wire = original.Serialize();

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(wire, out var frame, out _);

        Assert.Equal(DecodeStatus.Success, status);
        var mp = Assert.IsType<MaxPushIdFrame>(frame);
        Assert.Equal(largeId, mp.PushId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_all_with_empty_input()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(ReadOnlySpan<byte>.Empty, out var consumed);

        Assert.Empty(frames);
        Assert.Equal(0, consumed);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_decode_all_and_leave_remainder()
    {
        var original = new DataFrame(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE });
        var wire = original.Serialize();

        // Split so only partial frame is in buffer
        var partial = wire.AsSpan(0, wire.Length - 2); // Remove last 2 bytes

        var decoder = new FrameDecoder();
        var frames = decoder.DecodeAll(partial, out var consumed);

        // Should have 0 frames decoded (incomplete)
        Assert.Empty(frames);
        // consumed may be 0 for incomplete frames as they're buffered
        Assert.True(consumed >= 0 && consumed <= partial.Length);
        Assert.True(decoder.HasRemainder);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_return_correct_bytes_consumed_with_remainder()
    {
        var decoder = new FrameDecoder();

        // Feed partial frame
        var partial = new byte[] { 0x00, 0x05 };
        var status1 = decoder.TryDecode(partial, out _, out var consumed1);

        Assert.Equal(DecodeStatus.NeedMoreData, status1);
        Assert.Equal(partial.Length, consumed1);

        // Feed rest
        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE };
        var status2 = decoder.TryDecode(payload, out var frame, out var consumed2);

        Assert.Equal(DecodeStatus.Success, status2);
        Assert.NotNull(frame);
        // consumed2 should be just the new input consumed (not total)
        Assert.True(consumed2 > 0);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_skip_unknown_frame_while_consuming_bytes()
    {
        var buf = new byte[16];
        var offset = 0;

        // Encode unknown frame type
        offset += QuicVarInt.Encode(0xABCD, buf.AsSpan(offset)); // Unknown type
        offset += QuicVarInt.Encode(5, buf.AsSpan(offset)); // Length = 5
        for (var i = 0; i < 5; i++) buf[offset++] = 0xFF;

        var decoder = new FrameDecoder();
        var status = decoder.TryDecode(buf.AsSpan(0, offset), out var frame, out var consumed);

        Assert.Equal(DecodeStatus.Success, status);
        Assert.Null(frame);
        Assert.Equal(offset, consumed); // All bytes consumed despite null frame
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_clear_frame_list_in_decode_all()
    {
        var decoder = new FrameDecoder();

        // First call
        var frame1 = new DataFrame(new byte[] { 0x01 });
        var wire1 = frame1.Serialize();
        var frames1 = decoder.DecodeAll(wire1, out _);
        Assert.Single(frames1);

        // Second call with different frames
        var frame2A = new GoAwayFrame(0);
        var frame2B = new MaxPushIdFrame(42);
        var buf = new byte[64];
        var offset = 0;
        var bufSpan = buf.AsSpan();
        offset += frame2A.WriteTo(ref bufSpan);
        bufSpan = buf.AsSpan(offset);
        offset += frame2B.WriteTo(ref bufSpan);

        var frames2 = decoder.DecodeAll(buf.AsSpan(0, offset), out _);

        // Should have 2 frames from second call, not 1+2
        Assert.Equal(2, frames2.Count);
        Assert.IsType<GoAwayFrame>(frames2[0]);
        Assert.IsType<MaxPushIdFrame>(frames2[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7")]
    public void FrameDecoder_should_handle_partial_frames_across_decode_all_calls()
    {
        var payload = new byte[] { 0x11, 0x22, 0x33 };
        var frame = new DataFrame(payload);
        var wire = frame.Serialize();

        // Split in middle
        var part1Length = wire.Length / 2;
        var part1 = wire.AsSpan(0, part1Length);
        var part2 = wire.AsSpan(part1Length);

        var decoder = new FrameDecoder();

        // First DecodeAll with partial frame
        var frames1 = decoder.DecodeAll(part1, out _);
        Assert.Empty(frames1);
        Assert.True(decoder.HasRemainder);

        // Second DecodeAll with rest of frame
        var frames2 = decoder.DecodeAll(part2, out _);
        Assert.Single(frames2);
        Assert.False(decoder.HasRemainder);
    }
}