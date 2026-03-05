using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 9113 §5.1 — HTTP/2 Stream States (Phase 5-6: Full Stream Lifecycle).
///
/// Covers:
///   - Lifecycle state transitions: idle → open → closed (RFC 9113 §5.1)
///   - AUTO-CLOSE on END_STREAM in HEADERS and DATA frames
///   - Rejection of frames that violate stream state machine transitions
///   - DATA before HEADERS on idle stream → PROTOCOL_ERROR (RFC 9113 §5.1)
///   - DATA on closed stream → STREAM_CLOSED error (RFC 9113 §6.1)
///   - HEADERS on closed stream → PROTOCOL_ERROR (RFC 9113 §5.1)
///   - RST_STREAM closes stream (RFC 9113 §6.4)
///   - GetStreamLifecycleState() accessor for state machine inspection
/// </summary>
public sealed class Http2StreamLifecycleTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static byte[] MakeResponseHeadersFrame(int streamId, bool endStream = false, bool endHeaders = true)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders).Serialize();
    }

    private static byte[] MakeResponseHeadersFrameStatus(int streamId, int status, bool endStream = false)
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", status.ToString())]);
        return new HeadersFrame(streamId, headerBlock, endStream, endHeaders: true).Serialize();
    }

    private static byte[] MakeDataFrame(int streamId, bool endStream, byte[]? body = null)
    {
        return new DataFrame(streamId, body ?? "data"u8.ToArray(), endStream).Serialize();
    }

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

    // =========================================================================
    // SS-001..005: Initial / basic lifecycle state transitions
    // =========================================================================

    /// RFC 9113 §5.1 — Unknown stream ID reports Idle state
    [Fact(DisplayName = "RFC9113-5.1-SS-001: Unknown stream ID reports Idle state")]
    public void GetLifecycleState_UnknownStream_ReturnsIdle()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(99));
    }

    /// RFC 9113 §5.1 — HEADERS frame (no END_STREAM) moves stream from Idle to Open
    [Fact(DisplayName = "RFC9113-5.1-SS-002: HEADERS frame (no END_STREAM) moves stream from Idle to Open")]
    public void Headers_NoEndStream_StreamBecomesOpen()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));
    }

    /// RFC 9113 §5.1 — HEADERS+END_STREAM moves stream directly to Closed
    [Fact(DisplayName = "RFC9113-5.1-SS-003: HEADERS+END_STREAM moves stream directly to Closed")]
    public void Headers_WithEndStream_StreamBecomesClosed()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    /// RFC 9113 §5.1 — DATA+END_STREAM after HEADERS closes the stream
    [Fact(DisplayName = "RFC9113-5.1-SS-004: DATA+END_STREAM after HEADERS closes the stream")]
    public void Data_WithEndStream_AfterHeaders_StreamBecomesClosed()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var data = MakeDataFrame(streamId: 1, endStream: true);

        session.Process(Concat(headers, data));

        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    /// RFC 9113 §5.1 — RST_STREAM closes the stream
    [Fact(DisplayName = "RFC9113-5.1-SS-005: RST_STREAM closes the stream")]
    public void RstStream_MovesStream_ToClosed()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        session.Process(headers);
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst);
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    // =========================================================================
    // SS-006..008: Reject invalid frame per state
    // =========================================================================

    /// RFC 9113 §5.1 — DATA on idle stream (no HEADERS) is a connection PROTOCOL_ERROR.
    [Fact(DisplayName = "RFC9113-5.1-SS-006: DATA on idle stream (no HEADERS) is a connection error")]
    public void Data_OnIdleStream_ThrowsConnectionError()
    {
        var session = new Http2ProtocolSession();
        // Send DATA on stream 1 without any preceding HEADERS.
        var data = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(data));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §5.1 — DATA on closed stream is STREAM_CLOSED error
    [Fact(DisplayName = "RFC9113-5.1-SS-007: DATA on closed stream is STREAM_CLOSED error")]
    public void Data_OnClosedStream_ThrowsStreamClosed()
    {
        var session = new Http2ProtocolSession();
        // Close stream via HEADERS+END_STREAM.
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));

        // Now send DATA on the closed stream.
        var data = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(data));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }

    /// RFC 9113 §5.1 — HEADERS on closed stream is connection error STREAM_CLOSED (RFC 7540 §6.2)
    [Fact(DisplayName = "RFC9113-5.1-SS-008: HEADERS on closed stream is STREAM_CLOSED error (RFC 7540 §6.2)")]
    public void Headers_OnClosedStream_ThrowsStreamClosed()
    {
        var session = new Http2ProtocolSession();
        // Close stream via HEADERS+END_STREAM.
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));

        // RFC 7540 §6.2: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
        var headers2 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headers2));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // =========================================================================
    // SS-009..012: Auto-close on END_STREAM (MUST)
    // =========================================================================

    /// RFC 9113 §5.1 — Auto-close: HEADERS+END_STREAM produces response immediately
    [Fact(DisplayName = "RFC9113-5.1-SS-009: Auto-close: HEADERS+END_STREAM produces response immediately")]
    public void AutoClose_HeadersEndStream_ProducesResponse()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrameStatus(streamId: 1, status: 204, endStream: true));

        Assert.Single(session.Responses);
        Assert.Equal(204, (int)session.Responses[0].Response.StatusCode);
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    /// RFC 9113 §5.1 — Auto-close: DATA+END_STREAM closes stream
    /// Note: Http2ProtocolSession does not accumulate response body; status and stream state are verified.
    [Fact(DisplayName = "RFC9113-5.1-SS-010: Auto-close: DATA+END_STREAM closes stream")]
    public void AutoClose_DataEndStream_ClosesStream()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var data = MakeDataFrame(streamId: 1, endStream: true, body: "hello"u8.ToArray());

        session.Process(Concat(headers, data));

        Assert.Single(session.Responses);
        Assert.Equal(200, (int)session.Responses[0].Response.StatusCode);
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    /// RFC 9113 §5.1 — Auto-close: multiple streams closed independently via END_STREAM
    [Fact(DisplayName = "RFC9113-5.1-SS-011: Auto-close: multiple streams closed independently via END_STREAM")]
    public void AutoClose_MultipleStreams_EachClosedIndependently()
    {
        var session = new Http2ProtocolSession();

        // Stream 1: HEADERS + DATA+END_STREAM.
        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var d1 = MakeDataFrame(streamId: 1, endStream: true);

        // Stream 3: HEADERS+END_STREAM.
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: true);

        session.Process(Concat(h1, d1, h3));

        Assert.Equal(2, session.Responses.Count);
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(3));
    }

    /// RFC 9113 §5.1 — Stream open while DATA accumulates, then closed on END_STREAM
    [Fact(DisplayName = "RFC9113-5.1-SS-012: Stream open while DATA accumulates, then closed on END_STREAM")]
    public void StreamIsOpen_WhileDataAccumulates_ClosedOnEndStream()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        session.Process(headers);
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));

        var data1 = MakeDataFrame(streamId: 1, endStream: false, body: "chunk1"u8.ToArray());
        session.Process(data1);
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));

        var data2 = MakeDataFrame(streamId: 1, endStream: true, body: "chunk2"u8.ToArray());
        session.Process(data2);
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    // =========================================================================
    // SS-013..015: Multiple independent streams
    // =========================================================================

    /// RFC 9113 §5.1 — Different streams have independent lifecycle states
    [Fact(DisplayName = "RFC9113-5.1-SS-013: Different streams have independent lifecycle states")]
    public void MultipleStreams_IndependentLifecycleStates()
    {
        var session = new Http2ProtocolSession();

        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: true);

        session.Process(Concat(h1, h3));

        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(3));
        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(5)); // never seen
    }

    /// RFC 9113 §5.1 — Closing one stream does not affect other open streams
    [Fact(DisplayName = "RFC9113-5.1-SS-014: Closing one stream does not affect other open streams")]
    public void CloseOneStream_OtherRemainsOpen()
    {
        var session = new Http2ProtocolSession();

        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: false);
        session.Process(Concat(h1, h3));

        // Close stream 1 via DATA+END_STREAM.
        var d1 = MakeDataFrame(streamId: 1, endStream: true);
        session.Process(d1);

        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(3));
    }

    /// RFC 9113 §5.1 — RST_STREAM on open stream does not affect other streams
    [Fact(DisplayName = "RFC9113-5.1-SS-015: RST_STREAM on open stream does not affect other streams")]
    public void RstStream_OnOneStream_DoesNotAffectOthers()
    {
        var session = new Http2ProtocolSession();

        var h1 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var h3 = MakeResponseHeadersFrame(streamId: 3, endStream: false);
        session.Process(Concat(h1, h3));

        var rst1 = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst1);

        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(3));
    }

    // =========================================================================
    // SS-016..018: Post-RST_STREAM frame rejection
    // =========================================================================

    /// RFC 9113 §5.1 — DATA after RST_STREAM on same stream throws STREAM_CLOSED
    [Fact(DisplayName = "RFC9113-5.1-SS-016: DATA after RST_STREAM on same stream throws STREAM_CLOSED")]
    public void Data_AfterRstStream_ThrowsStreamClosed()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst);

        var data = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(data));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }

    /// RFC 9113 §5.1 — HEADERS after RST_STREAM on same stream is STREAM_CLOSED error (RFC 7540 §6.2)
    /// Note: Http2ProtocolSession uses stream-scope for this error (vs connection-scope in Http2Decoder).
    [Fact(DisplayName = "RFC9113-5.1-SS-017: HEADERS after RST_STREAM on same stream is STREAM_CLOSED error (RFC 7540 §6.2)")]
    public void Headers_AfterRstStream_ThrowsStreamClosed()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst);

        // RFC 7540 §6.2: HEADERS on a closed stream is a connection error of type STREAM_CLOSED.
        var headers2 = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headers2));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    /// RFC 9113 §5.1 — DATA on stream with only DATA+END_STREAM received (half-closed-remote) throws STREAM_CLOSED
    [Fact(DisplayName = "RFC9113-5.1-SS-018: DATA on stream with only DATA+END_STREAM received (half-closed-remote) throws STREAM_CLOSED")]
    public void Data_OnHalfClosedRemoteStream_ViaData_ThrowsStreamClosed()
    {
        var session = new Http2ProtocolSession();
        var headers = MakeResponseHeadersFrame(streamId: 1, endStream: false);
        var dataEnd = MakeDataFrame(streamId: 1, endStream: true);
        session.Process(Concat(headers, dataEnd));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));

        // Stream is now closed; any subsequent DATA frame must be rejected.
        var extra = MakeDataFrame(streamId: 1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(extra));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }

    // =========================================================================
    // SS-019..022: Reset behaviour
    // =========================================================================

    /// RFC 9113 §5.1 — Creating a new session clears all lifecycle states back to Idle
    [Fact(DisplayName = "RFC9113-5.1-SS-019: New session clears all lifecycle states back to Idle")]
    public void Reset_ClearsAllLifecycleStates()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        session.Process(MakeResponseHeadersFrame(streamId: 3, endStream: true));

        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(3));

        session = new Http2ProtocolSession();

        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(3));
    }

    /// RFC 9113 §5.1 — After new session, stream IDs can be reused (back to Idle)
    [Fact(DisplayName = "RFC9113-5.1-SS-020: After new session, stream IDs can be reused (back to Idle)")]
    public void AfterReset_StreamIdsCanBeReused()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: true));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));

        session = new Http2ProtocolSession();
        // Now stream 1 is idle again; HEADERS should work without PROTOCOL_ERROR.
        var ex = Record.Exception(() => session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false)));
        Assert.Null(ex);
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));
    }

    // =========================================================================
    // SS-021..025: Edge cases
    // =========================================================================

    /// RFC 9113 §5.1 — DATA on stream 0 is PROTOCOL_ERROR (stream 0 is for control only)
    [Fact(DisplayName = "RFC9113-5.1-SS-021: DATA on stream 0 is PROTOCOL_ERROR (stream 0 is for control only)")]
    public void Data_OnStream0_ThrowsProtocolError()
    {
        var session = new Http2ProtocolSession();
        var frame = new byte[]
        {
            0x00, 0x00, 0x04, // length = 4
            0x00,             // DATA
            0x00,             // no flags
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x01, 0x02, 0x03, 0x04
        };
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §5.1 — HEADERS on stream 0 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-5.1-SS-022: HEADERS on stream 0 is PROTOCOL_ERROR")]
    public void Headers_OnStream0_ThrowsProtocolError()
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", "200")]);
        var frame = new HeadersFrame(0, headerBlock, endStream: false, endHeaders: true).Serialize();

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §5.1 — DATA on a different idle stream than already-open streams is a connection PROTOCOL_ERROR.
    [Fact(DisplayName = "RFC9113-5.1-SS-023: DATA on a different idle stream than already-open streams is a connection error")]
    public void Data_OnDifferentIdleStream_ThrowsConnectionError()
    {
        var session = new Http2ProtocolSession();
        // Open stream 1.
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));

        // Send DATA on stream 3 which was never opened.
        var data = MakeDataFrame(streamId: 3, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(data));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §5.1 — Five streams each with distinct lifecycle states are tracked correctly
    [Fact(DisplayName = "RFC9113-5.1-SS-024: Five streams each with distinct lifecycle states are tracked correctly")]
    public void FiveStreams_DistinctLifecycles_AllTrackedCorrectly()
    {
        var session = new Http2ProtocolSession();

        // Stream 1: HEADERS+END_STREAM → Closed
        session.Process(MakeResponseHeadersFrame(1, endStream: true));
        // Stream 3: HEADERS only → Open
        session.Process(MakeResponseHeadersFrame(3, endStream: false));
        // Stream 5: HEADERS then DATA+END_STREAM → Closed
        var h5 = MakeResponseHeadersFrame(5, endStream: false);
        var d5 = MakeDataFrame(5, endStream: true);
        session.Process(Concat(h5, d5));
        // Stream 7: HEADERS then RST_STREAM → Closed
        session.Process(MakeResponseHeadersFrame(7, endStream: false));
        session.Process(new RstStreamFrame(7, Http2ErrorCode.Cancel).Serialize());
        // Stream 9: never seen → Idle

        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(3));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(5));
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(7));
        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(9));
    }

    /// RFC 9113 §5.1 — DATA on a known closed stream reports STREAM_CLOSED after RST_STREAM
    [Fact(DisplayName = "RFC9113-5.1-SS-025: DATA on a known closed stream reports STREAM_CLOSED after RST_STREAM")]
    public void Data_OnRstStreamClosedStream_ThrowsStreamClosed()
    {
        var session = new Http2ProtocolSession();
        session.Process(MakeResponseHeadersFrame(streamId: 1, endStream: false));

        // Server sends RST_STREAM to cancel stream 1.
        var rst = new RstStreamFrame(1, Http2ErrorCode.Cancel).Serialize();
        session.Process(rst);
        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));

        // Any further DATA must throw STREAM_CLOSED.
        var data = MakeDataFrame(1, endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(data));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }
}
