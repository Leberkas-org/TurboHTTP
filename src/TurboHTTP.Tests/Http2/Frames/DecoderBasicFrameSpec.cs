using TurboHTTP.Protocol.Http2.Hpack;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests basic HTTP/2 frame decoding for all standard frame types per RFC 9113 §6.
/// Verifies SETTINGS, DATA, HEADERS, WINDOW_UPDATE, RST_STREAM, and GOAWAY frame parsing.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §6: Frame format specification for all frame types.
/// </remarks>
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
}
