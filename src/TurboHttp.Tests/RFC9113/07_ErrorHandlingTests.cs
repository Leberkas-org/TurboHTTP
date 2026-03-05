using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 26: Error Mapping &amp; Correct Codes
///
/// RFC 7540 §5.4 distinguishes two error categories:
///
///   §5.4.1 Connection errors — terminate the entire HTTP/2 connection.
///     The endpoint MUST send GOAWAY then close the TCP connection.
///
///   §5.4.2 Stream errors — reset only the affected stream via RST_STREAM.
///     The connection continues; other streams are unaffected.
///
/// This test class verifies that Http2Decoder throws Http2Exception with:
///   - the correct Http2ErrorCode (PROTOCOL_ERROR, FLOW_CONTROL_ERROR,
///     FRAME_SIZE_ERROR, INTERNAL_ERROR, REFUSED_STREAM, CANCEL, STREAM_CLOSED)
///   - the correct Http2ErrorScope (Connection vs Stream)
///   - the correct StreamId for stream-scoped errors
///
/// Test IDs: EM-001..EM-025
/// </summary>
public sealed class Http2ErrorMappingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] BuildRawFrame(byte type, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        // Length (24-bit)
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

    private static byte[] BuildWindowUpdateFrame(int streamId, int increment)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)increment & 0x7FFFFFFF);
        return BuildRawFrame(0x8, 0, streamId, payload);
    }

    private static byte[] BuildSettingsFrame(bool ack = false, params (ushort, uint)[] settings)
    {
        byte flags = ack ? (byte)0x1 : (byte)0x0;
        var payload = new byte[settings.Length * 6];
        for (int i = 0; i < settings.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), settings[i].Item1);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), settings[i].Item2);
        }

        return BuildRawFrame(0x4, flags, 0, payload);
    }

    private static byte[] BuildMinimalStatusHeaderBlock()
    {
        var enc = new HpackEncoder(useHuffman: false);
        return enc.Encode([(":status", "200")]).ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var p in parts) total += p.Length;
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, offset);
            offset += p.Length;
        }

        return result;
    }

    // ── EM-001..EM-004: Http2ErrorScope enum sanity ───────────────────────────

    /// RFC 7540 §5.4 — Http2Exception defaults to Connection scope
    [Fact(DisplayName = "EM-001: Http2Exception defaults to Connection scope")]
    public void Http2Exception_Default_IsConnectionScope()
    {
        var ex = new Http2Exception("test");
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
        Assert.Equal(0, ex.StreamId);
    }

    /// RFC 7540 §5.4 — Http2Exception stream scope sets IsConnectionError=false
    [Fact(DisplayName = "EM-002: Http2Exception stream scope sets IsConnectionError=false")]
    public void Http2Exception_StreamScope_IsConnectionError_False()
    {
        var ex = new Http2Exception("test", Http2ErrorCode.RefusedStream,
            Http2ErrorScope.Stream, streamId: 3);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.False(ex.IsConnectionError);
        Assert.Equal(3, ex.StreamId);
    }

    /// RFC 7540 §5.4 — Http2Exception preserves ErrorCode when scope is set
    [Fact(DisplayName = "EM-003: Http2Exception preserves ErrorCode when scope is set")]
    public void Http2Exception_ErrorCode_PreservedWithScope()
    {
        var ex = new Http2Exception("test", Http2ErrorCode.FlowControlError,
            Http2ErrorScope.Stream, streamId: 7);
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(7, ex.StreamId);
    }

    /// RFC 7540 §5.4 — Http2Exception connection scope has StreamId=0
    [Fact(DisplayName = "EM-004: Http2Exception connection scope has StreamId=0")]
    public void Http2Exception_ConnectionScope_StreamIdIsZero()
    {
        var ex = new Http2Exception("test", Http2ErrorCode.ProtocolError,
            Http2ErrorScope.Connection);
        Assert.Equal(0, ex.StreamId);
        Assert.True(ex.IsConnectionError);
    }

    // ── EM-005..EM-007: Connection errors — PROTOCOL_ERROR ───────────────────

    /// RFC 7540 §5.4 — DATA on stream 0 is connection PROTOCOL ERROR
    [Fact(DisplayName = "EM-005: DATA on stream 0 is connection PROTOCOL_ERROR")]
    public void DataOnStream0_IsConnectionProtocolError()
    {
        var frame = BuildRawFrame(0x0, 0x0, 0, new byte[4]);
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.4 — DATA on idle stream is connection PROTOCOL ERROR
    [Fact(DisplayName = "EM-006: DATA on idle stream is connection PROTOCOL_ERROR")]
    public void DataOnIdleStream_IsConnectionProtocolError()
    {
        var session = new Http2ProtocolSession();
        var frame = BuildDataFrame(1, new byte[4], endStream: false);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.4 — CONTINUATION without preceding HEADERS is connection PROTOCOL ERROR
    [Fact(DisplayName = "EM-007: CONTINUATION without preceding HEADERS is connection PROTOCOL_ERROR")]
    public void ContinuationWithoutHeaders_IsConnectionProtocolError()
    {
        var contFrame = BuildRawFrame(0x9, 0x4, 1, new byte[4]); // CONTINUATION on stream 1
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(contFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // ── EM-008..EM-009: Connection errors — FRAME_SIZE_ERROR ─────────────────

    /// RFC 7540 §5.4 — PING with wrong payload size is connection FRAME SIZE ERROR
    [Fact(DisplayName = "EM-008: PING with wrong payload size is connection FRAME_SIZE_ERROR")]
    public void PingWrongLength_IsConnectionFrameSizeError()
    {
        var frame = BuildRawFrame(0x6, 0x0, 0, new byte[4]); // PING needs 8 bytes
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    /// RFC 7540 §5.4 — SETTINGS with non-multiple-of-6 length is connection FRAME SIZE ERROR
    [Fact(DisplayName = "EM-009: SETTINGS with non-multiple-of-6 length is connection FRAME_SIZE_ERROR")]
    public void SettingsWrongLength_IsConnectionFrameSizeError()
    {
        var frame = BuildRawFrame(0x4, 0x0, 0, new byte[7]); // 7 is not multiple of 6
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // ── EM-010..EM-011: Connection errors — FLOW_CONTROL_ERROR (connection-level)

    /// RFC 7540 §5.4 — DATA exceeding connection receive window is connection FLOW CONTROL ERROR
    [Fact(DisplayName = "EM-010: DATA exceeding connection receive window is connection FLOW_CONTROL_ERROR")]
    public void DataExceedingConnectionWindow_IsConnectionFlowControlError()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(1, headerBlock, endStream: false, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // Set connection window to 0 so next DATA frame will exceed it.
        session.SetConnectionReceiveWindow(0);
        var dataFrame = BuildDataFrame(1, new byte[100], endStream: true);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.4 — WINDOW UPDATE connection overflow is connection FLOW CONTROL ERROR
    [Fact(DisplayName = "EM-011: WINDOW_UPDATE connection overflow is connection FLOW_CONTROL_ERROR")]
    public void WindowUpdateConnectionOverflow_IsConnectionFlowControlError()
    {
        var session = new Http2ProtocolSession();

        // First WINDOW_UPDATE sets connection window near max.
        var wuFrame1 = BuildWindowUpdateFrame(0, 0x7FFF0000);
        session.Process(wuFrame1);

        // Second WINDOW_UPDATE causes overflow (current + increment > 2^31-1).
        var wuFrame2 = BuildWindowUpdateFrame(0, 0x7FFF0000);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(wuFrame2));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
        Assert.Equal(0, ex.StreamId);
    }

    // ── EM-012..EM-013: Stream errors — FLOW_CONTROL_ERROR (stream-level) ────

    /// RFC 7540 §5.4 — DATA exceeding stream receive window is stream FLOW CONTROL ERROR
    [Fact(DisplayName = "EM-012: DATA exceeding stream receive window is stream FLOW_CONTROL_ERROR")]
    public void DataExceedingStreamWindow_IsStreamFlowControlError()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(1, headerBlock, endStream: false, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // Reduce stream window to 0.
        session.SetStreamReceiveWindow(1, 0);
        var dataFrame = BuildDataFrame(1, new byte[100], endStream: true);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.False(ex.IsConnectionError);
        Assert.Equal(1, ex.StreamId);
    }

    /// RFC 7540 §5.4 — Stream FLOW CONTROL ERROR carries the affected stream ID
    [Fact(DisplayName = "EM-013: Stream FLOW_CONTROL_ERROR carries the affected stream ID")]
    public void StreamFlowControlError_CarriesStreamId()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(3, headerBlock, endStream: false, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);
        session.SetStreamReceiveWindow(3, 0);
        var dataFrame = BuildDataFrame(3, new byte[50], endStream: true);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(3, ex.StreamId);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
    }

    /// RFC 7540 §5.4 — WINDOW UPDATE stream overflow is stream FLOW CONTROL ERROR
    [Fact(DisplayName = "EM-014: WINDOW_UPDATE stream overflow is stream FLOW_CONTROL_ERROR")]
    public void WindowUpdateStreamOverflow_IsStreamFlowControlError()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(5, headerBlock, endStream: false, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame);

        // Two large WINDOW_UPDATEs on stream 5 to cause overflow.
        var wu1 = BuildWindowUpdateFrame(5, 0x7FFF0000);
        session.Process(wu1);
        var wu2 = BuildWindowUpdateFrame(5, 0x7FFF0000);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(wu2));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.False(ex.IsConnectionError);
        Assert.Equal(5, ex.StreamId);
    }

    // ── EM-015..EM-016: Stream errors — STREAM_CLOSED ────────────────────────

    /// RFC 7540 §5.4 — DATA on closed stream is stream STREAM CLOSED error
    [Fact(DisplayName = "EM-015: DATA on closed stream is stream STREAM_CLOSED error")]
    public void DataOnClosedStream_IsStreamStreamClosedError()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(1, headerBlock, endStream: true, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame); // stream 1 is now closed

        var dataFrame = BuildDataFrame(1, new byte[4], endStream: true);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.False(ex.IsConnectionError);
        Assert.Equal(1, ex.StreamId);
    }

    /// RFC 7540 §5.4 — STREAM CLOSED stream error carries the closed stream's ID
    [Fact(DisplayName = "EM-016: STREAM_CLOSED stream error carries the closed stream's ID")]
    public void DataOnClosedStream_CarriesClosedStreamId()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(7, headerBlock, endStream: true, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame); // stream 7 is now closed

        var dataFrame = BuildDataFrame(7, new byte[4]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(7, ex.StreamId);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
    }

    // ── EM-017..EM-018: Stream errors — REFUSED_STREAM ───────────────────────

    /// RFC 7540 §5.4 — Exceeding MAX CONCURRENT STREAMS is stream REFUSED STREAM error
    [Fact(DisplayName = "EM-017: Exceeding MAX_CONCURRENT_STREAMS is stream REFUSED_STREAM error")]
    public void ExceedMaxConcurrentStreams_IsStreamRefusedStreamError()
    {
        var session = new Http2ProtocolSession();

        // Set MAX_CONCURRENT_STREAMS = 1 via SETTINGS.
        var settingsPayload = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(settingsPayload, 0x3); // SETTINGS_MAX_CONCURRENT_STREAMS
        BinaryPrimitives.WriteUInt32BigEndian(settingsPayload.AsSpan(2), 1u);
        var settingsFrame = BuildRawFrame(0x4, 0x0, 0, settingsPayload);
        session.Process(settingsFrame);

        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();

        // Open stream 1 (occupies the single slot).
        var headers1 = BuildHeadersFrame(1, headerBlock, endStream: false, endHeaders: true);
        session.Process(headers1);

        // Opening stream 3 exceeds the limit → REFUSED_STREAM.
        var headers3 = BuildHeadersFrame(3, headerBlock, endStream: false, endHeaders: true);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headers3));
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.False(ex.IsConnectionError);
    }

    /// RFC 7540 §5.4 — REFUSED STREAM carries the refused stream's ID
    [Fact(DisplayName = "EM-018: REFUSED_STREAM carries the refused stream's ID")]
    public void RefusedStream_CarriesStreamId()
    {
        var session = new Http2ProtocolSession();

        // Set MAX_CONCURRENT_STREAMS = 1.
        var settingsPayload = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(settingsPayload, 0x3);
        BinaryPrimitives.WriteUInt32BigEndian(settingsPayload.AsSpan(2), 1u);
        var settingsFrame = BuildRawFrame(0x4, 0x0, 0, settingsPayload);
        session.Process(settingsFrame);

        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headers1 = BuildHeadersFrame(1, headerBlock, endStream: false, endHeaders: true);
        session.Process(headers1);

        // Stream 5 gets refused.
        var headers5 = BuildHeadersFrame(5, headerBlock, endStream: false, endHeaders: true);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headers5));
        Assert.Equal(5, ex.StreamId);
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
    }

    // ── EM-019..EM-020: Connection errors — HEADERS on closed stream ──────────

    /// RFC 7540 §5.4 — HEADERS on closed stream is connection STREAM CLOSED error
    [Fact(DisplayName = "EM-019: HEADERS on closed stream is connection STREAM_CLOSED error")]
    public void HeadersOnClosedStream_IsConnectionStreamClosedError()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFrame = BuildHeadersFrame(1, headerBlock, endStream: true, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFrame); // stream 1 is now closed

        // Sending another HEADERS on stream 1 is a connection error per RFC 7540 §6.2.
        var headersAgain = BuildHeadersFrame(1, headerBlock, endStream: true, endHeaders: true);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersAgain));
        Assert.Equal(Http2ErrorCode.StreamClosed, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.4 — HEADERS closed-stream error is connection-scoped (RFC 7540 §6.2)
    [Fact(DisplayName = "EM-020: HEADERS closed-stream error is connection-scoped (RFC 7540 §6.2)")]
    public void HeadersOnClosedStream_ConnectionScope_NotStreamScope()
    {
        var hpackEncoder = new HpackEncoder(useHuffman: false);
        var headerBlock = hpackEncoder.Encode([(":status", "200")]).ToArray();
        var headersFirst = BuildHeadersFrame(3, headerBlock, endStream: true, endHeaders: true);

        var session = new Http2ProtocolSession();
        session.Process(headersFirst); // stream 3 closed

        var headersAgain = BuildHeadersFrame(3, headerBlock, endStream: true, endHeaders: true);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headersAgain));

        // Must be Connection scope (not Stream) — the whole connection must be torn down.
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
        Assert.NotEqual(Http2ErrorScope.Stream, ex.Scope);
    }

    // ── EM-021..EM-023: Correct error codes for specific frame violations ─────

    /// RFC 7540 §5.4 — RST STREAM with wrong payload length is connection FRAME SIZE ERROR
    [Fact(DisplayName = "EM-021: RST_STREAM with wrong payload length is connection FRAME_SIZE_ERROR")]
    public void RstStreamWrongLength_IsConnectionFrameSizeError()
    {
        // RST_STREAM must be exactly 4 bytes; send 3 bytes.
        var session = new Http2ProtocolSession();
        var frame = BuildRawFrame(0x3, 0x0, 1, new byte[3]);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    /// RFC 7540 §5.4 — WINDOW UPDATE with increment=0 is PROTOCOL ERROR
    [Fact(DisplayName = "EM-022: WINDOW_UPDATE with increment=0 is PROTOCOL_ERROR")]
    public void WindowUpdateZeroIncrement_IsProtocolError()
    {
        var frame = BuildWindowUpdateFrame(0, 0);
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 7540 §5.4 — SETTINGS ACK with non-empty payload is FRAME SIZE ERROR
    [Fact(DisplayName = "EM-023: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR")]
    public void SettingsAckWithPayload_IsFrameSizeError()
    {
        var frame = BuildRawFrame(0x4, 0x1, 0, new byte[6]); // ACK with payload
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Connection, ex.Scope);
    }

    // ── EM-024..EM-025: Scope symmetry and future-proofing ───────────────────

    /// RFC 7540 §5.4 — Stream and connection errors are mutually exclusive
    [Fact(DisplayName = "EM-024: Stream and connection errors are mutually exclusive")]
    public void Http2Exception_Scope_IsMutuallyExclusive()
    {
        var connEx = new Http2Exception("conn", Http2ErrorCode.ProtocolError,
            Http2ErrorScope.Connection);
        var strmEx = new Http2Exception("strm", Http2ErrorCode.StreamClosed,
            Http2ErrorScope.Stream, 3);

        Assert.True(connEx.IsConnectionError);
        Assert.False(strmEx.IsConnectionError);
        Assert.NotEqual(connEx.Scope, strmEx.Scope);
    }

    /// RFC 7540 §5.4 — Stream-level FLOW CONTROL ERROR and connection-level have different scopes
    [Fact(DisplayName = "EM-025: Stream-level FLOW_CONTROL_ERROR and connection-level have different scopes")]
    public void StreamAndConnectionFlowControlError_HaveDifferentScopes()
    {
        // Stream scope
        var streamEx = new Http2Exception("strm fc",
            Http2ErrorCode.FlowControlError, Http2ErrorScope.Stream, 9);

        // Connection scope (default)
        var connEx = new Http2Exception("conn fc", Http2ErrorCode.FlowControlError);

        Assert.Equal(Http2ErrorScope.Stream, streamEx.Scope);
        Assert.Equal(Http2ErrorScope.Connection, connEx.Scope);
        Assert.Equal(9, streamEx.StreamId);
        Assert.Equal(0, connEx.StreamId);
    }
}
