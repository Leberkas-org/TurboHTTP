using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 7540 §6.10 — CONTINUATION Frame
/// Phase 14: Enforce END_HEADERS, require contiguous CONTINUATION frames,
/// reject interleaved frames.
///
/// Key invariants tested here:
///   - A HEADERS frame without END_HEADERS MUST be followed exclusively by
///     CONTINUATION frames on the same stream until END_HEADERS is set.
///   - Any other frame type (or CONTINUATION on a different stream) while a
///     header block is pending is a connection error of type PROTOCOL_ERROR.
///   - A CONTINUATION frame without a preceding HEADERS (or PUSH_PROMISE)
///     without END_HEADERS is a connection error of type PROTOCOL_ERROR.
/// </summary>
public sealed class Http2ContinuationFrameTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static byte[] EncodeStatus200()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        return hpack.Encode([(":status", "200")]).ToArray();
    }

    private static byte[] EncodeHeaders(params (string, string)[] headers)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        return hpack.Encode(headers).ToArray();
    }

    private static byte[] ConcatFrames(params byte[][] frames)
    {
        var total = frames.Sum(f => f.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var f in frames)
        {
            f.CopyTo(result, offset);
            offset += f.Length;
        }

        return result;
    }

    // ── Enforce END_HEADERS ──────────────────────────────────────────────────

    /// RFC 9113 §6.10 — HEADERS with END_HEADERS completes immediately without CONTINUATION
    [Fact(DisplayName = "RFC9113-6.10-CF-001: HEADERS with END_HEADERS completes immediately without CONTINUATION")]
    public void Should_ProduceResponse_When_HeadersHasEndHeadersSet()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    /// RFC 9113 §6.10 — HEADERS without END_HEADERS produces no response until CONTINUATION
    [Fact(DisplayName = "RFC9113-6.10-CF-002: HEADERS without END_HEADERS produces no response until CONTINUATION")]
    public void Should_ProduceNoResponse_When_HeadersLacksEndHeaders()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: true, endHeaders: false).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        Assert.Empty(session.Responses);
    }

    /// RFC 9113 §6.10 — Single CONTINUATION with END_HEADERS completes header block
    [Fact(DisplayName = "RFC9113-6.10-CF-003: Single CONTINUATION with END_HEADERS completes header block")]
    public void Should_ProduceResponse_When_ContinuationHasEndHeaders()
    {
        var block = EncodeStatus200();
        var split = block.Length / 2;
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(contFrame);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    /// RFC 9113 §6.10 — CONTINUATION without END_HEADERS produces no response
    [Fact(DisplayName = "RFC9113-6.10-CF-004: CONTINUATION without END_HEADERS produces no response")]
    public void Should_ProduceNoResponse_When_ContinuationLacksEndHeaders()
    {
        var block = EncodeStatus200();
        var third = Math.Max(1, block.Length / 3);
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..third], endStream: true, endHeaders: false).Serialize();
        var cont1 = new ContinuationFrame(1, block.AsMemory()[third..Math.Min(2 * third, block.Length)], endHeaders: false).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(cont1);

        Assert.Empty(session.Responses);
    }

    /// RFC 9113 §6.10 — Three CONTINUATION frames with last having END_HEADERS produces response
    [Fact(DisplayName = "RFC9113-6.10-CF-005: Three CONTINUATION frames with last having END_HEADERS produces response")]
    public void Should_ProduceResponse_When_ThreeContinuationFramesComplete()
    {
        var block = EncodeHeaders((":status", "200"), ("x-a", "1"), ("x-b", "2"), ("x-c", "3"));
        var quarter = block.Length / 4;

        var headersFrame = new HeadersFrame(1, block.AsMemory()[..quarter], endStream: true, endHeaders: false).Serialize();
        var cont1 = new ContinuationFrame(1, block.AsMemory()[quarter..(2 * quarter)], endHeaders: false).Serialize();
        var cont2 = new ContinuationFrame(1, block.AsMemory()[(2 * quarter)..(3 * quarter)], endHeaders: false).Serialize();
        var cont3 = new ContinuationFrame(1, block.AsMemory()[(3 * quarter)..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(cont1);
        session.Process(cont2);
        session.Process(cont3);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    /// RFC 9113 §6.10 — Header values preserved across multiple CONTINUATION fragments
    [Fact(DisplayName = "RFC9113-6.10-CF-006: Header values preserved across multiple CONTINUATION fragments")]
    public void Should_PreserveHeaderValues_When_SplitAcrossContinuationFrames()
    {
        var block = EncodeHeaders((":status", "201"), ("content-type", "application/json"), ("x-custom", "hello"));
        var half = block.Length / 2;

        var headersFrame = new HeadersFrame(1, block.AsMemory()[..half], endStream: true, endHeaders: false).Serialize();
        var cont = new ContinuationFrame(1, block.AsMemory()[half..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(cont);

        Assert.Single(session.Responses);
        var resp = session.Responses[0].Response;
        Assert.Equal(201, (int)resp.StatusCode);
        Assert.True(resp.Headers.Contains("x-custom"));
    }

    // ── Require contiguous CONTINUATION frames ───────────────────────────────

    /// RFC 9113 §6.10 — DATA frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-007: DATA frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_DataFrameInterleavesContinuation()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        // A DATA frame on stream 1 while waiting for CONTINUATION.
        var dataFrame = new byte[]
        {
            0x00, 0x00, 0x03, // length = 3
            0x00, 0x01,       // type=DATA, flag=END_STREAM
            0x00, 0x00, 0x00, 0x01, // stream=1
            0x61, 0x62, 0x63  // "abc"
        };

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — PING frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-008: PING frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_PingInterleavesContinuation()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var pingFrame = new PingFrame(new byte[8]).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(pingFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — SETTINGS frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-009: SETTINGS frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_SettingsInterleavesContinuation()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var settingsFrame = new SettingsFrame([]).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(settingsFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — RST_STREAM frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-010: RST_STREAM frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_RstStreamInterleavesContinuation()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var rstFrame = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(rstFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — WINDOW_UPDATE frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-011: WINDOW_UPDATE frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_WindowUpdateInterleavesContinuation()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var windowUpdate = new WindowUpdateFrame(0, 65535).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(windowUpdate));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — GOAWAY frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-012: GOAWAY frame interleaved while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_GoAwayInterleavesContinuation()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var goAwayFrame = new GoAwayFrame(1, Http2ErrorCode.NoError).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(goAwayFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — HEADERS frame for a different stream while awaiting CONTINUATION is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-013: HEADERS frame for a different stream while awaiting CONTINUATION is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_HeadersForOtherStreamInterleavesContinuation()
    {
        var block = EncodeStatus200();
        // First HEADERS on stream 1 without END_HEADERS.
        var headersFrame1 = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        // New HEADERS on stream 3 while stream 1 awaits CONTINUATION.
        var headersFrame3 = new HeadersFrame(3, block, endStream: true, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame1);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame3));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── Reject interleaved frames ─────────────────────────────────────────────

    /// RFC 9113 §6.10 — CONTINUATION on stream 0 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-014: CONTINUATION on stream 0 is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationOnStream0()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream0 = new byte[]
        {
            0x00, 0x00, 0x01, // length=1
            0x09, 0x04,       // type=CONTINUATION, END_HEADERS
            0x00, 0x00, 0x00, 0x00, // stream=0
            0x88
        };

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(contOnStream0));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION on different stream than HEADERS is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-015: CONTINUATION on different stream than HEADERS is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationOnDifferentStream()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream3 = new ContinuationFrame(3, block.AsMemory()[1..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(contOnStream3));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION without preceding HEADERS is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-016: CONTINUATION without preceding HEADERS is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationWithoutPrecedingHeaders()
    {
        var block = EncodeStatus200();
        var contFrame = new ContinuationFrame(1, block, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(contFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION after completed header block is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.10-CF-017: CONTINUATION after completed header block is PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ContinuationAfterCompletedHeaderBlock()
    {
        var block = EncodeStatus200();
        // Complete header block with END_HEADERS.
        var headersFrame = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();
        // Stray CONTINUATION on stream 1 after block is complete.
        var contFrame = new ContinuationFrame(1, new byte[] { 0x88 }, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(contFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── Combined delivery ────────────────────────────────────────────────────

    /// RFC 9113 §6.10 — HEADERS and CONTINUATION in same Process call are processed
    [Fact(DisplayName = "RFC9113-6.10-CF-018: HEADERS and CONTINUATION in same Process call are processed")]
    public void Should_ProduceResponse_When_HeadersAndContinuationDeliveredTogether()
    {
        var block = EncodeStatus200();
        var split = block.Length / 2;
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var combined = ConcatFrames(headersFrame, contFrame);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    /// RFC 9113 §6.10 — HEADERS + three CONTINUATION frames in single Process call
    [Fact(DisplayName = "RFC9113-6.10-CF-019: HEADERS + three CONTINUATION frames in single Process call")]
    public void Should_ProduceResponse_When_ThreeFramesDeliveredTogether()
    {
        var block = EncodeHeaders((":status", "404"), ("x-error", "not-found"));
        var q = block.Length / 4;

        var h = new HeadersFrame(1, block.AsMemory()[..q], endStream: true, endHeaders: false).Serialize();
        var c1 = new ContinuationFrame(1, block.AsMemory()[q..(2 * q)], endHeaders: false).Serialize();
        var c2 = new ContinuationFrame(1, block.AsMemory()[(2 * q)..(3 * q)], endHeaders: false).Serialize();
        var c3 = new ContinuationFrame(1, block.AsMemory()[(3 * q)..], endHeaders: true).Serialize();

        var combined = ConcatFrames(h, c1, c2, c3);

        var session = new Http2ProtocolSession();
        session.Process(combined);

        Assert.Single(session.Responses);
        Assert.Equal(404, (int)session.Responses[0].Response.StatusCode);
    }

    /// RFC 9113 §6.10 — Fragmented CONTINUATION (partial frame bytes) buffered and completed
    [Fact(DisplayName = "RFC9113-6.10-CF-020: Fragmented CONTINUATION (partial frame bytes) buffered and completed")]
    public void Should_BufferPartialContinuation_When_TcpFragmented()
    {
        var block = EncodeStatus200();
        var split = block.Length / 2;
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        // Deliver HEADERS fully.
        session.Process(headersFrame);
        // Deliver CONTINUATION in two TCP fragments: first half of frame bytes, then rest.
        var halfCont = contFrame.Length / 2;
        session.Process(contFrame.AsMemory()[..halfCont]);
        Assert.Empty(session.Responses); // incomplete frame — buffered
        session.Process(contFrame.AsMemory()[halfCont..]);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    /// RFC 9113 §6.10 — Reset clears pending CONTINUATION state
    [Fact(DisplayName = "RFC9113-6.10-CF-021: Reset clears pending CONTINUATION state")]
    public void Should_ClearPendingContinuation_When_Reset()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // After reset, a new stream should be accepted without PROTOCOL_ERROR.
        session.Reset();

        var block2 = EncodeStatus200();
        var freshFrame = new HeadersFrame(1, block2, endStream: true, endHeaders: true).Serialize();
        session.Process(freshFrame);

        Assert.Single(session.Responses);
    }

    /// RFC 9113 §6.10 — Error message includes offending stream ID when CONTINUATION on wrong stream
    [Fact(DisplayName = "RFC9113-6.10-CF-022: Error message includes offending stream ID when CONTINUATION on wrong stream")]
    public void Should_IncludeStreamIdInErrorMessage_When_ContinuationOnWrongStream()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();
        var contOnStream5 = new ContinuationFrame(5, block.AsMemory()[1..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(contOnStream5));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        // Error message should mention stream IDs involved.
        Assert.Contains("5", ex.Message);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — CONTINUATION flood protection triggers at 1000 frames
    [Fact(DisplayName = "RFC9113-6.10-CF-023: CONTINUATION flood protection triggers at 1000 frames")]
    public void Should_ThrowProtocolError_When_ContinuationFloodExceeds1000Frames()
    {
        var block = EncodeStatus200();
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..1], endStream: false, endHeaders: false).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        var ex = Assert.Throws<Http2Exception>(() =>
        {
            for (var i = 0; i < 1001; i++)
            {
                var cont = new ContinuationFrame(1, new byte[] { 0x00 }, endHeaders: false).Serialize();
                session.Process(cont);
            }
        });
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.10 — END_STREAM on HEADERS is carried through to reassembled response
    [Fact(DisplayName = "RFC9113-6.10-CF-024: END_STREAM on HEADERS is carried through to reassembled response")]
    public void Should_CarryEndStream_When_ContinuationCompletesHeaderBlock()
    {
        var block = EncodeStatus200();
        var split = block.Length / 2;
        // endStream=true on HEADERS, no body expected.
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..split], endStream: true, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(contFrame);

        // Response must be present — END_STREAM means no DATA frame will follow.
        Assert.Single(session.Responses);
    }

    /// RFC 9113 §6.10 — Without END_STREAM on HEADERS, response awaits DATA frame
    [Fact(DisplayName = "RFC9113-6.10-CF-025: Without END_STREAM on HEADERS, response awaits DATA frame")]
    public void Should_WaitForDataFrame_When_HeadersLacksEndStream()
    {
        var block = EncodeStatus200();
        var split = block.Length / 2;
        // endStream=false — a DATA frame will carry the body.
        var headersFrame = new HeadersFrame(1, block.AsMemory()[..split], endStream: false, endHeaders: false).Serialize();
        var contFrame = new ContinuationFrame(1, block.AsMemory()[split..], endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.Process(contFrame);

        // No response yet — body will follow in DATA frame.
        Assert.Empty(session.Responses);

        // Deliver a DATA frame with END_STREAM.
        session.SetStreamReceiveWindow(1, 65535);
        var dataPayload = new byte[] { 0x68, 0x69 }; // "hi"
        var dataFrame = BuildDataFrame(1, dataPayload, endStream: true);
        session.Process(dataFrame);

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
    }

    // ── helper: manually craft a DATA frame ──────────────────────────────────
    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream)
    {
        var frame = new byte[9 + data.Length];
        frame[0] = (byte)((data.Length >> 16) & 0xFF);
        frame[1] = (byte)((data.Length >> 8) & 0xFF);
        frame[2] = (byte)(data.Length & 0xFF);
        frame[3] = 0x00; // type = DATA
        frame[4] = endStream ? (byte)0x01 : (byte)0x00;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        data.CopyTo(frame, 9);
        return frame;
    }
}
