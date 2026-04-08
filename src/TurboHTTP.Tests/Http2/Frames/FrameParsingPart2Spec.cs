using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests HTTP/2 frame header parsing per RFC 9113 §4.1 — Part 2.
/// Covers stream ID rules, payload size validation, flags, and frame ordering.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §4.1: The frame header is 9 bytes — length(24) + type(8) + flags(8) + stream(31).
/// </remarks>
public sealed class Http2FrameParsingPart2Spec
{
    // Stream ID Rules (RFC 7540 §4.1, §6.5, §6.7, §6.8)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_settings_on_non_zero_stream()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x00,
            0x04,
            0x00,
            0x00, 0x00, 0x00, 0x01
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_ping_on_non_zero_stream()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x08,
            0x06,
            0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_goaway_on_non_zero_stream()
    {
        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8;
        frame[3] = 0x07;
        frame[4] = 0x00;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1;

        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_when_window_update_on_stream_0()
    {
        var frame = new WindowUpdateFrame(0, 1024).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<WindowUpdateFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_when_window_update_on_non_zero_stream()
    {
        var frame = new WindowUpdateFrame(3, 4096).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<WindowUpdateFrame>(frames[0]);
    }

    // Frame-Specific Payload Size Validation (RFC 7540 §6.4/§6.5/§6.7/§6.9)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_settings_payload_not_multiple_of_6()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x07,
            0x04,
            0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_settings_ack_has_non_empty_payload()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x06,
            0x04,
            0x01,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x00, 0x00, 0x10, 0x00
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_ping_has_seven_byte_payload()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x07,
            0x06,
            0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_ping_has_nine_byte_payload()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x09,
            0x06,
            0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_window_update_has_three_byte_payload()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x03,
            0x08,
            0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x01
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_rst_stream_has_three_byte_payload()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x03,
            0x03,
            0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x01
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.4")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_rst_stream_has_five_byte_payload()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x05,
            0x03,
            0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00
        };
        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // Unknown Flags Are Silently Ignored (RFC 7540 §4.1)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_ignore_unknown_flag_bits_when_settings_frame_has_unknown_flags()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x06,
            0x04,
            0xFE,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x03, 0x00, 0x00, 0x00, 0x64
        };
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<SettingsFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_ignore_unknown_flag_bits_when_ping_ack_has_unknown_flags()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x08,
            0x06,
            0xFF,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08
        };
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<PingFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_parse_correctly_when_goaway_has_debug_data()
    {
        var debugData = "shutdown"u8.ToArray();
        var frame = new GoAwayFrame(5, Http2ErrorCode.NoError, debugData).Serialize();

        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(5, goAwayFrame.LastStreamId);
        Assert.Equal(Http2ErrorCode.NoError, goAwayFrame.ErrorCode);
    }

    // Invalid Frame in Stream State (RFC 7540 §5.1)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_standalone_continuation_frame()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x01,
            0x09,
            0x04,
            0x00, 0x00, 0x00, 0x01,
            0x88
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-5.1")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_headers_without_end_headers_followed_by_ping()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var headersFrame = new HeadersFrame(1, headerBlock, endStream: false, endHeaders: false).Serialize();
        var pingFrame = new PingFrame(new byte[8], isAck: false).Serialize();

        var combined = new byte[headersFrame.Length + pingFrame.Length];
        headersFrame.CopyTo(combined, 0);
        pingFrame.CopyTo(combined, headersFrame.Length);

        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}
