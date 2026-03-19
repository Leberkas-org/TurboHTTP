using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2HeaderBlockDecoderTests
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


    /// RFC 9113 §8.2 — HEADERS frame with END_HEADERS set is decoded with EndHeaders=true
    [Fact(DisplayName = "RFC9113-8.2-HBD-001: HEADERS with END_HEADERS flag decoded with EndHeaders=true")]
    public void Should_SetEndHeadersTrue_WhenEndHeadersFlagSet()
    {
        var block = MakeBlock((":status", "200"));
        var bytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(hf.EndHeaders);
    }


    /// RFC 9113 §8.2 — HEADERS frame without END_HEADERS set is decoded with EndHeaders=false
    [Fact(DisplayName = "RFC9113-8.2-HBD-002: HEADERS without END_HEADERS flag decoded with EndHeaders=false")]
    public void Should_SetEndHeadersFalse_WhenEndHeadersFlagNotSet()
    {
        var block = MakeBlock((":status", "200"));
        var bytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: true, endHeaders: false).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(hf.EndHeaders);
    }


    /// RFC 9113 §8.2 — CONTINUATION frame with END_HEADERS is decoded with EndHeaders=true
    [Fact(DisplayName = "RFC9113-8.2-HBD-003: CONTINUATION with END_HEADERS flag decoded with EndHeaders=true")]
    public void Should_SetEndHeadersTrue_WhenContinuationEndHeadersFlagSet()
    {
        var block = MakeBlock((":status", "200"));
        var contBytes = new ContinuationFrame(1, block.AsMemory(), endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(contBytes);

        Assert.Single(frames);
        var cf = Assert.IsType<ContinuationFrame>(frames[0]);
        Assert.True(cf.EndHeaders);
    }


    /// RFC 9113 §8.2 — CONTINUATION frame without END_HEADERS is decoded with EndHeaders=false
    [Fact(DisplayName = "RFC9113-8.2-HBD-004: CONTINUATION without END_HEADERS flag decoded with EndHeaders=false")]
    public void Should_SetEndHeadersFalse_WhenContinuationEndHeadersFlagNotSet()
    {
        var block = MakeBlock((":status", "200"));
        var contBytes = new ContinuationFrame(1, block.AsMemory()[..1], endHeaders: false).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(contBytes);

        Assert.Single(frames);
        var cf = Assert.IsType<ContinuationFrame>(frames[0]);
        Assert.False(cf.EndHeaders);
    }


    /// RFC 9113 §8.2 — HeaderBlockFragment from decoded HEADERS frame is the original HPACK block
    [Fact(DisplayName = "RFC9113-8.2-HBD-005: HeaderBlockFragment from decoded HEADERS frame contains the HPACK block")]
    public void Should_ReturnOriginalHpackBlock_WhenHeadersFrameDecoded()
    {
        var block = MakeBlock((":status", "200"), ("content-type", "text/plain"));
        var bytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        var fragment = hf.HeaderBlockFragment;
        Assert.Equal(block, fragment.ToArray());

        // HPACK decode of the fragment must yield the original headers.
        var headers = new HpackDecoder().Decode(fragment.Span);
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(headers, h => h.Name == "content-type" && h.Value == "text/plain");
    }


    /// RFC 9113 §8.2 — HeaderBlockFragment from decoded CONTINUATION frame is the fragment bytes
    [Fact(DisplayName = "RFC9113-8.2-HBD-006: HeaderBlockFragment from decoded CONTINUATION frame contains its fragment bytes")]
    public void Should_ReturnFragmentBytes_WhenContinuationFrameDecoded()
    {
        var fullBlock = MakeBlock((":status", "201"), ("x-trace", "abc"));
        var half = fullBlock.Length / 2;
        var part1 = fullBlock[..half];
        var part2 = fullBlock[half..];

        var headersBytes = new HeadersFrame(1, part1.AsMemory(), endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, part2.AsMemory(), endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(Concat(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.Equal(part2, cf.HeaderBlockFragment.ToArray());
    }


    /// RFC 9113 §8.2 — Assembled HEADERS + CONTINUATION block decoded by HpackDecoder yields all headers
    [Fact(DisplayName = "RFC9113-8.2-HBD-007: Assembled HEADERS+CONTINUATION block decoded by HpackDecoder yields all headers")]
    public void Should_ContainAllHeaders_WhenBlockSplitAcrossFrames()
    {
        var block = MakeBlock(
            (":status", "200"),
            ("content-type", "application/json"),
            ("x-custom", "value123"),
            ("cache-control", "no-cache"));

        var half = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..half], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[half..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
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

        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(headers, h => h.Name == "content-type" && h.Value == "application/json");
        Assert.Contains(headers, h => h.Name == "x-custom" && h.Value == "value123");
        Assert.Contains(headers, h => h.Name == "cache-control" && h.Value == "no-cache");
    }


    /// RFC 9113 §8.3 — HeadersFrame built with HpackEncoder encodes correctly; HpackDecoder decodes
    /// the fragment; response pseudo-header :status is present and valid.
    [Fact(DisplayName = "RFC9113-8.3-HBD-008: HeadersFrame HPACK fragment round-trips correctly through HpackDecoder")]
    public void Should_DecodeHpackFragmentCorrectly_WhenHeadersFrameRoundTripped()
    {
        var enc = new HpackEncoder(useHuffman: false);
        var block = enc.Encode([(":status", "204"), ("content-length", "0")]);
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true);

        // Serialize → decode frame bytes → decode HPACK.
        var bytes = headersFrame.Serialize();
        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(bytes);

        Assert.Single(decoded);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.True(hf.EndHeaders);
        Assert.True(hf.EndStream);
        Assert.Equal(1, hf.StreamId);

        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(hf.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "204");
        Assert.Contains(headers, h => h.Name == "content-length" && h.Value == "0");
    }
}
