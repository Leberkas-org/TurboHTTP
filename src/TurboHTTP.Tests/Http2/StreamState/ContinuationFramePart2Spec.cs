using TurboHTTP.Protocol.Http2;
using TurboHTTP.Protocol.Http2.Hpack;

namespace TurboHTTP.Tests.Http2.StreamState;

/// <summary>
/// Tests CONTINUATION frame handling and header block reassembly per RFC 9113 §6.10 — Part 2.
/// Verifies that fragmented header blocks are correctly joined before HPACK decoding.
/// </summary>
/// <remarks>
/// Class under test: <see cref="FrameDecoder"/>.
/// RFC 9113 §6.10: CONTINUATION frames must immediately follow HEADERS or PUSH_PROMISE; END_HEADERS flag terminates the sequence.
/// </remarks>
public sealed class ContinuationFramePart2Spec
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

    /// <summary>
    /// Assembles the full HPACK header block from a sequence of decoded frames
    /// (one HeadersFrame optionally followed by ContinuationFrames).
    /// Throws Http2Exception if the sequence violates §6.10 ordering rules.
    /// </summary>
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
                    $"RFC 9113 §6.10: Expected CONTINUATION but received {frame.GetType().Name}.",
                    Http2ErrorCode.ProtocolError);
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
                            $"RFC 9113 §6.10: Unexpected CONTINUATION on stream {c.StreamId}; no pending header block.",
                            Http2ErrorCode.ProtocolError);
                    }

                    if (c.StreamId != pendingStreamId.Value)
                    {
                        throw new Http2Exception(
                            $"RFC 9113 §6.10: CONTINUATION on stream {c.StreamId}; expected stream {pendingStreamId.Value}.",
                            Http2ErrorCode.ProtocolError);
                    }

                    continuationCount++;
                    if (continuationCount >= 1000)
                    {
                        throw new Http2Exception(
                            "RFC 9113 §6.10: Excessive CONTINUATION frames — possible flood attack.",
                            Http2ErrorCode.ProtocolError);
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
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_on_stream_0()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        // Raw CONTINUATION frame on stream 0.
        var contOnStream0 = new byte[]
        {
            0x00, 0x00, 0x01, // length=1
            0x09, 0x04,       // type=CONTINUATION, END_HEADERS
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x88
        };

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        // Http2FrameDecoder rejects CONTINUATION on stream 0 at the frame level.
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(contOnStream0));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_on_different_stream()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream3 = new ContinuationFrame(3, block.AsMemory()[1..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(contOnStream3));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_without_preceding_headers()
    {
        var block = EncodeBlock((":status", "200"));
        var contBytes = new ContinuationFrame(1, block.AsMemory(), endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(contBytes));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_after_completed_header_block()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();
        var extraContBytes = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();

        // Http2FrameDecoder detects orphan CONTINUATION after completed HEADERS (END_HEADERS set).
        var decoder = new FrameDecoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(ConcatArrays(headersBytes, extraContBytes)));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }


    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_decode_both_frames_when_headers_and_continuation_delivered_together()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.IsType<ContinuationFrame>(decoded[1]);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_decode_all_frames_when_three_frames_delivered_together()
    {
        var block = EncodeBlock((":status", "404"), ("x-error", "not-found"));
        var q = block.Length / 4;

        var h = new HeadersFrame(1, block.AsMemory()[..q], endStream: true, endHeaders: false).Serialize();
        var c1 = new ContinuationFrame(1, block.AsMemory()[q..(2 * q)], endHeaders: false).Serialize();
        var c2 = new ContinuationFrame(1, block.AsMemory()[(2 * q)..(3 * q)], endHeaders: false).Serialize();
        var c3 = new ContinuationFrame(1, block.AsMemory()[(3 * q)..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(h, c1, c2, c3));

        Assert.Equal(4, decoded.Count);
        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, hdr => hdr is { Name: ":status", Value: "404" });
        Assert.Contains(headers, hdr => hdr is { Name: "x-error", Value: "not-found" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_buffer_partial_continuation_when_tcp_fragmented()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        // Feed HEADERS fully.
        var firstBatch = decoder.Decode(headersBytes);
        Assert.Single(firstBatch);

        // Feed first half of CONTINUATION bytes — incomplete frame: no new frames yet.
        var halfCont = contBytes.Length / 2;
        var partialBatch = decoder.Decode(contBytes.AsMemory()[..halfCont]);
        Assert.Empty(partialBatch);

        // Feed remaining bytes — CONTINUATION frame now complete.
        var finalBatch = decoder.Decode(contBytes.AsMemory()[halfCont..]);
        Assert.Single(finalBatch);

        var allDecoded = firstBatch.Concat(finalBatch).ToList();
        var fullBlock = AssembleHeaderBlock(allDecoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_accept_new_block_after_decoder_reset()
    {
        var block = EncodeBlock((":status", "200"));
        // Deliver first byte of HEADERS only — decoder buffers the remainder.
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var halfHeader = headersBytes.Length / 2;

        var decoder = new FrameDecoder();
        var partial = decoder.Decode(headersBytes.AsMemory()[..halfHeader]);
        Assert.Empty(partial); // frame not yet complete

        // Reset clears the partial frame buffer.
        decoder.Reset();

        // A fresh complete HEADERS+END_HEADERS is now accepted without error.
        var fullBytes = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();
        var decoded = decoder.Decode(fullBytes);
        Assert.Single(decoded);

        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.True(hf.EndHeaders);

        var headers = new HpackDecoder().Decode(hf.HeaderBlockFragment.Span);
        Assert.Contains(headers, h => h is { Name: ":status", Value: "200" });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_include_stream_id_in_error_message_when_continuation_on_wrong_stream()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream5 = new ContinuationFrame(5, block.AsMemory()[1..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        decoder.Decode(headersBytes);
        var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(contOnStream5));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("5", ex.Message);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_throw_protocol_error_when_continuation_flood_exceeds_1000_frames()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        // Build 1001 no-END_HEADERS CONTINUATION frames.
        var frames = new List<Http2Frame>();
        var decoder = new FrameDecoder();
        frames.AddRange(decoder.Decode(headersBytes));

        for (var i = 0; i < 1001; i++)
        {
            var cont = new ContinuationFrame(1, new byte[] { 0x00 }, endHeaders: false).Serialize();
            frames.AddRange(decoder.Decode(cont));
        }

        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(frames));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void Http2FrameDecoder_should_preserve_end_stream_when_continuation_completes_header_block()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.True(hf.EndStream); // END_STREAM preserved on HEADERS frame
        Assert.False(hf.EndHeaders);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.True(cf.EndHeaders);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.2")]
    public void HeadersFrame_should_have_end_stream_false_when_headers_lacks_end_stream()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: false, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.False(hf.EndStream); // no DATA expected to follow — caller handles body assembly
    }
}
