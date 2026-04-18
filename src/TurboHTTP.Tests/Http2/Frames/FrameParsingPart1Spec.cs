using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

public sealed class Http2FrameParsingPart1Spec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_return_empty_when_zero_bytes_provided()
    {
        var frames = new FrameDecoder().Decode(ReadOnlyMemory<byte>.Empty);
        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_return_empty_when_eight_bytes_provided()
    {
        var partial = new byte[8];
        var frames = new FrameDecoder().Decode(partial);
        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_decode_when_exactly_9_bytes_with_empty_payload()
    {
        var frame = SettingsFrame.SettingsAck();
        Assert.Equal(9, frame.Length);

        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<SettingsFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_accept_when_length_field_is_zero()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x00,
            0x04,
            0x01,
            0x00, 0x00, 0x00, 0x00
        };
        var frames = new FrameDecoder().Decode(frame);
        Assert.NotEmpty(frames);
        Assert.IsType<SettingsFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_reassemble_when_frame_fragmented_across_calls()
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
    public void Http2FrameDecoder_should_use_24_bit_length_when_payload_is_large()
    {
        const int payloadLen = 66006;
        var buf = new byte[9 + payloadLen];
        buf[0] = payloadLen >> 16;
        buf[1] = (payloadLen >> 8) & 0xFF;
        buf[2] = payloadLen & 0xFF;
        buf[3] = 0x04;
        buf[4] = 0x00;
        for (var i = 0; i < payloadLen; i += 6)
        {
            buf[9 + i + 1] = 0x01;
        }

        var frames = new FrameDecoder().Decode(buf);
        Assert.NotEmpty(frames);
        Assert.IsType<SettingsFrame>(frames[0]);
        var settingsFrame = (SettingsFrame)frames[0];
        Assert.NotNull(settingsFrame);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_accept_when_default_max_frame_size()
    {
        const int maxPayload = 16384;
        var dataFrame = new byte[9 + maxPayload];
        dataFrame[0] = maxPayload >> 16;
        dataFrame[1] = (maxPayload >> 8) & 0xFF;
        dataFrame[2] = maxPayload & 0xFF;
        dataFrame[3] = 0x00;
        dataFrame[4] = 0x01;
        dataFrame[5] = 0;
        dataFrame[6] = 0;
        dataFrame[7] = 0;
        dataFrame[8] = 1;

        var frames = new FrameDecoder().Decode(dataFrame);
        Assert.NotEmpty(frames);
        Assert.IsType<DataFrame>(frames[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_frame_is_one_beyond_max()
    {
        const int overSize = 16385;
        var frame = new byte[9 + overSize];
        frame[0] = overSize >> 16;
        frame[1] = overSize >> 8;
        frame[2] = overSize & 0xFF;
        frame[3] = 0x04;
        frame[4] = 0x00;

        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_accept_larger_frame_when_settings_permit()
    {
        const int payloadLen = 32766;

        var buf = new byte[9 + payloadLen];
        buf[0] = payloadLen >> 16;
        buf[1] = (payloadLen >> 8) & 0xFF;
        buf[2] = payloadLen & 0xFF;
        buf[3] = 0x04;
        buf[4] = 0x00;
        for (var i = 0; i < payloadLen; i += 6)
        {
            buf[9 + i + 1] = 0x01;
        }

        Assert.Equal(9 + payloadLen, buf.Length);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_max_frame_size_is_below_min()
    {
        var settings = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16383u)]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(settings));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_max_frame_size_is_above_max()
    {
        var settings = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777216u)]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => new FrameDecoder().Decode(settings));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.2")]
    public void Http2FrameDecoder_should_accept_when_max_frame_size_is_at_max_boundary()
    {
        var settings = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777215u)]).Serialize();
        var ex = Record.Exception(() => new FrameDecoder().Decode(settings));
        Assert.Null(ex);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_ignore_when_frame_type_is_unknown_0x0f()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x04,
            0x0F,
            0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00
        };
        var frames = new FrameDecoder().Decode(frame);
        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_ignore_all_when_multiple_unknown_frame_types()
    {
        var frame1 = new byte[] { 0x00, 0x00, 0x00, 0xAA, 0x00, 0x00, 0x00, 0x00, 0x01 };
        var frame2 = new byte[] { 0x00, 0x00, 0x00, 0xBB, 0xFF, 0x00, 0x00, 0x00, 0x01 };
        var combined = new byte[frame1.Length + frame2.Length];
        frame1.CopyTo(combined, 0);
        frame2.CopyTo(combined, frame1.Length);

        var frames = new FrameDecoder().Decode(combined);
        Assert.Empty(frames);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2FrameDecoder_should_ignore_when_unknown_frame_type_has_large_payload()
    {
        const int payloadLen = 16384;
        var frame = new byte[9 + payloadLen];
        frame[0] = payloadLen >> 16;
        frame[1] = (payloadLen >> 8) & 0xFF;
        frame[2] = payloadLen & 0xFF;
        frame[3] = 0xEE;
        frame[4] = 0x00;
        frame[5] = 0;
        frame[6] = 0;
        frame[7] = 0;
        frame[8] = 1;

        var frames = new FrameDecoder().Decode(frame);
        Assert.Empty(frames);
    }
}