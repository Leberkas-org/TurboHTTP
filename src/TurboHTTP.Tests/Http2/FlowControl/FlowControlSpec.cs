using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.FlowControl;

/// <summary>
/// Tests HTTP/2 flow control and WINDOW_UPDATE frame decoding per RFC 9113 §6.9.
/// Covers both connection-level (stream 0) and stream-level window update semantics.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http2FrameDecoder"/>.
/// RFC 9113 §6.9: WINDOW_UPDATE frames carry a 31-bit increment and apply to stream 0 (connection) or a specific stream.
/// </remarks>
public sealed class FlowControlSpec
{
    // FC-WU-001..006: WINDOW_UPDATE Decoding — Connection Level (Stream 0)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_have_correct_stream_id_when_window_update_on_stream_0()
    {
        var bytes = new WindowUpdateFrame(0, 1000).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, frame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_have_correct_increment_when_window_update_on_stream_0()
    {
        var bytes = new WindowUpdateFrame(0, 32768).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(32768, frame.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_have_correct_frame_type_when_window_update_on_stream_0()
    {
        var bytes = new WindowUpdateFrame(0, 1).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(FrameType.WindowUpdate, frames[0].Type);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_as_independent_frames_when_multiple_window_updates_on_stream_0()
    {
        var wu1 = new WindowUpdateFrame(0, 1000).Serialize();
        var wu2 = new WindowUpdateFrame(0, 500).Serialize();
        var combined = wu1.Concat(wu2).ToArray();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(2, frames.Count);
        var frame1 = Assert.IsType<WindowUpdateFrame>(frames[0]);
        var frame2 = Assert.IsType<WindowUpdateFrame>(frames[1]);
        Assert.Equal(0, frame1.StreamId);
        Assert.Equal(1000, frame1.Increment);
        Assert.Equal(0, frame2.StreamId);
        Assert.Equal(500, frame2.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_when_window_update_on_stream_0_with_increment_one()
    {
        var bytes = new WindowUpdateFrame(0, 1).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, frame.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_when_window_update_on_stream_0_with_max_increment()
    {
        var bytes = new WindowUpdateFrame(0, 0x7FFFFFFF).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0x7FFFFFFF, frame.Increment);
    }

    // FC-WU-007..012: WINDOW_UPDATE Decoding — Stream Level (Stream N)

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_have_correct_stream_id_when_window_update_on_stream_1()
    {
        var bytes = new WindowUpdateFrame(1, 2000).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_have_correct_increment_when_window_update_on_stream_3()
    {
        var bytes = new WindowUpdateFrame(3, 65535).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(3, frame.StreamId);
        Assert.Equal(65535, frame.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_independently_when_mixed_window_updates_on_stream_0_and_stream_n()
    {
        var wu0 = new WindowUpdateFrame(0, 100).Serialize();
        var wu1 = new WindowUpdateFrame(1, 200).Serialize();
        var wu3 = new WindowUpdateFrame(3, 300).Serialize();
        var combined = wu0.Concat(wu1).Concat(wu3).ToArray();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(3, frames.Count);
        var f0 = Assert.IsType<WindowUpdateFrame>(frames[0]);
        var f1 = Assert.IsType<WindowUpdateFrame>(frames[1]);
        var f3 = Assert.IsType<WindowUpdateFrame>(frames[2]);
        Assert.Equal(0, f0.StreamId);
        Assert.Equal(100, f0.Increment);
        Assert.Equal(1, f1.StreamId);
        Assert.Equal(200, f1.Increment);
        Assert.Equal(3, f3.StreamId);
        Assert.Equal(300, f3.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_correctly_when_window_update_has_large_stream_id()
    {
        var bytes = new WindowUpdateFrame(0x7FFFFFFE, 1024).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0x7FFFFFFE, frame.StreamId);
        Assert.Equal(1024, frame.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_when_window_update_on_stream_n_with_increment_one()
    {
        var bytes = new WindowUpdateFrame(5, 1).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(5, frame.StreamId);
        Assert.Equal(1, frame.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_accept_when_window_update_on_stream_n_with_max_increment()
    {
        var bytes = new WindowUpdateFrame(7, 0x7FFFFFFF).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(7, frame.StreamId);
        Assert.Equal(0x7FFFFFFF, frame.Increment);
    }

    // FC-WU-013..016: Reserved bit handling and increment values

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_strip_reserved_high_bit_when_window_update_decoded()
    {
        // Build raw WINDOW_UPDATE with high bit set: 0x80000001 → increment should be 1
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x80, 0x00, 0x00, 0x01, // increment with high bit set → stripped to 1
        };
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(rawFrame);

        Assert.Single(frames);
        var frame = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(1, frame.Increment); // high bit stripped
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_preserve_fields_when_window_update_round_trip_on_stream_0()
    {
        var original = new WindowUpdateFrame(0, 131072);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(0, decoded.StreamId);
        Assert.Equal(131072, decoded.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_preserve_fields_when_window_update_round_trip_on_stream_n()
    {
        var original = new WindowUpdateFrame(9, 4096);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.Equal(9, decoded.StreamId);
        Assert.Equal(4096, decoded.Increment);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_across_two_calls_when_window_update_tcp_fragmented()
    {
        var bytes = new WindowUpdateFrame(0, 8192).Serialize(); // 13 bytes total
        var part1 = bytes[..7];
        var part2 = bytes[7..];

        var decoder = new Http2FrameDecoder();
        var frames1 = decoder.Decode(part1);
        var frames2 = decoder.Decode(part2);

        Assert.Empty(frames1); // incomplete
        Assert.Single(frames2);
        var frame = Assert.IsType<WindowUpdateFrame>(frames2[0]);
        Assert.Equal(0, frame.StreamId);
        Assert.Equal(8192, frame.Increment);
    }

    // FC-WU-017..019: Error cases — PROTOCOL_ERROR and FRAME_SIZE_ERROR

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_be_protocol_error_when_window_update_has_zero_increment_on_stream_0()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x00, 0x00, // increment = 0 — MUST be > 0
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_be_protocol_error_when_window_update_has_zero_increment_on_stream_n()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x01, // stream = 1
            0x00, 0x00, 0x00, 0x00, // increment = 0 — MUST be > 0
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_be_frame_size_error_when_window_update_has_wrong_payload_size()
    {
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x03, // length = 3 (must be 4)
            0x08,             // WINDOW_UPDATE
            0x00,             // flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x00, 0x01, // only 3 payload bytes
        };
        var decoder = new Http2FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rawFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // FC-DF-001..007: DATA Frame Decoding

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_with_correct_stream_id_and_data_when_data_frame_received()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var bytes = new DataFrame(1, data).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.Equal(data, frame.Data.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_as_end_stream_when_data_frame_has_end_stream()
    {
        var data = new byte[10];
        var bytes = new DataFrame(3, data, endStream: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.True(frame.EndStream);
        Assert.Equal(3, frame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_as_not_end_stream_when_data_frame_lacks_end_stream()
    {
        var data = new byte[10];
        var bytes = new DataFrame(5, data, endStream: false).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.False(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_correctly_when_data_frame_is_zero_length()
    {
        var bytes = new DataFrame(1, ReadOnlyMemory<byte>.Empty, endStream: true).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(0, frame.Data.Length);
        Assert.True(frame.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_preserve_all_fields_when_data_frame_round_trip()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var original = new DataFrame(7, data, endStream: true);
        var bytes = original.Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(7, decoded.StreamId);
        Assert.Equal(data, decoded.Data.ToArray());
        Assert.True(decoded.EndStream);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_in_order_when_window_update_followed_by_data_frame()
    {
        var wu = new WindowUpdateFrame(1, 65535).Serialize();
        var df = new DataFrame(1, new byte[] { 0x42 }, endStream: true).Serialize();
        var combined = wu.Concat(df).ToArray();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(combined);

        Assert.Equal(2, frames.Count);
        Assert.IsType<WindowUpdateFrame>(frames[0]);
        Assert.IsType<DataFrame>(frames[1]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2FrameDecoder_should_decode_correctly_when_data_frame_has_large_payload()
    {
        var data = new byte[16384]; // 16 KB
        for (var i = 0; i < data.Length; i++) { data[i] = (byte)(i & 0xFF); }

        var bytes = new DataFrame(1, data).Serialize();
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(16384, frame.Data.Length);
        Assert.Equal(data, frame.Data.ToArray());
    }
}
