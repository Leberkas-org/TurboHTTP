using TurboHttp.Protocol.Http2;

namespace TurboHttp.Tests.Http2.FlowControl;

/// <summary>
/// Tests stream-level and connection-level WINDOW_UPDATE decoding per RFC 9113 §6.9.
/// Verifies increment values, boundary conditions, and zero-increment error handling.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.9.1: A zero-increment WINDOW_UPDATE on a stream is a stream error (PROTOCOL_ERROR).
/// </remarks>
public sealed class DecoderStreamFlowControlSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_correctly_when_window_update_on_stream_0()
    {
        var frame = new WindowUpdateFrame(0, 32768).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, wu.StreamId);
        Assert.Equal(32768, wu.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_correctly_when_window_update_on_stream_n()
    {
        var frame = new WindowUpdateFrame(3, 8192).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(3, wu.StreamId);
        Assert.Equal(8192, wu.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_window_update_increment_is_zero()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x00, 0x00, // increment = 0 — illegal
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_throw_frame_size_error_when_window_update_payload_size_is_wrong()
    {
        // Payload must be exactly 4 bytes; use 5 bytes.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x05, // length = 5 (must be 4)
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x01, 0x00, 0x00, // 5 payload bytes
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_with_correct_fields_when_data_frame_decoded()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var frame = new DataFrame(1, data, endStream: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(frame);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, df.StreamId);
        Assert.Equal(data, df.Data.ToArray());
        Assert.True(df.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_preserve_increment_exactly_when_window_update_decoded()
    {
        // Test several distinct increment values
        var increments = new[] { 1, 100, 65535, 65536, 0x7FFFFFFE, 0x7FFFFFFF };
        foreach (var increment in increments)
        {
            var bytes = new WindowUpdateFrame(1, increment).Serialize();
            var decoder = new Http2FrameDecoder();
            var frames = decoder.Decode(bytes);

            Assert.Single(frames);
            var wu = Assert.IsType<WindowUpdateFrame>(frames[0]);
            Assert.Equal(increment, wu.Increment);
        }
    }
}
