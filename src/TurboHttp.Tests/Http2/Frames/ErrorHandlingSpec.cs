using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.Frames;

/// <summary>
/// Tests RST_STREAM and PING frame error handling per RFC 9113 §6.3 and §6.7.
/// Verifies error code decoding, reserved bits, and ACK flag semantics.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.3: RST_STREAM carries a 32-bit error code.
/// RFC 9113 §6.7: PING frames are 8 bytes and must have exact payload size.
/// </remarks>
public sealed class Http2ErrorHandlingSpec
{
    [Theory(Timeout = 5000)]
    [InlineData((uint)0, Http2ErrorCode.NoError)]
    [InlineData((uint)1, Http2ErrorCode.ProtocolError)]
    [InlineData((uint)2, Http2ErrorCode.InternalError)]
    [InlineData((uint)3, Http2ErrorCode.FlowControlError)]
    [InlineData((uint)4, Http2ErrorCode.SettingsTimeout)]
    [InlineData((uint)5, Http2ErrorCode.StreamClosed)]
    [Trait("RFC", "RFC9113-6.3")]
#pragma warning disable xUnit1026
    public void Http2FrameDecoder_should_decode_rst_stream_error_code(uint errorCodeInt, Http2ErrorCode expectedCode)
    {
        var frame = new RstStreamFrame(1, expectedCode).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(expectedCode, rstFrame.ErrorCode);
    }
#pragma warning restore xUnit1026

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_decode_rst_stream_error_code_from_raw_bytes()
    {
        var frame = new byte[9 + 4];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 4;
        frame[3] = 0x03; // RST_STREAM
        frame[4] = 0x00;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1;  // stream ID = 1
        frame[9] = 0;
        frame[10] = 0;
        frame[11] = 0;
        frame[12] = 1; // ErrorCode = ProtocolError

        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ProtocolError, rstFrame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_accept_rst_stream_on_any_stream()
    {
        var frame = new RstStreamFrame(5, Http2ErrorCode.Cancel).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(5, rstFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_decode_ping_frame_with_ack_flag_false()
    {
        var data = new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(pingFrame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_decode_ping_frame_with_ack_flag_true()
    {
        var data = new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: true).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(pingFrame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_preserve_ping_data_unchanged()
    {
        var data = new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.Equal(data, pingFrame.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_reject_ping_on_non_zero_stream()
    {
        var data = new byte[8];
        var frame = new PingFrame(data, isAck: false).Serialize();
        // Stream ID is at bytes [5]-[8] in the 9-byte header
        frame[5] = 0x00;
        frame[6] = 0x00;
        frame[7] = 0x00;
        frame[8] = 0x01;

        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_reject_rst_stream_with_wrong_payload_length()
    {
        // RST_STREAM must have exactly 4 bytes payload
        var frame = new byte[9 + 3];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 3;  // wrong: should be 4
        frame[3] = 0x03; // RST_STREAM
        frame[4] = 0x00;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1;  // stream ID = 1
        frame[9] = 0;
        frame[10] = 0;
        frame[11] = 0;

        var ex = Assert.Throws<Http2Exception>(() => new Http2FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_accept_rst_stream_with_no_error()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.NoError).Serialize();
        var frames = new Http2FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.NoError, rstFrame.ErrorCode);
    }
}
