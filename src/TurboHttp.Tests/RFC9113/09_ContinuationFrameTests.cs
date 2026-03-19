using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2ContinuationFrameTests
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


    /// RFC 9113 §8.2 / §6.10 — HEADERS with END_HEADERS flag set completes immediately
    [Fact(DisplayName = "RFC9113-8.2-CF-001: HEADERS with END_HEADERS decoded with EndHeaders=true")]
    public void Should_HaveEndHeadersTrue_When_HeadersHasEndHeadersSet()
    {
        var block = EncodeBlock((":status", "200"));
        var bytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.True(frame.EndHeaders);
        Assert.True(frame.EndStream);

        // Full block is just the HEADERS fragment — no CONTINUATION needed.
        var fullBlock = AssembleHeaderBlock(frames);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
    }

    /// RFC 9113 §8.2 / §6.10 — HEADERS without END_HEADERS decoded with EndHeaders=false
    [Fact(DisplayName = "RFC9113-8.2-CF-002: HEADERS without END_HEADERS decoded with EndHeaders=false")]
    public void Should_HaveEndHeadersFalse_When_HeadersLacksEndHeaders()
    {
        var block = EncodeBlock((":status", "200"));
        var partial = block[..1];
        var bytes = new HeadersFrame(1, partial.AsMemory(), endStream: true, endHeaders: false).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.False(frame.EndHeaders);
    }

    /// RFC 9113 §8.2 / §6.10 — Single CONTINUATION with END_HEADERS completes the header block
    [Fact(DisplayName = "RFC9113-8.2-CF-003: Single CONTINUATION with END_HEADERS completes header block")]
    public void Should_DecodeCorrectly_When_ContinuationHasEndHeaders()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = ConcatArrays(headersBytes, contBytes);
        var decoded = decoder.Decode(frames);

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.False(hf.EndHeaders);
        Assert.True(cf.EndHeaders);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
    }

    /// RFC 9113 §8.2 / §6.10 — CONTINUATION without END_HEADERS does not complete the block
    [Fact(DisplayName = "RFC9113-8.2-CF-004: CONTINUATION without END_HEADERS has EndHeaders=false")]
    public void Should_HaveEndHeadersFalse_When_ContinuationLacksEndHeaders()
    {
        var block = EncodeBlock((":status", "200"));
        var third = Math.Max(1, block.Length / 3);
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..third], endStream: true, endHeaders: false).Serialize();
        var cont1Bytes = new ContinuationFrame(1, block.AsMemory()[third..Math.Min(2 * third, block.Length)], endHeaders: false).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, cont1Bytes));

        Assert.Equal(2, decoded.Count);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.False(cf.EndHeaders);
    }

    /// RFC 9113 §8.2 / §6.10 — Three CONTINUATION frames with last having END_HEADERS
    [Fact(DisplayName = "RFC9113-8.2-CF-005: Three CONTINUATION frames with last END_HEADERS assembles correctly")]
    public void Should_AssembleBlock_When_ThreeContinuationFramesComplete()
    {
        var block = EncodeBlock((":status", "200"), ("x-a", "1"), ("x-b", "2"), ("x-c", "3"));
        var quarter = block.Length / 4;

        var h = new HeadersFrame(1, block.AsMemory()[..quarter], endStream: true, endHeaders: false).Serialize();
        var c1 = new ContinuationFrame(1, block.AsMemory()[quarter..(2 * quarter)], endHeaders: false).Serialize();
        var c2 = new ContinuationFrame(1, block.AsMemory()[(2 * quarter)..(3 * quarter)], endHeaders: false).Serialize();
        var c3 = new ContinuationFrame(1, block.AsMemory()[(3 * quarter)..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(h, c1, c2, c3));
        Assert.Equal(4, decoded.Count);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h2 => h2.Name == ":status" && h2.Value == "200");
        Assert.Contains(headers, h2 => h2.Name == "x-a" && h2.Value == "1");
        Assert.Contains(headers, h2 => h2.Name == "x-c" && h2.Value == "3");
    }

    /// RFC 9113 §8.2 / §6.10 — Header values preserved across multiple CONTINUATION fragments
    [Fact(DisplayName = "RFC9113-8.2-CF-006: Header values preserved across multiple CONTINUATION fragments")]
    public void Should_PreserveHeaderValues_When_SplitAcrossContinuationFrames()
    {
        var block = EncodeBlock((":status", "201"), ("content-type", "application/json"), ("x-custom", "hello"));
        var half = block.Length / 2;

        var headersBytes = new HeadersFrame(1, block.AsMemory()[..half], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[half..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));
        Assert.Equal(2, decoded.Count);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "201");
        Assert.Contains(headers, h => h.Name == "x-custom" && h.Value == "hello");
        Assert.Contains(headers, h => h.Name == "content-type" && h.Value == "application/json");
    }


    /// RFC 9113 §6.10 — DATA frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-007: DATA frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_DataFrameInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        // A raw DATA frame on stream 1.
        var dataFrame = new byte[]
        {
            0x00, 0x00, 0x03, // length = 3
            0x00, 0x01,       // type=DATA, flag=END_STREAM
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x61, 0x62, 0x63  // "abc"
        };

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(dataFrame);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — PING frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-008: PING frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_PingInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var pingBytes = new PingFrame(new byte[8]).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(pingBytes);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — SETTINGS frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-009: SETTINGS frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_SettingsInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var settingsBytes = new SettingsFrame([]).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(settingsBytes);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — RST_STREAM frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-010: RST_STREAM frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_RstStreamInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var rstBytes = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(rstBytes);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — WINDOW_UPDATE frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-011: WINDOW_UPDATE frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_WindowUpdateInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var windowUpdateBytes = new WindowUpdateFrame(0, 65535).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(windowUpdateBytes);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — GOAWAY frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-012: GOAWAY frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_GoAwayInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var goAwayBytes = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(goAwayBytes);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — HEADERS on a different stream while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-013: HEADERS on different stream while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_HeadersForOtherStreamInterleavesContinuation()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes1 = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var headersBytes3 = new HeadersFrame(3, block.AsMemory(), endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes1);
        var secondDecoded = decoder.Decode(headersBytes3);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §6.10 — CONTINUATION on stream 0 is decoded with StreamId=0; treated as wrong stream
    [Fact(DisplayName = "RFC9113-8.2-CF-014: CONTINUATION on stream 0 while awaiting stream 1 is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationOnStream0()
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

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(contOnStream0);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION on different stream than HEADERS is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-015: CONTINUATION on stream 3 while awaiting stream 1 is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationOnDifferentStream()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream3 = new ContinuationFrame(3, block.AsMemory()[1..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(contOnStream3);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION without preceding HEADERS is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-016: CONTINUATION without preceding HEADERS is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationWithoutPrecedingHeaders()
    {
        var block = EncodeBlock((":status", "200"));
        var contBytes = new ContinuationFrame(1, block.AsMemory(), endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(contBytes);

        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(decoded));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION after completed header block (after END_HEADERS) is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-8.2-CF-017: CONTINUATION after completed header block is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationAfterCompletedHeaderBlock()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory(), endStream: true, endHeaders: true).Serialize();
        var extraContBytes = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var allDecoded = decoder.Decode(ConcatArrays(headersBytes, extraContBytes));

        // AssembleHeaderBlock processes both; after HEADERS with END_HEADERS, there's no
        // pending stream. The orphan CONTINUATION throws.
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(allDecoded));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }


    /// RFC 9113 §8.2 / §6.10 — HEADERS and CONTINUATION in same byte buffer are decoded together
    [Fact(DisplayName = "RFC9113-8.2-CF-018: HEADERS and CONTINUATION in same Decode call are decoded together")]
    public void Should_DecodeBothFrames_When_HeadersAndContinuationDeliveredTogether()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.IsType<ContinuationFrame>(decoded[1]);

        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
    }

    /// RFC 9113 §8.2 / §6.10 — HEADERS + three CONTINUATION frames in single Decode call
    [Fact(DisplayName = "RFC9113-8.2-CF-019: HEADERS + three CONTINUATION frames in single Decode call")]
    public void Should_DecodeAllFrames_When_ThreeFramesDeliveredTogether()
    {
        var block = EncodeBlock((":status", "404"), ("x-error", "not-found"));
        var q = block.Length / 4;

        var h = new HeadersFrame(1, block.AsMemory()[..q], endStream: true, endHeaders: false).Serialize();
        var c1 = new ContinuationFrame(1, block.AsMemory()[q..(2 * q)], endHeaders: false).Serialize();
        var c2 = new ContinuationFrame(1, block.AsMemory()[(2 * q)..(3 * q)], endHeaders: false).Serialize();
        var c3 = new ContinuationFrame(1, block.AsMemory()[(3 * q)..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(h, c1, c2, c3));

        Assert.Equal(4, decoded.Count);
        var fullBlock = AssembleHeaderBlock(decoded);
        var headers = new HpackDecoder().Decode(fullBlock.AsSpan());
        Assert.Contains(headers, hdr => hdr.Name == ":status" && hdr.Value == "404");
        Assert.Contains(headers, hdr => hdr.Name == "x-error" && hdr.Value == "not-found");
    }

    /// RFC 9113 §8.2 / §6.10 — Partial CONTINUATION (TCP fragmentation) buffered until complete
    [Fact(DisplayName = "RFC9113-8.2-CF-020: Partial CONTINUATION (TCP-fragmented) is buffered until complete")]
    public void Should_BufferPartialContinuation_When_TcpFragmented()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
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
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
    }

    /// RFC 9113 §8.2 / §6.10 — Decoder.Reset() clears buffered remainder; next block accepted cleanly
    [Fact(DisplayName = "RFC9113-8.2-CF-021: Decoder Reset clears buffered remainder; next header block accepted")]
    public void Should_AcceptNewBlock_After_DecoderReset()
    {
        var block = EncodeBlock((":status", "200"));
        // Deliver first byte of HEADERS only — decoder buffers the remainder.
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var halfHeader = headersBytes.Length / 2;

        var decoder = new Http2FrameDecoder();
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
        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "200");
    }

    /// RFC 9113 §8.2 / §6.10 — Error message includes offending stream ID
    [Fact(DisplayName = "RFC9113-8.2-CF-022: Error message includes offending stream ID when CONTINUATION on wrong stream")]
    public void Should_IncludeStreamIdInErrorMessage_When_ContinuationOnWrongStream()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream5 = new ContinuationFrame(5, block.AsMemory()[1..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var firstDecoded = decoder.Decode(headersBytes);
        var secondDecoded = decoder.Decode(contOnStream5);

        var combined = firstDecoded.Concat(secondDecoded).ToList();
        var ex = Assert.Throws<Http2Exception>(() => AssembleHeaderBlock(combined));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Contains("5", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION flood protection triggers at ≥1000 frames
    [Fact(DisplayName = "RFC9113-8.2-CF-023: CONTINUATION flood protection triggers at 1000 frames")]
    public void Should_ThrowProtocolError_When_ContinuationFloodExceeds1000Frames()
    {
        var block = EncodeBlock((":status", "200"));
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        // Build 1001 no-END_HEADERS CONTINUATION frames.
        var frames = new List<Http2Frame>();
        var decoder = new Http2FrameDecoder();
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

    /// RFC 9113 §8.2 / §6.10 — END_STREAM flag on HEADERS is carried through to the final frame
    [Fact(DisplayName = "RFC9113-8.2-CF-024: END_STREAM flag on HEADERS is preserved in the decoded frame")]
    public void Should_PreserveEndStream_When_ContinuationCompletesHeaderBlock()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.True(hf.EndStream); // END_STREAM preserved on HEADERS frame
        Assert.False(hf.EndHeaders);
        var cf = Assert.IsType<ContinuationFrame>(decoded[1]);
        Assert.True(cf.EndHeaders);
    }

    /// RFC 9113 §8.2 / §6.10 — HEADERS without END_STREAM decoded with EndStream=false
    [Fact(DisplayName = "RFC9113-8.2-CF-025: HEADERS without END_STREAM decoded with EndStream=false")]
    public void Should_HaveEndStreamFalse_When_HeadersLacksEndStream()
    {
        var block = EncodeBlock((":status", "200"));
        var split = block.Length / 2;
        var headersBytes = new HeadersFrame(1, block.AsMemory()[..split], endStream: false, endHeaders: false).Serialize();
        var contBytes = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var decoded = decoder.Decode(ConcatArrays(headersBytes, contBytes));

        Assert.Equal(2, decoded.Count);
        var hf = Assert.IsType<HeadersFrame>(decoded[0]);
        Assert.False(hf.EndStream); // no DATA expected to follow — caller handles body assembly
    }
}
