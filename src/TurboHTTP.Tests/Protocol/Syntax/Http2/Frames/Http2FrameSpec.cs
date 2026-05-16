using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Frames;

public sealed class Http2FrameSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2Frame_should_serialize_to_correct_format_when_settings_frame_built()
    {
        var frame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.EnablePush, 0u),
        });
        var bytes = frame.Serialize();

        Assert.Equal(9 + 12, bytes.Length);
        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(12, bytes[2]);
        Assert.Equal(4, bytes[3]);
        Assert.Equal(0, bytes[4]);
        Assert.Equal(0, bytes[5]);
        Assert.Equal(0, bytes[6]);
        Assert.Equal(0, bytes[7]);
        Assert.Equal(0, bytes[8]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2Frame_should_serialize_empty_payload_when_settings_ack_built()
    {
        var ack = SettingsFrame.SettingsAck();
        Assert.Equal(9, ack.Length);
        Assert.Equal(0x01, ack[4]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2Frame_should_serialize_8_byte_payload_when_ping_frame_built()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var frame = new PingFrame(data).Serialize();
        Assert.Equal(17, frame.Length);
        Assert.Equal(8, frame[2]);
        Assert.Equal(6, frame[3]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2Frame_should_serialize_correct_increment_when_window_update_frame_built()
    {
        var frame = new WindowUpdateFrame(0, 65535).Serialize();
        Assert.Equal(13, frame.Length);

        Assert.Equal(0x00, frame[9]);
        Assert.Equal(0x00, frame[10]);
        Assert.Equal(0xFF, frame[11]);
        Assert.Equal(0xFF, frame[12]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2Frame_should_serialize_with_end_stream_flag_when_data_frame_built()
    {
        var data = new byte[] { 1, 2, 3 };
        var frame = new DataFrame(1, data, endStream: true).Serialize();
        Assert.Equal(12, frame.Length);
        Assert.Equal(0x1, frame[4]);
        Assert.Equal((byte)FrameType.Data, frame[3]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public void Http2Frame_should_serialize_with_debug_data_when_goaway_frame_built()
    {
        var debug = "test error"u8.ToArray();
        var frame = new GoAwayFrame(3, Http2ErrorCode.ProtocolError, debug).Serialize();
        Assert.Equal(27, frame.Length);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void Http2Frame_should_throw_argument_out_of_range_exception_when_stream_id_is_negative(int negativeStreamId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataFrame(negativeStreamId, ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HeadersFrame(negativeStreamId, ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RstStreamFrame(negativeStreamId, Http2ErrorCode.Cancel));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WindowUpdateFrame(negativeStreamId, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContinuationFrame(negativeStreamId, ReadOnlyMemory<byte>.Empty));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void Http2Frame_should_accept_stream_id_when_stream_id_is_non_negative(int streamId)
    {
        var frame = new DataFrame(streamId, ReadOnlyMemory<byte>.Empty);
        Assert.Equal(streamId, frame.StreamId);
    }
}