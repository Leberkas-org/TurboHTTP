using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

public sealed class Http2ErrorHandlingSpec
{
    [Theory(Timeout = 5000)]
    [InlineData(Http2ErrorCode.NoError)]
    [InlineData(Http2ErrorCode.ProtocolError)]
    [InlineData(Http2ErrorCode.InternalError)]
    [InlineData(Http2ErrorCode.FlowControlError)]
    [InlineData(Http2ErrorCode.SettingsTimeout)]
    [InlineData(Http2ErrorCode.StreamClosed)]
    [Trait("RFC", "RFC9113-6.3")]
    internal void Http2FrameDecoder_should_decode_rst_stream_error_code(Http2ErrorCode expectedCode)
    {
        var frame = new RstStreamFrame(1, expectedCode).Serialize();
        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(expectedCode, rstFrame.ErrorCode);
    }

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
        frame[8] = 1; // stream ID = 1
        frame[9] = 0;
        frame[10] = 0;
        frame[11] = 0;
        frame[12] = 1; // ErrorCode = ProtocolError

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ProtocolError, rstFrame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_accept_rst_stream_on_any_stream()
    {
        var frame = new RstStreamFrame(5, Http2ErrorCode.Cancel).Serialize();
        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(5, rstFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_decode_ping_frame_with_ack_flag_false()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.False(pingFrame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_decode_ping_frame_with_ack_flag_true()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: true).Serialize();
        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var pingFrame = Assert.IsType<PingFrame>(frames[0]);
        Assert.True(pingFrame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.7")]
    public void Http2FrameDecoder_should_preserve_ping_data_unchanged()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data, isAck: false).Serialize();
        var frames = new FrameDecoder().Decode(frame);
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

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frame));
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
        frame[2] = 3; // wrong: should be 4
        frame[3] = 0x03; // RST_STREAM
        frame[4] = 0x00;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1; // stream ID = 1
        frame[9] = 0;
        frame[10] = 0;
        frame[11] = 0;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_accept_rst_stream_with_no_error()
    {
        var frame = new RstStreamFrame(1, Http2ErrorCode.NoError).Serialize();
        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.NoError, rstFrame.ErrorCode);
    }
}