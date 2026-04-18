using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.Frames;

public sealed class Http2DecoderBasicFrameSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_decode_settings_frame()
    {
        var settings = new SettingsFrame([(SettingsParameter.HeaderTableSize, 4096u)]);
        var frame = settings.Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        Assert.IsType<SettingsFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_decode_data_frame()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var frame = new DataFrame(1, data).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        Assert.IsType<DataFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_decode_headers_frame()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":method", "GET")]);
        var frame = new HeadersFrame(1, headerBlock).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        Assert.IsType<HeadersFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_window_update_frame()
    {
        var frame = new WindowUpdateFrame(1, 65535).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        var wuFrame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(65535, wuFrame.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_decode_rst_stream_frame()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        Assert.IsType<RstStreamFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_goaway_frame()
    {
        var frame = new GoAwayFrame(1, Http2ErrorCode.NoError, ReadOnlyMemory<byte>.Empty).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        Assert.IsType<GoAwayFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_reassemble_when_frame_received_in_two_chunks()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        var chunk1 = ping[..5];
        var chunk2 = ping[5..];

        var combined = new byte[chunk1.Length + chunk2.Length];
        chunk1.CopyTo(combined, 0);
        chunk2.CopyTo(combined, chunk1.Length);

        var frames = new FrameDecoder().Decode(combined);
        Assert.Single(frames);
        Assert.IsType<PingFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_decode_multiple_frames_in_single_segment()
    {
        var settingsFrame = new SettingsFrame([]);
        var settingsBytes = settingsFrame.Serialize();
        var pingBytes = new PingFrame(new byte[8], isAck: false).Serialize();

        var combined = new byte[settingsBytes.Length + pingBytes.Length];
        settingsBytes.CopyTo(combined, 0);
        pingBytes.CopyTo(combined, settingsBytes.Length);

        var frames = new FrameDecoder().Decode(combined);
        Assert.Equal(2, frames.Count);
        Assert.IsType<SettingsFrame>(frames[0]);
        Assert.IsType<PingFrame>(frames[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_decode_data_frame_with_end_stream_flag()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = new DataFrame(1, data, endStream: true).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        var dataFrame = Assert.IsType<DataFrame>(frames[0]);
        Assert.True(dataFrame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_decode_headers_frame_with_end_headers_flag()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(1, headerBlock, endHeaders: true).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(headersFrame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_decode_settings_ack_frame()
    {
        var ack = SettingsFrame.SettingsAck();

        var frames = new FrameDecoder().Decode(ack);
        Assert.Single(frames);
        var settingsFrame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(settingsFrame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_reject_data_frame_on_stream_zero()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = new DataFrame(0, data).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frame));
        Assert.Contains("RFC 9113 §6.1", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_reject_data_frame_padded_with_empty_payload()
    {
        // Manually construct a DATA frame with PADDED flag (0x08) but empty payload
        var frameBytes = new byte[9];
        frameBytes[0] = 0;     // length high byte
        frameBytes[1] = 0;     // length mid byte
        frameBytes[2] = 0;     // length low byte (0 bytes payload)
        frameBytes[3] = (byte)FrameType.Data;
        frameBytes[4] = 0x08;  // PADDED flag
        frameBytes[5] = 0;     // stream ID bytes (big-endian, stream 1)
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("payload is empty", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.1")]
    public void Http2FrameDecoder_should_reject_data_frame_padded_with_pad_length_exceeding_payload()
    {
        // DATA frame with PADDED flag and pad_length > payload size
        var frameBytes = new byte[10];
        frameBytes[0] = 0;     // length high byte
        frameBytes[1] = 0;     // length mid byte
        frameBytes[2] = 1;     // length low byte (1 byte payload)
        frameBytes[3] = (byte)FrameType.Data;
        frameBytes[4] = 0x08;  // PADDED flag
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;
        frameBytes[9] = 255;   // pad_length = 255, exceeds 1-byte payload

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("pad_length exceeds payload size", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_reject_headers_frame_padded_with_empty_payload()
    {
        // Manually construct a HEADERS frame with PADDED flag (0x08) but empty payload
        var frameBytes = new byte[9];
        frameBytes[0] = 0;     // length high byte
        frameBytes[1] = 0;     // length mid byte
        frameBytes[2] = 0;     // length low byte (0 bytes payload)
        frameBytes[3] = (byte)FrameType.Headers;
        frameBytes[4] = 0x08;  // PADDED flag
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("payload is empty", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.2")]
    public void Http2FrameDecoder_should_reject_headers_frame_padded_with_pad_length_exceeding_payload()
    {
        // HEADERS frame with PADDED flag and pad_length > payload size
        var frameBytes = new byte[10];
        frameBytes[0] = 0;     // length high byte
        frameBytes[1] = 0;     // length mid byte
        frameBytes[2] = 1;     // length low byte (1 byte payload)
        frameBytes[3] = (byte)FrameType.Headers;
        frameBytes[4] = 0x08;  // PADDED flag
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;
        frameBytes[9] = 255;   // pad_length = 255, exceeds 1-byte payload

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("pad_length exceeds payload size", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_reject_rst_stream_with_wrong_payload_size()
    {
        // RST_STREAM must be exactly 4 bytes
        var frameBytes = new byte[13];  // 9-byte header + 4 bytes payload (wrong)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 3;     // 3 bytes payload (should be 4)
        frameBytes[3] = (byte)FrameType.RstStream;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;
        frameBytes[9] = 0;
        frameBytes[10] = 0;
        frameBytes[11] = 0;
        frameBytes[12] = (byte)Http2ErrorCode.NoError;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("RST_STREAM frame must be exactly 4 bytes", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_reject_ping_frame_with_wrong_payload_size()
    {
        // PING must be exactly 8 bytes
        var frameBytes = new byte[16];  // 9-byte header + 7 bytes (wrong)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 7;     // 7 bytes payload (should be 8)
        frameBytes[3] = (byte)FrameType.Ping;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 0;
        // 7 bytes of payload follow

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("PING frame must be exactly 8 bytes", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_reject_ping_frame_on_non_zero_stream()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();
        // Modify stream ID to 1 (ping must be on stream 0)
        ping[5] = 0;
        ping[6] = 0;
        ping[7] = 0;
        ping[8] = 1;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(ping));
        Assert.Contains("RFC 9113 §6.7", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_reject_settings_frame_on_non_zero_stream()
    {
        var settings = new SettingsFrame([(SettingsParameter.HeaderTableSize, 4096u)]);
        var frame = settings.Serialize();
        // Modify stream ID to 1 (settings must be on stream 0)
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frame));
        Assert.Contains("RFC 9113 §6.5", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_reject_settings_ack_with_payload()
    {
        // SETTINGS ACK with payload is invalid
        var frameBytes = new byte[18];  // 9-byte header + 9 bytes payload (non-zero)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 9;
        frameBytes[3] = (byte)FrameType.Settings;
        frameBytes[4] = 0x01;  // ACK flag
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 0;
        // 9 bytes of payload follow (should be empty)
        for (var i = 9; i < 18; i++)
        {
            frameBytes[i] = 0xFF;
        }

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("SETTINGS frame with ACK flag MUST have empty payload", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_reject_settings_payload_not_multiple_of_six()
    {
        // SETTINGS payload must be multiple of 6
        var frameBytes = new byte[16];  // 9-byte header + 7 bytes payload (wrong, should be 6 or 12 or...)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 7;     // 7 bytes (not a multiple of 6)
        frameBytes[3] = (byte)FrameType.Settings;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 0;
        // 7 bytes of payload follow
        for (var i = 9; i < 16; i++)
        {
            frameBytes[i] = 0;
        }

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("not a multiple of 6", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http2FrameDecoder_should_reject_settings_max_frame_size_out_of_range()
    {
        // SETTINGS_MAX_FRAME_SIZE (parameter 5) with value < 2^14 (16384)
        var frameBytes = new byte[15];  // 9-byte header + 6 bytes settings entry
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 6;
        frameBytes[3] = (byte)FrameType.Settings;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 0;
        // SETTINGS entry: parameter 5 (MAX_FRAME_SIZE), value 1000 (too small)
        frameBytes[9] = 0;
        frameBytes[10] = 5;
        frameBytes[11] = 0;
        frameBytes[12] = 0;
        frameBytes[13] = 0x03;
        frameBytes[14] = 0xE8;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("SETTINGS_MAX_FRAME_SIZE", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_reject_goaway_frame_on_non_zero_stream()
    {
        var goaway = new GoAwayFrame(1, Http2ErrorCode.NoError, ReadOnlyMemory<byte>.Empty).Serialize();
        // Modify stream ID to 1 (goaway must be on stream 0)
        goaway[5] = 0;
        goaway[6] = 0;
        goaway[7] = 0;
        goaway[8] = 1;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(goaway));
        Assert.Contains("RFC 9113 §6.8", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_reject_goaway_with_insufficient_payload()
    {
        // GOAWAY must have at least 8 bytes (last-stream-id + error-code)
        var frameBytes = new byte[16];  // 9-byte header + 7 bytes (too short)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 7;
        frameBytes[3] = (byte)FrameType.GoAway;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 0;
        // 7 bytes of payload follow

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("GOAWAY payload must be at least 8 bytes", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_goaway_with_debug_data()
    {
        var debugData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var frame = new GoAwayFrame(1, Http2ErrorCode.NoError, debugData).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.Single(frames);
        var goawayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(1, goawayFrame.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, goawayFrame.ErrorCode);
        Assert.Equal(debugData, goawayFrame.DebugData.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_reject_continuation_frame_without_preceding_headers()
    {
        var continuation = new ContinuationFrame(1, ReadOnlyMemory<byte>.Empty, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(continuation));
        Assert.Contains("RFC 9113 §6.10", ex.Message);
        Assert.Contains("without preceding HEADERS or PUSH_PROMISE", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_reject_continuation_frame_on_stream_zero()
    {
        var continuation = new ContinuationFrame(0, ReadOnlyMemory<byte>.Empty, endHeaders: true).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(continuation));
        Assert.Contains("RFC 9113 §6.10", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_enforce_continuation_stream_matching()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":method", "GET")]);
        // Create HEADERS on stream 1 without END_HEADERS (expects continuation)
        var headers = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        // Create CONTINUATION on stream 2 (wrong stream)
        var continuation = new ContinuationFrame(2, ReadOnlyMemory<byte>.Empty, endHeaders: true).Serialize();

        var combined = new byte[headers.Length + continuation.Length];
        headers.CopyTo(combined, 0);
        continuation.CopyTo(combined, headers.Length);

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(combined));
        Assert.Contains("Expected CONTINUATION on stream 1", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.10")]
    public void Http2FrameDecoder_should_reject_non_continuation_when_awaiting_continuation()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":method", "GET")]);
        // Create HEADERS on stream 1 without END_HEADERS (expects continuation)
        var headers = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        // Create a PING frame (wrong frame type when expecting continuation)
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        var combined = new byte[headers.Length + ping.Length];
        headers.CopyTo(combined, 0);
        ping.CopyTo(combined, headers.Length);

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(combined));
        Assert.Contains("Expected CONTINUATION frame on stream 1", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.6")]
    public void Http2FrameDecoder_should_reject_push_promise_with_insufficient_payload()
    {
        // PUSH_PROMISE must have at least 4 bytes (promised stream ID)
        var frameBytes = new byte[12];  // 9-byte header + 3 bytes (too short)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 3;
        frameBytes[3] = (byte)FrameType.PushPromise;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;
        // 3 bytes of payload follow

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("PUSH_PROMISE payload must be at least 4 bytes", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_reject_window_update_with_zero_increment()
    {
        // WINDOW_UPDATE with increment = 0 is invalid
        var frameBytes = new byte[13];  // 9-byte header + 4 bytes
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 4;
        frameBytes[3] = (byte)FrameType.WindowUpdate;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 0;
        // increment = 0 (all zeros)
        frameBytes[9] = 0;
        frameBytes[10] = 0;
        frameBytes[11] = 0;
        frameBytes[12] = 0;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("WINDOW_UPDATE increment of 0", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_reject_window_update_with_wrong_payload_size()
    {
        // WINDOW_UPDATE must be exactly 4 bytes
        var frameBytes = new byte[12];  // 9-byte header + 3 bytes (wrong)
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 3;
        frameBytes[3] = (byte)FrameType.WindowUpdate;
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;
        // 3 bytes of payload

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frameBytes));
        Assert.Contains("WINDOW_UPDATE payload must be exactly 4 bytes", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.5")]
    public void Http2FrameDecoder_should_ignore_unknown_frame_types()
    {
        // Frame type 255 (unknown)
        var frameBytes = new byte[9];
        frameBytes[0] = 0;
        frameBytes[1] = 0;
        frameBytes[2] = 0;
        frameBytes[3] = 255;   // Unknown frame type
        frameBytes[4] = 0;
        frameBytes[5] = 0;
        frameBytes[6] = 0;
        frameBytes[7] = 0;
        frameBytes[8] = 1;

        var frames = new FrameDecoder().Decode(frameBytes);
        Assert.Empty(frames);  // Unknown frames are silently ignored
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_handle_reset_and_restart_decoding()
    {
        var decoder = new FrameDecoder();
        var ping1 = new PingFrame(new byte[8], isAck: false).Serialize();

        var frames1 = decoder.Decode(ping1);
        Assert.Single(frames1);

        decoder.Reset();

        var ping2 = new PingFrame(new byte[8], isAck: true).Serialize();
        var frames2 = decoder.Decode(ping2);
        Assert.Single(frames2);
        var pingFrame = Assert.IsType<PingFrame>(frames2[0]);
        Assert.True(pingFrame.IsAck);
    }
}
