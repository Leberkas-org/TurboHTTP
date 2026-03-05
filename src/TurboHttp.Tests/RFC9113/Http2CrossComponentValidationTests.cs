using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 27-28: Cross-Component Validation
///
/// Ensures correct interaction between Http2ProtocolSession sub-systems:
///
///   CC-001..005 — HPACK failure → connection error (RFC 9113 §4.3)
///       A decompression failure MUST be treated as a connection error of type
///       COMPRESSION_ERROR, regardless of which stream triggered it.
///
///   CC-006..010 — Flow control independent from header decoding
///       Flow control windows are tracked and enforced independently from HPACK.
///       Neither system should corrupt the other's state on failure.
///
///   CC-011..014 — Stream cleanup on RST_STREAM (RFC 7540 §6.4)
///       RST_STREAM must decrement active stream count, mark lifecycle Closed,
///       and prevent further DATA on the reset stream.
///
///   CC-015..018 — GOAWAY stops new stream creation (RFC 7540 §6.8)
///       After receiving GOAWAY, new HEADERS must be rejected.
///       Streams ≤ lastStreamId are still processed; > lastStreamId are cleaned up.
///
///   CC-019..020 — No header injection via HPACK compression (RFC 9113 §8.2)
///       Invalid HPACK-encoded data is rejected before it can influence stream state.
///       HPACK table corruption cannot bypass header validation.
///
/// Test IDs: CC-001..CC-020
/// </summary>
public sealed class Http2CrossComponentValidationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] BuildRawFrame(byte type, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFF);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildHeadersFrame(int streamId, byte[] headerBlock,
        bool endStream = false, bool endHeaders = true)
    {
        byte flags = 0;
        if (endStream)
        {
            flags |= 0x1;
        }

        if (endHeaders)
        {
            flags |= 0x4;
        }

        return BuildRawFrame(0x1, flags, streamId, headerBlock);
    }

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = false)
    {
        byte flags = endStream ? (byte)0x1 : (byte)0x0;
        return BuildRawFrame(0x0, flags, streamId, data);
    }

    private static byte[] BuildRstStreamFrame(int streamId, Http2ErrorCode error)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)error);
        return BuildRawFrame(0x3, 0, streamId, payload);
    }

    private static byte[] BuildGoAwayFrame(int lastStreamId, Http2ErrorCode error = Http2ErrorCode.NoError)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)lastStreamId & 0x7FFFFFFF);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)error);
        return BuildRawFrame(0x7, 0, 0, payload);
    }

    private static byte[] BuildWindowUpdateFrame(int streamId, int increment)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)increment & 0x7FFFFFFF);
        return BuildRawFrame(0x8, 0, streamId, payload);
    }

    private static byte[] ValidStatusHeaderBlock()
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode([(":status", "200")]).ToArray();
    }

    // ── CC-001..005: HPACK failure → connection error ─────────────────────────

    [Fact(DisplayName = "CC-001: Malformed HPACK byte sequence → CompressionError connection error")]
    public void MalformedHpackBytes_ThrowsCompressionError()
    {
        // 0x80 = indexed representation with index 0 (reserved → HpackException)
        var corruptHpack = new byte[] { 0x80 };
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
    }

    [Fact(DisplayName = "CC-002: Out-of-range dynamic index in HPACK block → CompressionError connection error")]
    public void OutOfRangeDynamicIndex_ThrowsCompressionError()
    {
        // Dynamic table is empty; reference dynamic index 1 → HpackException
        // Encoded as 0xFF 0x01 (indexed, index = 63 + 64 = >62 static entries)
        var corruptHpack = new byte[] { 0xFF, 0x3F }; // index = 127 (out of range)
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "CC-003: HPACK CompressionError has IsConnectionError = true (not stream error)")]
    public void HpackCompressionError_IsConnectionLevel_NotStreamLevel()
    {
        var corruptHpack = new byte[] { 0x80 }; // index 0 is reserved → HpackException
        var headersFrame = BuildHeadersFrame(3, corruptHpack);

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));

        // Must NOT be a stream error — RFC 9113 §4.3 mandates connection scope
        Assert.NotEqual(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.Equal(0, ex.StreamId); // StreamId = 0 for connection errors
    }

    [Fact(DisplayName = "CC-004: HPACK empty header name → CompressionError connection error")]
    public void HpackEmptyHeaderName_ThrowsCompressionError()
    {
        // Literal without indexing (0x00), name index=0 (new name), name length = 0 (empty)
        // RFC 7541 §7.2: empty header name is a protocol violation
        var corruptHpack = new byte[] { 0x00, 0x00 }; // literal, new name, empty string
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "CC-005: HPACK failure on stream 5 is still a connection error (affects all streams)")]
    public void HpackFailureOnAnyStream_IsConnectionError()
    {
        // Open stream 1 successfully first
        var session = new Http2ProtocolSession();
        var goodHeaders = BuildHeadersFrame(1, ValidStatusHeaderBlock(), endHeaders: true);
        session.Process(goodHeaders);

        // Now trigger HPACK failure on stream 5
        var corruptHpack = new byte[] { 0x80 };
        var badHeadersFrame = BuildHeadersFrame(5, corruptHpack);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(badHeadersFrame));

        // The HPACK error is connection-level even though stream 1 is fine
        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // ── CC-006..010: Flow control independent from header decoding ─────────────

    [Fact(DisplayName = "CC-006: Connection window tracked independently from HPACK state")]
    public void ConnectionWindow_TrackedIndependentlyFromHpack()
    {
        var session = new Http2ProtocolSession();

        // Open stream 1
        var headers = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers);

        var initialWindow = session.ConnectionReceiveWindow;
        Assert.Equal(65535, initialWindow);

        // Send 100 bytes of DATA — window should decrease
        var data = BuildDataFrame(1, new byte[100]);
        session.Process(data);

        Assert.Equal(65535 - 100, session.ConnectionReceiveWindow);
    }

    [Fact(DisplayName = "CC-007: Stream windows are independent across multiple streams")]
    public void StreamWindows_AreIndependent_AcrossStreams()
    {
        var session = new Http2ProtocolSession();

        // Open streams 1 and 3
        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        var headers3 = BuildHeadersFrame(3, ValidStatusHeaderBlock());
        session.Process(headers1);
        session.Process(headers3);

        // Reduce stream 1 window to 0
        session.SetStreamReceiveWindow(1, 0);

        // Stream 3 window should still be 65535
        Assert.Equal(65535, session.GetStreamReceiveWindow(3));

        // Sending DATA on stream 3 should succeed (stream 3's window is intact)
        var data3 = BuildDataFrame(3, new byte[50]);
        session.Process(data3); // should not throw
        Assert.Equal(65535 - 50, session.GetStreamReceiveWindow(3));
    }

    [Fact(DisplayName = "CC-008: Flow control error on stream 1 does not corrupt stream 3 window")]
    public void FlowControlErrorOnStream1_DoesNotCorruptStream3()
    {
        var session = new Http2ProtocolSession();

        // Open both streams
        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        var headers3 = BuildHeadersFrame(3, ValidStatusHeaderBlock());
        session.Process(headers1);
        session.Process(headers3);

        // Force stream 1 to have a window of 0 → will throw on DATA
        session.SetStreamReceiveWindow(1, 0);

        // DATA on stream 1 will fail with stream FLOW_CONTROL_ERROR
        var data1 = BuildDataFrame(1, new byte[10]);
        Assert.Throws<Http2Exception>(() => session.Process(data1));

        // Stream 3's receive window should be unchanged (65535)
        Assert.Equal(65535, session.GetStreamReceiveWindow(3));
    }

    [Fact(DisplayName = "CC-009: WINDOW_UPDATE on stream 1 does not affect stream 3 send window")]
    public void WindowUpdateOnStream1_DoesNotAffectStream3()
    {
        var session = new Http2ProtocolSession();

        // Open stream 1 (stream 3 is idle, send window defaults to initial)
        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers1);

        var initialStream3Window = session.GetStreamSendWindow(3);

        // WINDOW_UPDATE on stream 1 (increments stream 1's send window)
        var wu1 = BuildWindowUpdateFrame(1, 1000);
        session.Process(wu1);

        // Stream 3 send window should be unchanged
        Assert.Equal(initialStream3Window, session.GetStreamSendWindow(3));
        Assert.Equal(65535 + 1000, session.GetStreamSendWindow(1));
    }

    [Fact(DisplayName = "CC-010: Connection WINDOW_UPDATE increases only the connection send window")]
    public void ConnectionWindowUpdate_IncreasesOnlyConnectionSendWindow()
    {
        var session = new Http2ProtocolSession();

        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers1);

        var initialStream1 = session.GetStreamSendWindow(1);
        var initialConn = session.ConnectionSendWindow;

        // WINDOW_UPDATE on stream 0 (connection level)
        var wu = BuildWindowUpdateFrame(0, 5000);
        session.Process(wu);

        Assert.Equal(initialConn + 5000, session.ConnectionSendWindow);
        Assert.Equal(initialStream1, session.GetStreamSendWindow(1)); // stream unchanged
    }

    // ── CC-011..014: Stream cleanup on RST_STREAM ──────────────────────────────

    [Fact(DisplayName = "CC-011: RST_STREAM decrements active stream count")]
    public void RstStream_DecrementsActiveStreamCount()
    {
        var session = new Http2ProtocolSession();

        // Open 2 streams
        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        var headers3 = BuildHeadersFrame(3, ValidStatusHeaderBlock());
        session.Process(headers1);
        session.Process(headers3);

        Assert.Equal(2, session.ActiveStreamCount);

        // RST_STREAM on stream 1
        var rst = BuildRstStreamFrame(1, Http2ErrorCode.Cancel);
        session.Process(rst);

        Assert.Equal(1, session.ActiveStreamCount);
    }

    [Fact(DisplayName = "CC-012: RST_STREAM marks stream lifecycle as Closed")]
    public void RstStream_MarksStreamLifecycleAsClosed()
    {
        var session = new Http2ProtocolSession();

        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers1);

        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));

        var rst = BuildRstStreamFrame(1, Http2ErrorCode.Cancel);
        session.Process(rst);

        Assert.Equal(Http2StreamLifecycleState.Closed, session.GetStreamState(1));
    }

    [Fact(DisplayName = "CC-013: After RST_STREAM, subsequent DATA on that stream is stream STREAM_CLOSED error")]
    public void AfterRstStream_DataOnResetStream_IsStreamClosedError()
    {
        var session = new Http2ProtocolSession();

        // Open stream 1, then reset it
        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers1);

        var rst = BuildRstStreamFrame(1, Http2ErrorCode.Cancel);
        session.Process(rst);

        // DATA on reset stream → STREAM_CLOSED
        var data = BuildDataFrame(1, new byte[10]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(data));

        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }

    [Fact(DisplayName = "CC-014: RST_STREAM result carries the error code from the frame payload")]
    public void RstStream_Result_CarriesErrorCode()
    {
        var session = new Http2ProtocolSession();

        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers1);

        var rst = BuildRstStreamFrame(1, Http2ErrorCode.InternalError);
        session.Process(rst);

        Assert.Single(session.RstStreams);
        Assert.Equal(1, session.RstStreams[0].StreamId);
        Assert.Equal(Http2ErrorCode.InternalError, session.RstStreams[0].Error);
    }

    // ── CC-015..018: GOAWAY stops new stream creation ──────────────────────────

    [Fact(DisplayName = "CC-015: After receiving GOAWAY, IsGoingAway = true")]
    public void AfterGoAway_IsGoingAway_IsTrue()
    {
        var session = new Http2ProtocolSession();
        Assert.False(session.IsGoingAway);

        var goAway = BuildGoAwayFrame(0, Http2ErrorCode.NoError);
        session.Process(goAway);

        Assert.True(session.IsGoingAway);
    }

    [Fact(DisplayName = "CC-016: New HEADERS after GOAWAY is rejected with PROTOCOL_ERROR")]
    public void NewHeadersAfterGoAway_IsRejected()
    {
        var session = new Http2ProtocolSession();

        var goAway = BuildGoAwayFrame(0, Http2ErrorCode.NoError);
        session.Process(goAway);

        // Stream 1 was not open before GOAWAY, so it is new → rejected
        var headers = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headers));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    [Fact(DisplayName = "CC-017: GOAWAY sets GoAwayLastStreamId correctly")]
    public void GoAway_SetsLastStreamId()
    {
        var session = new Http2ProtocolSession();

        // Open stream 1 first (lastStreamId = 1 in GOAWAY)
        var headers1 = BuildHeadersFrame(1, ValidStatusHeaderBlock());
        session.Process(headers1);

        var goAway = BuildGoAwayFrame(1, Http2ErrorCode.NoError);
        session.Process(goAway);

        Assert.Equal(1, session.GoAwayLastStreamId);
    }

    [Fact(DisplayName = "CC-018: GOAWAY cleans up streams with ID > lastStreamId")]
    public void GoAway_CleansUpStreamsAboveLastStreamId()
    {
        var session = new Http2ProtocolSession();

        // Open streams 1, 3, 5
        session.Process(BuildHeadersFrame(1, ValidStatusHeaderBlock()));
        session.Process(BuildHeadersFrame(3, ValidStatusHeaderBlock()));
        session.Process(BuildHeadersFrame(5, ValidStatusHeaderBlock()));

        Assert.Equal(3, session.ActiveStreamCount);

        // GOAWAY with lastStreamId = 1 → streams 3 and 5 should be cleaned up
        var goAway = BuildGoAwayFrame(1, Http2ErrorCode.NoError);
        session.Process(goAway);

        // Only stream 1 (≤ lastStreamId) should remain active
        Assert.Equal(1, session.ActiveStreamCount);
    }

    // ── CC-019..020: No header injection via HPACK compression ────────────────

    [Fact(DisplayName = "CC-019: Invalid HPACK index cannot inject arbitrary headers (CompressionError)")]
    public void InvalidHpackIndex_CannotInjectHeaders_ThrowsCompressionError()
    {
        // Craft a HPACK block that references index 0 (reserved) — no header can be injected
        // because the HpackException is caught and wrapped as CompressionError before
        // any header is added to the response.
        var corruptHpack = new byte[] { 0x80 }; // indexed, index=0 (reserved)
        var headersFrame = BuildHeadersFrame(1, corruptHpack);

        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));

        Assert.Equal(Http2ErrorCode.CompressionError, ex.ErrorCode);
        // Verify: no response was produced (the exception precedes response building)
        Assert.True(ex.IsConnectionError);
    }

    [Fact(DisplayName = "CC-020: HPACK-encoded uppercase header name is caught by response validation")]
    public void HpackEncodedUppercaseHeaderName_IsRejectedByValidation()
    {
        // Build a valid HPACK block that encodes an uppercase header name.
        // This bypasses HPACK (legitimate encoding) but ValidateResponseHeaders catches it.
        // Literal without indexing (0x00), name index=0 (new literal name)
        // Name: "X-UPPER" (uppercase), Value: "test"
        // RFC 9113 §8.2: all header names MUST be lowercase.
        var hpack = new List<byte>();
        // Literal without indexing (bit pattern 0000xxxx = 0x00), prefix 4 bits, index=0
        hpack.Add(0x00);
        // Name: literal string "X-UPPER"
        var upperName = "X-UPPER"u8.ToArray();
        hpack.Add((byte)upperName.Length); // not Huffman
        hpack.AddRange(upperName);
        // Value: "test"
        var val = "test"u8.ToArray();
        hpack.Add((byte)val.Length);
        hpack.AddRange(val);

        // But we also need ":status" to pass ValidateResponseHeaders.
        // Build the correct :status first (indexed representation, index 8 = :status: 200)
        var combined = new List<byte> { 0x88 }; // indexed :status: 200
        combined.AddRange(hpack);

        var headersFrame = BuildHeadersFrame(1, combined.ToArray());
        var session = new Http2ProtocolSession();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersFrame));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }
}
