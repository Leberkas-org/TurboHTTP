using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.FrameDecoding;

public sealed class DecoderStreamValidationSpec
{
    private static byte[] MakeBlock(params (string Name, string Value)[] headers)
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode(headers).ToArray();
    }

    private static byte[] Concat(params byte[][] arrays)
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

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_set_end_headers_true_when_end_headers_flag_set()
    {
        var block = MakeBlock((":status", "200"));
        var bytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_set_end_headers_false_when_end_headers_flag_not_set()
    {
        var block = MakeBlock((":status", "200"));
        var bytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: true, endHeaders: false).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_set_end_headers_true_when_continuation_end_headers_flag_set()
    {
        var block = MakeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes =
            new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(Concat(headersBytes, contBytes));

        Assert.Equal(2, frames.Count);
        var cf = Assert.IsType<ContinuationFrame>(frames[1]);
        Assert.True(cf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_set_end_headers_false_when_continuation_end_headers_flag_not_set()
    {
        var block = MakeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[1..], endHeaders: false).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(Concat(headersBytes, contBytes));

        Assert.Equal(2, frames.Count);
        var cf = Assert.IsType<ContinuationFrame>(frames[1]);
        Assert.False(cf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_return_original_hpack_block_when_headers_frame_decoded()
    {
        var block = MakeBlock((":status", "200"), ("content-type", "text/plain"));
        var bytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        var fragment = hf.HeaderBlockFragment;
        Assert.Equal(block, fragment.ToArray());

        // HPACK decode of the fragment must yield the original headers.
        var headers = new HpackDecoder().Decode(fragment.Span);
        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
        Assert.Contains(headers, h => h is { Name: "content-type", Value: "text/plain" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_return_fragment_bytes_when_continuation_frame_decoded()
    {
        var fullBlock = MakeBlock((":status", "201"), ("x-trace", "abc"));
        var half = fullBlock.Length / 2;
        var part1 = fullBlock[..half];
        var part2 = fullBlock[half..];

        var headersBytes = new HeadersFrame(1, part1.AsMemory(), endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, part2.AsMemory(), endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(Concat(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.Equal(part2, cf.HeaderBlockFragment.ToArray());
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_contain_all_headers_when_block_split_across_frames()
    {
        var block = MakeBlock(
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-custom", "value123"),
            ("cache-control", "no-cache"));

        var half = block.Length / 2;
        var headersBytes =
            new HeadersFrame(1, block.AsMemory()[..half], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[half..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(Concat(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);

        // Assemble the full block from both fragments.
        var fullBlock = new byte[hf.HeaderBlockFragment.Length + cf.HeaderBlockFragment.Length];
        hf.HeaderBlockFragment.Span.CopyTo(fullBlock);
        cf.HeaderBlockFragment.Span.CopyTo(fullBlock.AsSpan(hf.HeaderBlockFragment.Length));

        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(fullBlock.AsSpan());

        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
        Assert.Contains(headers, h => h is { Name: "content-type", Value: "application/json" });
        Assert.Contains(headers, h => h is { Name: "x-custom", Value: "value123" });
        Assert.Contains(headers, h => h is { Name: "cache-control", Value: "no-cache" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.3")]
    public void Http2FrameDecoder_should_decode_hpack_fragment_correctly_when_headers_frame_round_tripped()
    {
        var enc = new HpackEncoder(useHuffman: false);
        var block = enc.Encode([(":status", "204"), ("content-length", "0")]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true);

        // Serialize â†’ decode frame bytes â†’ decode HPACK.
        var bytes = headersFrame.Serialize();
        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(bytes);

        Assert.Single(decoded);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.True(hf.EndHeaders);
        Assert.True(hf.EndStream);
        Assert.Equal(1, hf.StreamId);

        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(hf.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h is { Name: ":status", Value: "204" });
        Assert.Contains(headers, h => h is { Name: "content-length", Value: "0" });
    }
}
