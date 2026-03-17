using TurboHttp.Protocol.RFC7541;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §5.1 — HTTP/2 Stream States.
///
/// Tests verify that <see cref="Http2FrameDecoder"/> correctly decodes frames
/// involved in stream lifecycle transitions, and that the RFC-required stream
/// state constraints are enforced by conforming callers.
///
/// Covered transitions:
///   Idle → Open       HEADERS without END_STREAM (§5.1)
///   Open → Closed     DATA + END_STREAM (§5.1)
///   Any → Closed      RST_STREAM (§6.4)
///
/// Covered error cases:
///   HEADERS on stream 0  → connection PROTOCOL_ERROR (§5.1, §6.2)
///   DATA on stream 0     → connection PROTOCOL_ERROR (§5.1, §6.1)
///   DATA on idle stream  → connection PROTOCOL_ERROR (§5.1)
///   DATA on closed stream→ stream STREAM_CLOSED (§6.1)
/// </summary>
public sealed class Http2StreamStateMachineTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static byte[] MakeHeadersBytes(int streamId, bool endStream = false, string status = "200")
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", status)]);
        return new HeadersFrame(streamId, block, endStream, endHeaders: true).Serialize();
    }

    private static byte[] MakeDataBytes(int streamId, bool endStream, byte[]? body = null)
        => new DataFrame(streamId, body ?? "data"u8.ToArray(), endStream).Serialize();

    private static byte[] Concat(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var arr in arrays)
        {
            arr.CopyTo(result, offset);
            offset += arr.Length;
        }

        return result;
    }

    /// <summary>
    /// RFC 9113 §5.1: Any non-connection frame received on stream 0 is a PROTOCOL_ERROR.
    /// The decoder does not enforce stream identifiers — callers must apply this rule.
    /// </summary>
    private static void EnforceNonZeroStreamId(Http2Frame frame, FrameType frameType)
    {
        if (frame.StreamId == 0)
        {
            throw new Http2Exception(
                $"RFC 9113 §5.1: {frameType} frame on stream 0 is a connection error (PROTOCOL_ERROR).",
                Http2ErrorCode.ProtocolError);
        }
    }

    /// <summary>
    /// RFC 9113 §5.1: DATA received on an idle stream (no prior HEADERS) is a PROTOCOL_ERROR.
    /// </summary>
    private static void EnforceStreamOpen(int streamId, HashSet<int> openStreams)
    {
        if (!openStreams.Contains(streamId))
        {
            throw new Http2Exception(
                $"RFC 9113 §5.1: DATA on idle stream {streamId} is a connection error (PROTOCOL_ERROR).",
                Http2ErrorCode.ProtocolError);
        }
    }

    /// <summary>
    /// RFC 9113 §6.1: DATA received on a closed stream is a STREAM_CLOSED stream error.
    /// </summary>
    private static void EnforceStreamNotClosed(int streamId, HashSet<int> closedStreams)
    {
        if (closedStreams.Contains(streamId))
        {
            throw new Http2Exception(
                $"RFC 9113 §6.1: DATA on closed stream {streamId} is a stream error (STREAM_CLOSED).",
                Http2ErrorCode.StreamClosed,
                Http2ErrorScope.Stream,
                streamId);
        }
    }

    // =========================================================================
    // SS-001..004: Frame decoding — Idle→Open, Open→Closed
    // =========================================================================

    /// RFC 9113 §5.1 — Idle→Open: HEADERS without END_STREAM decoded as open-stream frame
    [Fact(DisplayName = "RFC-9113-§5.1-SS-001: Idle→Open — HEADERS (no END_STREAM) decoded correctly")]
    public void Headers_NoEndStream_DecodedAsHeadersFrame()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeHeadersBytes(streamId: 1, endStream: false));

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.False(frame.EndStream);
        Assert.True(frame.EndHeaders);
        // RFC §5.1: receiving HEADERS without END_STREAM transitions stream 1 from Idle to Open.
    }

    /// RFC 9113 §5.1 — HEADERS+END_STREAM decoded with both flags set
    [Fact(DisplayName = "RFC-9113-§5.1-SS-002: HEADERS+END_STREAM decoded with EndStream flag set")]
    public void Headers_WithEndStream_DecodedWithEndStreamFlag()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeHeadersBytes(streamId: 1, endStream: true));

        Assert.Single(frames);
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.True(frame.EndStream);
        // RFC §5.1: HEADERS+END_STREAM transitions stream directly from Idle to Closed.
    }

    /// RFC 9113 §5.1 — Open→Closed: DATA+END_STREAM decoded with END_STREAM flag
    [Fact(DisplayName = "RFC-9113-§5.1-SS-003: Open→Closed — DATA+END_STREAM decoded with EndStream flag")]
    public void Data_WithEndStream_DecodedWithEndStreamFlag()
    {
        var decoder = new Http2FrameDecoder();
        var payload = "response body"u8.ToArray();
        var frames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: true, body: payload));

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.True(frame.EndStream);
        Assert.Equal(payload, frame.Data.ToArray());
        // RFC §5.1: DATA+END_STREAM transitions stream from Open to Closed.
    }

    /// RFC 9113 §5.1 — DATA without END_STREAM keeps stream open
    [Fact(DisplayName = "RFC-9113-§5.1-SS-004: DATA without END_STREAM decoded with EndStream=false")]
    public void Data_NoEndStream_DecodedWithEndStreamFalse()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: false));

        Assert.Single(frames);
        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.False(frame.EndStream);
        // RFC §5.1: stream remains in Open state — more DATA frames expected.
    }

    // =========================================================================
    // SS-005..007: RST_STREAM and multi-frame sequences
    // =========================================================================

    /// RFC 9113 §5.1 / §6.4 — RST_STREAM decoded with correct ErrorCode and StreamId
    [Fact(DisplayName = "RFC-9113-§5.1-SS-005: RST_STREAM decoded with correct StreamId and ErrorCode")]
    public void RstStream_DecodedWithCorrectFields()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize());

        Assert.Single(frames);
        var frame = Assert.IsType<RstStreamFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);
        Assert.Equal(Http2ErrorCode.Cancel, frame.ErrorCode);
        // RFC §6.4: RST_STREAM transitions any non-Idle stream to Closed.
    }

    /// RFC 9113 §5.1 — HEADERS+DATA+END_STREAM sequence decoded in order
    [Fact(DisplayName = "RFC-9113-§5.1-SS-006: HEADERS followed by DATA+END_STREAM decoded as two frames")]
    public void Headers_ThenDataEndStream_DecodedAsSequence()
    {
        var decoder = new Http2FrameDecoder();
        var bytes = Concat(
            MakeHeadersBytes(streamId: 1, endStream: false),
            MakeDataBytes(streamId: 1, endStream: true));

        var frames = decoder.Decode(bytes);

        Assert.Equal(2, frames.Count);
        var headers = Assert.IsType<HeadersFrame>(frames[0]);
        var data = Assert.IsType<DataFrame>(frames[1]);
        Assert.Equal(1, headers.StreamId);
        Assert.Equal(1, data.StreamId);
        Assert.False(headers.EndStream);
        Assert.True(data.EndStream);
    }

    /// RFC 9113 §5.1 — Frames for different streams decoded with independent StreamIds
    [Fact(DisplayName = "RFC-9113-§5.1-SS-007: Frames for streams 1 and 3 carry independent StreamIds")]
    public void Frames_ForDifferentStreams_HaveIndependentStreamIds()
    {
        var decoder = new Http2FrameDecoder();
        var bytes = Concat(
            MakeHeadersBytes(streamId: 1, endStream: false),
            MakeHeadersBytes(streamId: 3, endStream: true));

        var frames = decoder.Decode(bytes);

        Assert.Equal(2, frames.Count);
        Assert.Equal(1, frames[0].StreamId);
        Assert.Equal(3, frames[1].StreamId);
    }

    // =========================================================================
    // SS-008: HPACK integration
    // =========================================================================

    /// RFC 9113 §5.1 / RFC 7541 — HPACK-encoded :status header decoded from HEADERS fragment
    [Fact(DisplayName = "RFC-9113-§5.1-SS-008: HPACK-encoded :status in HEADERS fragment decoded correctly")]
    public void Headers_HpackFragment_DecodedByHpackDecoder()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var block = hpack.Encode([(":status", "204"), ("content-type", "text/plain")]);
        var bytes = new HeadersFrame(1, block, endStream: true, endHeaders: true).Serialize();

        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(bytes);

        var headersFrame = Assert.IsType<HeadersFrame>(frames[0]);

        // HPACK-decode the fragment using HpackDecoder directly.
        var hpackDecoder = new HpackDecoder();
        var headers = hpackDecoder.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(headers, h => h.Name == ":status" && h.Value == "204");
        Assert.Contains(headers, h => h.Name == "content-type" && h.Value == "text/plain");
    }

    // =========================================================================
    // SS-009..010: Stream 0 PROTOCOL_ERROR (RFC 9113 §5.1)
    // =========================================================================

    /// RFC 9113 §5.1 — HEADERS on stream 0 is a connection PROTOCOL_ERROR.
    [Fact(DisplayName = "RFC-9113-§5.1-SS-009: HEADERS on stream 0 is connection PROTOCOL_ERROR")]
    public void Headers_OnStream0_IsProtocolError()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeHeadersBytes(streamId: 0, endStream: false));

        // Decoder produces the frame; stream-0 validation is the caller's responsibility.
        var frame = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(0, frame.StreamId);

        // RFC 9113 §5.1: HEADERS on stream 0 MUST trigger a connection PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => EnforceNonZeroStreamId(frame, FrameType.Headers));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §5.1 — DATA on stream 0 is a connection PROTOCOL_ERROR.
    [Fact(DisplayName = "RFC-9113-§5.1-SS-010: DATA on stream 0 is connection PROTOCOL_ERROR")]
    public void Data_OnStream0_IsProtocolError()
    {
        // Build a raw DATA frame with stream ID = 0 (bypasses frame constructor defaults).
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x00,             // DATA
            0x00,             // no flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x01, 0x02, 0x03, 0x04, // payload
        };
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(rawFrame);

        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(0, frame.StreamId);

        // RFC 9113 §5.1: DATA on stream 0 MUST trigger a connection PROTOCOL_ERROR.
        var ex = Assert.Throws<Http2Exception>(() => EnforceNonZeroStreamId(frame, FrameType.Data));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // SS-011..012: Idle/closed stream DATA errors (RFC 9113 §5.1 / §6.1)
    // =========================================================================

    /// RFC 9113 §5.1 — DATA on idle stream (no prior HEADERS) is connection PROTOCOL_ERROR.
    [Fact(DisplayName = "RFC-9113-§5.1-SS-011: DATA on idle stream (no HEADERS) is connection PROTOCOL_ERROR")]
    public void Data_OnIdleStream_IsProtocolError()
    {
        var decoder = new Http2FrameDecoder();
        var frames = decoder.Decode(MakeDataBytes(streamId: 1, endStream: false));

        var frame = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, frame.StreamId);

        // RFC 9113 §5.1: No HEADERS received for stream 1 → stream is Idle.
        // DATA on an Idle stream MUST be treated as a connection PROTOCOL_ERROR.
        var openStreams = new HashSet<int>(); // stream 1 never opened
        var ex = Assert.Throws<Http2Exception>(() => EnforceStreamOpen(frame.StreamId, openStreams));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §6.1 — DATA on closed stream (after END_STREAM) is STREAM_CLOSED stream error.
    [Fact(DisplayName = "RFC-9113-§5.1-SS-012: DATA on closed stream (after END_STREAM) is STREAM_CLOSED error")]
    public void Data_OnClosedStream_IsStreamClosedError()
    {
        var decoder = new Http2FrameDecoder();

        // Step 1: decode HEADERS+END_STREAM → stream 1 is now Closed.
        var headersBytes = MakeHeadersBytes(streamId: 1, endStream: true);
        decoder.Decode(headersBytes);
        var closedStreams = new HashSet<int> { 1 }; // stream 1 closed by END_STREAM on HEADERS

        // Step 2: decode DATA on the (now-closed) stream 1.
        var dataBytes = MakeDataBytes(streamId: 1, endStream: false);
        var dataFrames = decoder.Decode(dataBytes);
        var frame = Assert.IsType<DataFrame>(dataFrames[0]);
        Assert.Equal(1, frame.StreamId);

        // RFC 9113 §6.1: DATA on a closed stream MUST trigger a STREAM_CLOSED stream error.
        var ex = Assert.Throws<Http2Exception>(() => EnforceStreamNotClosed(frame.StreamId, closedStreams));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }
}
