using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.FrameDecoding;

public sealed class ContinuationFramePart1Spec
{
    private static byte[] EncodeBlock(params (string Name, string Value)[] headers)
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode(headers).ToArray();
    }

    private static byte[] ConcatArrays(params byte[][] arrays)
    {
        var total = arrays.Sum(a => a.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var a in arrays)
        {
            a.CopyTo(result, offset);
            offset += a.Length;
        }

        return result;
    }

    private static byte[] AssembleHeaderBlock(IReadOnlyList<Http2Frame> frames)
    {
        var buffer = new List<byte>();
        int? pendingStreamId = null;
        var continuationCount = 0;

        foreach (var frame in frames)
        {
            if (pendingStreamId.HasValue && frame is not ContinuationFrame)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.10: Expected CONTINUATION but received {frame.GetType().Name}.");
            }

            switch (frame)
            {
                case HeadersFrame h:
                    buffer.AddRange(h.HeaderBlockFragment.ToArray());
                    if (!h.EndHeaders)
                    {
                        pendingStreamId = h.StreamId;
                        continuationCount = 0;
                    }

                    break;

                case ContinuationFrame c:
                    if (!pendingStreamId.HasValue)
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §6.10: Unexpected CONTINUATION on stream {c.StreamId}; no pending header block.");
                    }

                    if (c.StreamId != pendingStreamId.Value)
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §6.10: CONTINUATION on stream {c.StreamId}; expected stream {pendingStreamId.Value}.");
                    }

                    continuationCount++;
                    if (continuationCount >= 1000)
                    {
                        throw new Http2Exception(
                            "RFC 9113 §6.10: Excessive CONTINUATION frames — possible flood attack.");
                    }

                    buffer.AddRange(c.HeaderBlockFragment.ToArray());
                    if (c.EndHeaders)
                    {
                        pendingStreamId = null;
                    }

                    break;
            }
        }

        return buffer.ToArray();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void HeadersFrame_should_have_end_headers_true_when_headers_has_end_headers_set()
    {
        var block = EncodeBlock((":status", "200"));
        var bytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(frame.EndHeaders);
        Assert.True(frame.EndStream);

        // Full block is just the HEADERS fragment — no CONTINUATION needed.
        var fullBlock = AssembleHeaderBlock(frames);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void HeadersFrame_should_have_end_headers_false_when_headers_lacks_end_headers()
    {
        var block = EncodeBlock((":status", "200"));
        var partial = block[..1];
        var bytes = new HeadersFrame(1, partial.AsMemory(), endStream: true, endHeaders: false).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(frame.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_decode_correctly_when_continuation_has_end_headers()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes =
            new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var frames = ConcatArrays(headersBytes, contBytes);
        var decoded = decoder.Decode(frames);

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.False(hf.EndHeaders);
        Assert.True(cf.EndHeaders);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void ContinuationFrame_should_have_end_headers_false_when_continuation_lacks_end_headers()
    {
        var block = EncodeBlock((":status", "200"));
        var third = Math.Max(1, block.Length / 3);
        var headersBytes =
            new HeadersFrame(1, block.AsMemory()[..third], endStream: true, endHeaders: false).Serialize();
        var cont1Bytes =
            new ContinuationFrame(1, block.AsMemory()[third..Math.Min(2 * third, block.Length)], endHeaders: false)
                .Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, cont1Bytes));

        Assert.Equal(2, decoded.Count);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.False(cf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_assemble_block_when_three_continuation_frames_complete()
    {
        var block = EncodeBlock((":status", "200"), ("x-a", "1"), ("x-b", "2"), ("x-c", "3"));
        var quarter = block.Length / 4;

        var h = new HeadersFrame(1, block.AsMemory()[..quarter], endStream: true, endHeaders: false).Serialize();
        var c1 = new ContinuationFrame(1, block.AsMemory()[quarter..(2 * quarter)], endHeaders: false).Serialize();
        var c2 = new ContinuationFrame(1, block.AsMemory()[(2 * quarter)..(3 * quarter)], endHeaders: false)
            .Serialize();
        var c3 = new ContinuationFrame(1, block.AsMemory()[(3 * quarter)..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(h, c1, c2, c3));
        Assert.Equal(4, decoded.Count);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h2 => h2 is { Name: ":status", Value: "200" });
        Assert.Contains(headers, h2 => h2 is { Name: "x-a", Value: "1" });
        Assert.Contains(headers, h2 => h2 is { Name: "x-c", Value: "3" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_preserve_header_values_when_split_across_continuation_frames()
    {
        var block = EncodeBlock((":status", "201"), ("content-type", "application/json"), ("x-custom", "hello"));
        var half = block.Length / 2;

        var headersBytes =
            new HeadersFrame(1, block.AsMemory()[..half], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[half..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));
        Assert.Equal(2, decoded.Count);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h is { Name: ":status", Value: "201" });
        Assert.Contains(headers, h => h is { Name: "x-custom", Value: "hello" });
        Assert.Contains(headers, h => h is { Name: "content-type", Value: "application/json" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_data_frame_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        // A raw DATA frame on stream 1.
        var dataFrame = new byte[]
        {
            0x00, 0x00, 0x03, // length = 3
            0x00, 0x01, // type=DATA, flag=END_STREAM
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x61, 0x62, 0x63 // "abc"
        };

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(dataFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_ping_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var pingBytes = new PingFrame(new byte[8]).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(pingBytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_settings_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var settingsBytes = new SettingsFrame([]).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(settingsBytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_rst_stream_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var rstBytes = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(rstBytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_window_update_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var windowUpdateBytes = new WindowUpdateFrame(0, 65535).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(windowUpdateBytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_goaway_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var goAwayBytes = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(goAwayBytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_headers_for_other_stream_interleaves_continuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes1 = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var headersBytes3 = new HeadersFrame(3, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes1);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(headersBytes3));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }
}