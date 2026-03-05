using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Phase 10-11: HTTP/2 Flow Control Engine
/// RFC 7540 §5.2 — Flow Control
/// RFC 7540 §6.9 — WINDOW_UPDATE Frame
/// RFC 7540 §6.9.1 — Flow-Control Window
/// RFC 7540 §6.9.2 — Initial Flow-Control Window Size
/// </summary>
public sealed class Http2FlowControlTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] BuildRawFrame(byte frameType, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = (byte)(payload.Length >> 16);
        frame[1] = (byte)(payload.Length >> 8);
        frame[2] = (byte)payload.Length;
        frame[3] = frameType;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);
        payload.CopyTo(frame, 9);
        return frame;
    }

    private static Http2ProtocolSession OpenStreamWithHeaders(int streamId)
    {
        var session = new Http2ProtocolSession();
        // Minimal HPACK: 0x88 = indexed :status: 200 (static table index 8).
        var headersPayload = new byte[] { 0x88 };
        // END_HEADERS flag (0x4) — no END_STREAM so the stream stays open.
        var headersFrame = BuildRawFrame(0x1, 0x04, streamId, headersPayload);
        session.Process(headersFrame);
        return session;
    }

    private static byte[] BuildWindowUpdate(int streamId, int increment)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)increment & 0x7FFFFFFFu);
        return BuildRawFrame(0x8, 0x0, streamId, payload);
    }

    private static byte[] BuildDataFrame(int streamId, byte[] data, bool endStream = false)
    {
        return BuildRawFrame(0x0, endStream ? (byte)0x01 : (byte)0x00, streamId, data);
    }

    private static byte[] BuildSettingsFrame(List<(SettingsParameter Param, uint Value)> settings)
    {
        var payload = new byte[settings.Count * 6];
        for (var i = 0; i < settings.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(i * 6), (ushort)settings[i].Param);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(i * 6 + 2), settings[i].Value);
        }

        return BuildRawFrame(0x4, 0x0, 0, payload);
    }

    // ── FC-001..005: Connection Receive Window Tracking ───────────────────────

    /// RFC 7540 §5.2 — Initial connection receive window is 65535
    [Fact(DisplayName = "FC-001: Initial connection receive window is 65535")]
    public void Should_HaveDefaultConnectionReceiveWindow_When_DecoderCreated()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(65535, session.ConnectionReceiveWindow);
    }

    /// RFC 7540 §5.2 — Connection receive window decrements after DATA received
    [Fact(DisplayName = "FC-002: Connection receive window decrements after DATA received")]
    public void Should_DecrementConnectionReceiveWindow_When_DataFrameReceived()
    {
        var session = OpenStreamWithHeaders(1);

        var data = new byte[1000];
        var dataFrame = BuildDataFrame(1, data, endStream: true);
        session.Process(dataFrame);

        // Window decrements; caller restores it via SetConnectionReceiveWindow after sending WINDOW_UPDATE.
        Assert.Equal(65535 - 1000, session.ConnectionReceiveWindow);
    }

    /// RFC 7540 §5.2 — FlowControlError when DATA exceeds connection receive window
    [Fact(DisplayName = "FC-003: FlowControlError when DATA exceeds connection receive window")]
    public void Should_ThrowFlowControlError_When_DataExceedsConnectionWindow()
    {
        var session = OpenStreamWithHeaders(1);
        session.SetConnectionReceiveWindow(500);

        var data = new byte[501];
        var dataFrame = BuildDataFrame(1, data);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.2 — FlowControlError when DATA exceeds stream window but not connection window
    [Fact(DisplayName = "FC-004: FlowControlError when DATA exceeds stream window but not connection window")]
    public void Should_ThrowFlowControlError_When_DataExceedsStreamWindowButNotConnectionWindow()
    {
        var session = OpenStreamWithHeaders(1);
        session.SetConnectionReceiveWindow(600);
        session.SetStreamReceiveWindow(1, 300);

        var data = new byte[400]; // 400 <= 600 (connection) but 400 > 300 (stream)
        var dataFrame = BuildDataFrame(1, data);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));
    }

    /// RFC 7540 §5.2 — No FlowControlError when DATA is exactly at connection window limit
    [Fact(DisplayName = "FC-005: No FlowControlError when DATA is exactly at connection window limit")]
    public void Should_AcceptData_When_DataEqualsConnectionWindow()
    {
        var session = OpenStreamWithHeaders(1);
        session.SetConnectionReceiveWindow(100);
        session.SetStreamReceiveWindow(1, 1000);

        var data = new byte[100];
        var dataFrame = BuildDataFrame(1, data, endStream: true);

        // Must not throw.
        session.Process(dataFrame);
    }

    // ── FC-006..010: Stream Receive Window Tracking ───────────────────────────

    /// RFC 7540 §5.2 — Initial stream receive window is 65535
    [Fact(DisplayName = "FC-006: Initial stream receive window is 65535")]
    public void Should_HaveDefaultStreamReceiveWindow_When_StreamOpened()
    {
        var session = OpenStreamWithHeaders(1);
        Assert.Equal(65535, session.GetStreamReceiveWindow(1));
    }

    /// RFC 7540 §5.2 — Connection receive window decrements by DATA size
    [Fact(DisplayName = "FC-007: Connection receive window decrements by DATA size")]
    public void Should_DecrementConnectionWindowByDataSize_When_LargeDataFrameReceived()
    {
        var session = OpenStreamWithHeaders(1);

        var data = new byte[2000];
        var dataFrame = BuildDataFrame(1, data, endStream: true);
        session.Process(dataFrame);

        // Window decrements; caller restores via SetConnectionReceiveWindow after sending WINDOW_UPDATE.
        Assert.Equal(65535 - 2000, session.ConnectionReceiveWindow);
    }

    /// RFC 7540 §5.2 — FlowControlError when DATA exceeds stream receive window
    [Fact(DisplayName = "FC-008: FlowControlError when DATA exceeds stream receive window")]
    public void Should_ThrowFlowControlError_When_DataExceedsStreamWindow()
    {
        var session = OpenStreamWithHeaders(1);
        session.SetStreamReceiveWindow(1, 200);

        var data = new byte[201];
        var dataFrame = BuildDataFrame(1, data);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(dataFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
        Assert.Equal(Http2StreamLifecycleState.Open, session.GetStreamState(1));
    }

    /// RFC 7540 §5.2 — SetStreamReceiveWindow updates stream window correctly
    [Fact(DisplayName = "FC-009: SetStreamReceiveWindow updates stream window correctly")]
    public void Should_UpdateStreamWindow_When_SetStreamReceiveWindowCalled()
    {
        var session = OpenStreamWithHeaders(1);
        session.SetStreamReceiveWindow(1, 999);
        Assert.Equal(999, session.GetStreamReceiveWindow(1));
    }

    /// RFC 7540 §5.2 — Unknown stream receive window defaults to 65535
    [Fact(DisplayName = "FC-010: Unknown stream receive window defaults to 65535")]
    public void Should_Return65535_When_StreamReceiveWindowRequestedForUnknownStream()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(65535, session.GetStreamReceiveWindow(99));
    }

    // ── FC-011..015: Connection Send Window (WINDOW_UPDATE from server) ───────

    /// RFC 7540 §5.2 — Initial connection send window is 65535
    [Fact(DisplayName = "FC-011: Initial connection send window is 65535")]
    public void Should_HaveDefaultConnectionSendWindow_When_DecoderCreated()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(65535L, session.ConnectionSendWindow);
    }

    /// RFC 7540 §5.2 — WINDOW UPDATE on stream 0 increases connection send window
    [Fact(DisplayName = "FC-012: WINDOW_UPDATE on stream 0 increases connection send window")]
    public void Should_IncreaseConnectionSendWindow_When_WindowUpdateReceivedOnStream0()
    {
        var session = new Http2ProtocolSession();
        var wuFrame = BuildWindowUpdate(0, 1000);
        session.Process(wuFrame);
        Assert.Equal(65535L + 1000, session.ConnectionSendWindow);
    }

    /// RFC 7540 §5.2 — Multiple WINDOW UPDATEs on stream 0 accumulate
    [Fact(DisplayName = "FC-013: Multiple WINDOW_UPDATEs on stream 0 accumulate")]
    public void Should_AccumulateConnectionSendWindow_When_MultipleWindowUpdatesReceived()
    {
        var session = new Http2ProtocolSession();
        var wu1 = BuildWindowUpdate(0, 1000);
        var wu2 = BuildWindowUpdate(0, 500);
        var combined = wu1.Concat(wu2).ToArray();
        session.Process(combined);
        Assert.Equal(65535L + 1500, session.ConnectionSendWindow);
    }

    /// RFC 7540 §5.2 — FlowControlError when connection send window overflows 2^31-1
    [Fact(DisplayName = "FC-014: FlowControlError when connection send window overflows 2^31-1")]
    public void Should_ThrowFlowControlError_When_ConnectionSendWindowWouldOverflow()
    {
        var session = new Http2ProtocolSession();
        // Window starts at 65535. Increment by (2^31-1 - 65535 + 1) to overflow.
        var increment = 0x7FFFFFFF - 65535 + 1;
        var wuFrame = BuildWindowUpdate(0, increment);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(wuFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.2 — Connection send window at exactly 2^31-1 is accepted
    [Fact(DisplayName = "FC-015: Connection send window at exactly 2^31-1 is accepted")]
    public void Should_AcceptWindowUpdate_When_ConnectionSendWindowReachesMaxExactly()
    {
        var session = new Http2ProtocolSession();
        var increment = 0x7FFFFFFF - 65535; // exactly reaches max
        var wuFrame = BuildWindowUpdate(0, increment);

        // Must not throw.
        session.Process(wuFrame);
        Assert.Equal(0x7FFFFFFFL, session.ConnectionSendWindow);
    }

    // ── FC-016..020: Stream Send Window (WINDOW_UPDATE for stream > 0) ────────

    /// RFC 7540 §5.2 — Initial stream send window defaults to SETTINGS INITIAL WINDOW SIZE (65535)
    [Fact(DisplayName = "FC-016: Initial stream send window defaults to SETTINGS_INITIAL_WINDOW_SIZE (65535)")]
    public void Should_ReturnInitialWindowSize_When_StreamSendWindowRequestedBeforeAnyUpdate()
    {
        var session = new Http2ProtocolSession();
        Assert.Equal(65535L, session.GetStreamSendWindow(1));
    }

    /// RFC 7540 §5.2 — WINDOW UPDATE on stream N increases that stream's send window
    [Fact(DisplayName = "FC-017: WINDOW_UPDATE on stream N increases that stream's send window")]
    public void Should_IncreaseStreamSendWindow_When_WindowUpdateReceivedForStream()
    {
        var session = new Http2ProtocolSession();
        var wuFrame = BuildWindowUpdate(1, 2000);
        session.Process(wuFrame);
        Assert.Equal(65535L + 2000, session.GetStreamSendWindow(1));
    }

    /// RFC 7540 §5.2 — Multiple stream WINDOW UPDATEs accumulate independently per stream
    [Fact(DisplayName = "FC-018: Multiple stream WINDOW_UPDATEs accumulate independently per stream")]
    public void Should_TrackSendWindowsIndependently_When_MultipleStreamsUpdated()
    {
        var session = new Http2ProtocolSession();
        var wu1 = BuildWindowUpdate(1, 100);
        var wu3 = BuildWindowUpdate(3, 200);
        var combined = wu1.Concat(wu3).ToArray();
        session.Process(combined);

        Assert.Equal(65535L + 100, session.GetStreamSendWindow(1));
        Assert.Equal(65535L + 200, session.GetStreamSendWindow(3));
        Assert.Equal(65535L, session.GetStreamSendWindow(5)); // untouched stream
    }

    /// RFC 7540 §5.2 — FlowControlError when stream send window overflows 2^31-1
    [Fact(DisplayName = "FC-019: FlowControlError when stream send window overflows 2^31-1")]
    public void Should_ThrowFlowControlError_When_StreamSendWindowWouldOverflow()
    {
        var session = new Http2ProtocolSession();
        var increment = 0x7FFFFFFF - 65535 + 1; // one over max
        var wuFrame = BuildWindowUpdate(1, increment);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(wuFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.Contains("stream 1", ex.Message);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
    }

    /// RFC 7540 §5.2 — Stream send window at exactly 2^31-1 is accepted
    [Fact(DisplayName = "FC-020: Stream send window at exactly 2^31-1 is accepted")]
    public void Should_AcceptStreamWindowUpdate_When_StreamSendWindowReachesMaxExactly()
    {
        var session = new Http2ProtocolSession();
        var increment = 0x7FFFFFFF - 65535; // exactly reaches max
        var wuFrame = BuildWindowUpdate(1, increment);

        session.Process(wuFrame);
        Assert.Equal(0x7FFFFFFFL, session.GetStreamSendWindow(1));
    }

    // ── FC-021..023: WINDOW_UPDATE Validation ─────────────────────────────────

    /// RFC 7540 §5.2 — Zero-increment WINDOW UPDATE on stream 0 is a PROTOCOL ERROR
    [Fact(DisplayName = "FC-021: Zero-increment WINDOW_UPDATE on stream 0 is a PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_ConnectionWindowUpdateIncrementIsZero()
    {
        var session = new Http2ProtocolSession();
        var payload = new byte[4]; // 4 zero bytes = increment of 0
        var wuFrame = BuildRawFrame(0x8, 0x0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(wuFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.2 — Zero-increment WINDOW UPDATE on stream N is a PROTOCOL ERROR
    [Fact(DisplayName = "FC-022: Zero-increment WINDOW_UPDATE on stream N is a PROTOCOL_ERROR")]
    public void Should_ThrowProtocolError_When_StreamWindowUpdateIncrementIsZero()
    {
        var session = new Http2ProtocolSession();
        var payload = new byte[4]; // increment of 0
        var wuFrame = BuildRawFrame(0x8, 0x0, 1, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(wuFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §5.2 — WINDOW UPDATE with wrong payload size is a FRAME SIZE ERROR
    [Fact(DisplayName = "FC-023: WINDOW_UPDATE with wrong payload size is a FRAME_SIZE_ERROR")]
    public void Should_ThrowFrameSizeError_When_WindowUpdatePayloadIsNot4Bytes()
    {
        var session = new Http2ProtocolSession();
        var payload = new byte[3]; // wrong size
        var wuFrame = BuildRawFrame(0x8, 0x0, 0, payload);

        var ex = Assert.Throws<Http2Exception>(() => session.Process(wuFrame));
        Assert.Equal(Http2ErrorCode.FrameSizeError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── FC-024..028: Receive Window Tracking for DATA Frames ──────────────────

    /// RFC 7540 §5.2 — Connection and stream receive windows both decrement after DATA received
    [Fact(DisplayName = "FC-024: Connection and stream receive windows decrement after DATA received")]
    public void Should_DecrementBothReceiveWindows_When_DataFrameReceived()
    {
        var session = OpenStreamWithHeaders(1);

        var data = new byte[500];
        var dataFrame = BuildDataFrame(1, data, endStream: true);
        session.Process(dataFrame);

        Assert.Equal(65535 - 500, session.ConnectionReceiveWindow);
        Assert.Equal(65535 - 500, session.GetStreamReceiveWindow(1));
    }

    /// RFC 7540 §5.2 — Connection receive window decrements by exact DATA payload size
    [Fact(DisplayName = "FC-025: Connection receive window decrements by exact DATA payload size")]
    public void Should_DecrementConnectionWindowByExactPayloadSize_When_DataReceived()
    {
        var session = OpenStreamWithHeaders(1);

        var data = new byte[1234];
        var dataFrame = BuildDataFrame(1, data, endStream: true);
        session.Process(dataFrame);

        Assert.Equal(65535 - 1234, session.ConnectionReceiveWindow);
    }

    /// RFC 7540 §5.2 — Stream receive window decrements by exact DATA payload size
    [Fact(DisplayName = "FC-026: Stream receive window decrements by exact DATA payload size")]
    public void Should_DecrementStreamWindowByExactPayloadSize_When_DataReceived()
    {
        var session = OpenStreamWithHeaders(3);

        var data = new byte[777];
        var dataFrame = BuildDataFrame(3, data, endStream: true);
        session.Process(dataFrame);

        Assert.Equal(65535 - 777, session.GetStreamReceiveWindow(3));
    }

    /// RFC 7540 §5.2 — Zero-length DATA does not decrement receive windows
    [Fact(DisplayName = "FC-027: Zero-length DATA does not decrement receive windows")]
    public void Should_NotDecrementReceiveWindows_When_ZeroLengthDataReceived()
    {
        var session = OpenStreamWithHeaders(1);
        var dataFrame = BuildDataFrame(1, [], endStream: false); // zero-length DATA, no END_STREAM
        session.Process(dataFrame);

        Assert.Equal(65535, session.ConnectionReceiveWindow);
        Assert.Equal(65535, session.GetStreamReceiveWindow(1));
    }

    /// RFC 7540 §5.2 — Multiple DATA frames cumulatively decrement receive windows
    [Fact(DisplayName = "FC-028: Multiple DATA frames cumulatively decrement receive windows")]
    public void Should_CumulativelyDecrementReceiveWindows_When_MultipleDataFramesReceived()
    {
        var session = OpenStreamWithHeaders(1);

        var dataFrame1 = BuildDataFrame(1, new byte[100]);
        var dataFrame2 = BuildDataFrame(1, new byte[200], endStream: true);
        var combined = dataFrame1.Concat(dataFrame2).ToArray();

        session.Process(combined);

        Assert.Equal(65535 - 300, session.ConnectionReceiveWindow);
        Assert.Equal(65535 - 300, session.GetStreamReceiveWindow(1));
    }

    // ── FC-029..032: SETTINGS_INITIAL_WINDOW_SIZE ─────────────────────────────

    /// RFC 7540 §5.2 — SETTINGS INITIAL WINDOW SIZE updates default send window for unknown streams
    [Fact(DisplayName = "FC-029: SETTINGS_INITIAL_WINDOW_SIZE updates default send window for unknown streams")]
    public void Should_UseNewInitialWindowSize_When_SettingsUpdatesInitialWindowSize()
    {
        var session = new Http2ProtocolSession();
        var settingsFrame = BuildSettingsFrame(
            new List<(SettingsParameter, uint)> { (SettingsParameter.InitialWindowSize, 32768u) });
        session.Process(settingsFrame);

        // Unknown streams should return the updated initial window size as default.
        Assert.Equal(32768L, session.GetStreamSendWindow(99));
    }

    /// RFC 7540 §5.2 — SETTINGS INITIAL WINDOW SIZE applies delta to existing open streams
    [Fact(DisplayName = "FC-030: SETTINGS_INITIAL_WINDOW_SIZE applies delta to existing open streams")]
    public void Should_ApplyDeltaToOpenStreams_When_InitialWindowSizeSettingChanges()
    {
        var session = OpenStreamWithHeaders(1);
        // Stream 1 is now open with default send window 65535.

        // Change initial window size to 32768 (delta = -32767).
        var settingsFrame = BuildSettingsFrame(
            new List<(SettingsParameter, uint)> { (SettingsParameter.InitialWindowSize, 32768u) });
        session.Process(settingsFrame);

        Assert.Equal(32768L, session.GetStreamSendWindow(1));
    }

    /// RFC 7540 §5.2 — SETTINGS INITIAL WINDOW SIZE increase applies positive delta to open streams
    [Fact(DisplayName = "FC-031: SETTINGS_INITIAL_WINDOW_SIZE increase applies positive delta to open streams")]
    public void Should_IncreaseOpenStreamsWindowByDelta_When_InitialWindowSizeIncreased()
    {
        var session = OpenStreamWithHeaders(1);
        // Stream 1 is open with default 65535.

        // Apply a stream WINDOW_UPDATE to make stream 1's window differ from default.
        var wu = BuildWindowUpdate(1, 1000);
        session.Process(wu);
        Assert.Equal(65535L + 1000, session.GetStreamSendWindow(1));

        // Now increase initial window size by 10000 (75535 - 65535 = 10000 delta).
        var settingsFrame = BuildSettingsFrame(
            new List<(SettingsParameter, uint)> { (SettingsParameter.InitialWindowSize, 75535u) });
        session.Process(settingsFrame);

        // Delta = +10000 applied to stream 1's explicit window (66535 + 10000 = 76535).
        Assert.Equal(65535L + 1000 + 10000, session.GetStreamSendWindow(1));
    }

    /// RFC 7540 §5.2 — SETTINGS INITIAL WINDOW SIZE value > 2^31-1 causes FLOW CONTROL ERROR
    [Fact(DisplayName = "FC-032: SETTINGS_INITIAL_WINDOW_SIZE value > 2^31-1 causes FLOW_CONTROL_ERROR")]
    public void Should_ThrowFlowControlError_When_InitialWindowSizeExceedsMax()
    {
        var session = new Http2ProtocolSession();
        var settingsFrame = BuildSettingsFrame(
            new List<(SettingsParameter, uint)> { (SettingsParameter.InitialWindowSize, 0x80000000u) });

        var ex = Assert.Throws<Http2Exception>(() => session.Process(settingsFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // ── FC-033..035: Reset Clears All Flow Control State ──────────────────────

    /// RFC 7540 §5.2 — Reset restores connection receive window to 65535
    [Fact(DisplayName = "FC-033: Reset restores connection receive window to 65535")]
    public void Should_RestoreConnectionReceiveWindowTo65535_When_ResetCalled()
    {
        var session = OpenStreamWithHeaders(1);
        session.SetConnectionReceiveWindow(100);
        session.Reset();
        Assert.Equal(65535, session.ConnectionReceiveWindow);
    }

    /// RFC 7540 §5.2 — Reset restores connection send window to 65535
    [Fact(DisplayName = "FC-034: Reset restores connection send window to 65535")]
    public void Should_RestoreConnectionSendWindowTo65535_When_ResetCalled()
    {
        var session = new Http2ProtocolSession();
        var wu = BuildWindowUpdate(0, 5000);
        session.Process(wu);
        session.Reset();
        Assert.Equal(65535L, session.ConnectionSendWindow);
    }

    /// RFC 7540 §5.2 — Reset clears stream send windows back to default
    [Fact(DisplayName = "FC-035: Reset clears stream send windows back to default")]
    public void Should_ClearStreamSendWindows_When_ResetCalled()
    {
        var session = new Http2ProtocolSession();
        var wu = BuildWindowUpdate(1, 9999);
        session.Process(wu);
        Assert.NotEqual(65535L, session.GetStreamSendWindow(1));

        session.Reset();
        Assert.Equal(65535L, session.GetStreamSendWindow(1));
    }

    // ── FC-036..038: Window Update Result Reporting ───────────────────────────

    /// RFC 7540 §5.2 — Received WINDOW_UPDATE is reported in session.WindowUpdates
    [Fact(DisplayName = "FC-036: Received WINDOW_UPDATE is reported in session.WindowUpdates")]
    public void Should_ReportWindowUpdateInResult_When_WindowUpdateFrameReceived()
    {
        var session = new Http2ProtocolSession();
        var wu = BuildWindowUpdate(1, 4096);
        session.Process(wu);

        Assert.Single(session.WindowUpdates);
        Assert.Equal((1, 4096), session.WindowUpdates[0]);
    }

    /// RFC 7540 §5.2 — Connection WINDOW_UPDATE tracked separately from stream WINDOW_UPDATEs
    [Fact(DisplayName = "FC-037: Connection WINDOW_UPDATE tracked separately from stream WINDOW_UPDATEs")]
    public void Should_TrackConnectionAndStreamWindowUpdatesSeparately_When_MultipleWindowUpdateFramesReceived()
    {
        var session = new Http2ProtocolSession();
        var wu1 = BuildWindowUpdate(0, 100);
        var wu2 = BuildWindowUpdate(1, 200);
        var wu3 = BuildWindowUpdate(3, 300);
        var combined = wu1.Concat(wu2).Concat(wu3).ToArray();
        session.Process(combined);

        // Stream-level WINDOW_UPDATEs accumulate in WindowUpdates.
        Assert.Equal(2, session.WindowUpdates.Count);
        Assert.Contains((1, 200), session.WindowUpdates);
        Assert.Contains((3, 300), session.WindowUpdates);
        // Connection-level WINDOW_UPDATE tracked in ConnectionSendWindow.
        Assert.Equal(65535L + 100, session.ConnectionSendWindow);
    }

    /// RFC 7540 §5.2 — Zero-length DATA with END_STREAM does not decrement receive windows
    [Fact(DisplayName = "FC-038: Zero-length DATA with END_STREAM does not decrement receive windows")]
    public void Should_NotDecrementReceiveWindows_When_ZeroLengthDataWithEndStreamReceived()
    {
        var session = OpenStreamWithHeaders(1);
        // END_STREAM on zero-length DATA closes stream without decrementing receive windows.
        var dataFrame = BuildDataFrame(1, [], endStream: true);
        session.Process(dataFrame);

        Assert.Equal(65535, session.ConnectionReceiveWindow);
    }
}
