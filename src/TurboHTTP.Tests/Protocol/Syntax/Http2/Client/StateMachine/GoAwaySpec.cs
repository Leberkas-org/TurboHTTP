using System.Buffers.Binary;
using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.StateMachine;

public sealed class Http2GoAwaySpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_with_correct_last_stream_id()
    {
        var bytes = new GoAwayFrame(7, Http2ErrorCode.NoError).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(7, frame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_with_correct_error_code()
    {
        var bytes = new GoAwayFrame(3, Http2ErrorCode.ProtocolError).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(Http2ErrorCode.ProtocolError, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_have_correct_frame_type()
    {
        var bytes = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Equal(FrameType.GoAway, frames[0].Type);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_have_zero_stream_id()
    {
        var bytes = new GoAwayFrame(5, Http2ErrorCode.NoError).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Equal(0, frames[0].StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_have_empty_debug_data_when_go_away_has_no_debug_data()
    {
        var bytes = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.True(frame.DebugData.IsEmpty);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_correctly_when_go_away_has_debug_data()
    {
        var debugData = "graceful shutdown"u8.ToArray();
        var bytes = new GoAwayFrame(3, Http2ErrorCode.NoError, debugData).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.True(frame.DebugData.Span.SequenceEqual(debugData));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_decode_correctly_when_go_away_last_stream_id_is_zero()
    {
        var bytes = new GoAwayFrame(0, Http2ErrorCode.NoError).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(0, frame.LastStreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_be_protocol_error_when_go_away_on_non_zero_stream()
    {
        // Craft a GOAWAY frame with stream ID = 1 (violates RFC 9113 §6.8).
        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8; // length = 8
        frame[3] = 0x7; // type = GOAWAY
        frame[4] = 0x0; // no flags
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), 1u); // stream 1
        // lastStreamId=0, errorCode=0

        var decoder = new FrameDecoder();
        Assert.Throws<HttpProtocolException>(() => decoder.Decode(frame));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_preserve_all_fields_when_go_away_round_trip()
    {
        var debugData = new byte[] { 0xAB, 0xCD };
        var original = new GoAwayFrame(9, Http2ErrorCode.InternalError, debugData);
        var bytes = original.Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var decoded = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(original.LastStreamId, decoded.LastStreamId);
        Assert.Equal(original.ErrorCode, decoded.ErrorCode);
        Assert.True(decoded.DebugData.Span.SequenceEqual(original.DebugData.Span));
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    [InlineData(Http2ErrorCode.NoError)]
    [InlineData(Http2ErrorCode.ProtocolError)]
    [InlineData(Http2ErrorCode.InternalError)]
    [InlineData(Http2ErrorCode.Cancel)]
    [InlineData(Http2ErrorCode.FlowControlError)]
    [InlineData(Http2ErrorCode.CompressionError)]
    internal void Http2FrameDecoder_should_decode_correctly_when_go_away_has_various_error_codes(
        Http2ErrorCode errorCode)
    {
        var bytes = new GoAwayFrame(1, errorCode).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var frame = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(errorCode, frame.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.8")]
    public void Http2FrameDecoder_should_strip_when_go_away_last_stream_id_has_reserved_high_bit()
    {
        // Craft a GOAWAY frame with the reserved high bit set in lastStreamId.
        var frame = new byte[9 + 8];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = 8; // length = 8
        frame[3] = 0x7; // type = GOAWAY
        frame[4] = 0x0; // no flags
        // stream id = 0
        // lastStreamId with high bit set = 0x80000005 → should decode as 5
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(9), 0x80000005u);
        // errorCode = 0

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(frame);

        var decoded = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(5, decoded.LastStreamId);
    }
}