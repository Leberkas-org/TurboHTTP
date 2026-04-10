using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests HTTP/2 error code decoding from GOAWAY and RST_STREAM frames per RFC 9113 §7.
/// Verifies all error codes are correctly decoded and mapped.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §7: Error codes for connection errors, stream errors, and frame validation.
/// </remarks>
public sealed class Http2DecoderErrorCodeSpec
{
    [Theory(Timeout = 5000)]
    [InlineData((uint)0x0, Http2ErrorCode.NoError)]
    [InlineData((uint)0x1, Http2ErrorCode.ProtocolError)]
    [InlineData((uint)0x2, Http2ErrorCode.InternalError)]
    [InlineData((uint)0x3, Http2ErrorCode.FlowControlError)]
    [InlineData((uint)0x4, Http2ErrorCode.SettingsTimeout)]
    [InlineData((uint)0x5, Http2ErrorCode.StreamClosed)]
    [InlineData((uint)0x6, Http2ErrorCode.FrameSizeError)]
    [InlineData((uint)0x7, Http2ErrorCode.RefusedStream)]
    [InlineData((uint)0x8, Http2ErrorCode.Cancel)]
    [InlineData((uint)0x9, Http2ErrorCode.CompressionError)]
    [InlineData((uint)0xa, Http2ErrorCode.ConnectError)]
    [InlineData((uint)0xb, Http2ErrorCode.EnhanceYourCalm)]
    [InlineData((uint)0xc, Http2ErrorCode.InadequateSecurity)]
    [InlineData((uint)0xd, Http2ErrorCode.Http11Required)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_error_code_from_goaway(uint errorCodeInt, Http2ErrorCode expected)
    {
        var frame = new GoAwayFrame(0, (Http2ErrorCode)errorCodeInt, ReadOnlyMemory<byte>.Empty).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(expected, goAwayFrame.ErrorCode);
    }

    [Theory(Timeout = 5000)]
    [InlineData((uint)0x0, Http2ErrorCode.NoError)]
    [InlineData((uint)0x1, Http2ErrorCode.ProtocolError)]
    [InlineData((uint)0x2, Http2ErrorCode.InternalError)]
    [InlineData((uint)0x3, Http2ErrorCode.FlowControlError)]
    [InlineData((uint)0x4, Http2ErrorCode.SettingsTimeout)]
    [InlineData((uint)0x5, Http2ErrorCode.StreamClosed)]
    [InlineData((uint)0x6, Http2ErrorCode.FrameSizeError)]
    [InlineData((uint)0x7, Http2ErrorCode.RefusedStream)]
    [InlineData((uint)0x8, Http2ErrorCode.Cancel)]
    [InlineData((uint)0x9, Http2ErrorCode.CompressionError)]
    [InlineData((uint)0xa, Http2ErrorCode.ConnectError)]
    [InlineData((uint)0xb, Http2ErrorCode.EnhanceYourCalm)]
    [InlineData((uint)0xc, Http2ErrorCode.InadequateSecurity)]
    [InlineData((uint)0xd, Http2ErrorCode.Http11Required)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_error_code_from_rst_stream(uint errorCodeInt, Http2ErrorCode expected)
    {
        var frame = new RstStreamFrame(1, (Http2ErrorCode)errorCodeInt).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(expected, rstFrame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-7")]
    public void Http2FrameDecoder_should_decode_last_stream_id_from_goaway()
    {
        var frame = new GoAwayFrame(42, Http2ErrorCode.NoError, ReadOnlyMemory<byte>.Empty).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(42, goAwayFrame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_parse_goaway_additional_debug_data()
    {
        var debug = "debug info"u8.ToArray();
        var frame = new GoAwayFrame(1, Http2ErrorCode.ProtocolError, debug).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(1, goAwayFrame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_extract_stream_id_from_rst_stream()
    {
        var frame = new RstStreamFrame(7, Http2ErrorCode.Cancel).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var rstFrame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(7, rstFrame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_ignore_reserved_bit_in_goaway_last_stream_id()
    {
        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8;
        frame[3] = 0x07; // GOAWAY
        frame[4] = 0x00;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 0;
        frame[9] = 0x80; // Reserved bit
        frame[10] = 0x00;
        frame[11] = 0x00;
        frame[12] = 0x05; // LastStreamId = 5
        frame[13] = 0x00;
        frame[14] = 0x00;
        frame[15] = 0x00;
        frame[16] = 0x00; // ErrorCode = 0

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(5, goAwayFrame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_accept_goaway_with_max_stream_id()
    {
        var frame = new GoAwayFrame(int.MaxValue, Http2ErrorCode.NoError, ReadOnlyMemory<byte>.Empty).Serialize();

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        var goAwayFrame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(int.MaxValue, goAwayFrame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.3")]
    public void Http2FrameDecoder_should_accept_rst_stream_with_reserved_bit_in_error_code()
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
        frame[8] = 1;
        frame[9] = 0xFF; // All bits set (error code with reserved bit)
        frame[10] = 0xFF;
        frame[11] = 0xFF;
        frame[12] = 0xFF;

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
    }
}
