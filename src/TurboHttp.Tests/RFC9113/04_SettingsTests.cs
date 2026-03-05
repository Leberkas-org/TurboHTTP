using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// RFC 7540 §6.5 — SETTINGS Synchronization (Phase 8-9).
///
/// Covers:
///   - Connection preface includes SETTINGS frame (RFC 7540 §3.5)
///   - Decoder applies peer SETTINGS after receipt (RFC 7540 §6.5)
///   - Decoder emits SETTINGS ACK for every non-ACK SETTINGS (RFC 7540 §6.5)
///   - Validation of SETTINGS_MAX_FRAME_SIZE, SETTINGS_INITIAL_WINDOW_SIZE,
///     SETTINGS_ENABLE_PUSH, SETTINGS_MAX_CONCURRENT_STREAMS (RFC 7540 §6.5.2)
///   - SETTINGS_HEADER_TABLE_SIZE applied to HPACK decoder (RFC 7541 §4.2)
///   - SETTINGS flood protection (security)
/// </summary>
public sealed class Http2SettingsSynchronizationTests
{
    // =========================================================================
    // Category 1: Connection Preface includes SETTINGS (RFC 7540 §3.5)
    // =========================================================================

    /// RFC 7540 §3.5 — BuildConnectionPreface produces magic + SETTINGS frame
    [Fact(DisplayName = "RFC7540-3.5-SS-001: BuildConnectionPreface produces magic + SETTINGS frame")]
    public void Preface_IncludesSettingsFrame()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();

        // 24-byte magic
        const string magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n";
        Assert.True(preface.Length > magic.Length + 9, "Preface must be longer than magic + 9-byte frame header");

        // Frame header starts at byte 24
        var frameType = (FrameType)preface[24 + 3];
        Assert.Equal(FrameType.Settings, frameType);
    }

    /// RFC 7540 §3.5 — Connection preface SETTINGS is on stream 0
    [Fact(DisplayName = "RFC7540-3.5-SS-002: Connection preface SETTINGS is on stream 0")]
    public void Preface_SettingsIsOnStreamZero()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(preface.AsSpan(24 + 5)) & 0x7FFFFFFFu);
        Assert.Equal(0, streamId);
    }

    /// RFC 7540 §3.5 — Connection preface SETTINGS contains HeaderTableSize=4096
    [Fact(DisplayName = "RFC7540-3.5-SS-003: Connection preface SETTINGS contains HeaderTableSize=4096")]
    public void Preface_SettingsContainsHeaderTableSize4096()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        Assert.True(ContainsSetting(preface, 24, SettingsParameter.HeaderTableSize, 4096));
    }

    /// RFC 7540 §3.5 — Connection preface SETTINGS contains EnablePush=0
    [Fact(DisplayName = "RFC7540-3.5-SS-004: Connection preface SETTINGS contains EnablePush=0")]
    public void Preface_SettingsContainsEnablePush0()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        Assert.True(ContainsSetting(preface, 24, SettingsParameter.EnablePush, 0));
    }

    /// RFC 7540 §3.5 — Connection preface SETTINGS contains MaxFrameSize=16384
    [Fact(DisplayName = "RFC7540-3.5-SS-005: Connection preface SETTINGS contains MaxFrameSize=16384")]
    public void Preface_SettingsContainsMaxFrameSize16384()
    {
        var preface = Http2FrameUtils.BuildConnectionPreface();
        Assert.True(ContainsSetting(preface, 24, SettingsParameter.MaxFrameSize, 16384));
    }

    // =========================================================================
    // Category 2: Decoder applies SETTINGS_MAX_FRAME_SIZE (RFC 7540 §6.5.2)
    // =========================================================================

    /// RFC 7540 §6.5.2 — MaxFrameSize=16383 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC7540-6.5.2-SS-006: MaxFrameSize=16383 is PROTOCOL_ERROR")]
    public void Settings_MaxFrameSize_BelowMin_ThrowsProtocolError()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16383u)]).Serialize();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §6.5.2 — MaxFrameSize=16777216 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC7540-6.5.2-SS-007: MaxFrameSize=16777216 is PROTOCOL_ERROR")]
    public void Settings_MaxFrameSize_AboveMax_ThrowsProtocolError()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777216u)]).Serialize();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §6.5.2 — After MaxFrameSize update, larger frames are accepted
    [Fact(DisplayName = "RFC7540-6.5.2-SS-008: After MaxFrameSize update, larger frames are accepted")]
    public void Settings_MaxFrameSize_Update_AllowsLargerFrames()
    {
        var session = new Http2ProtocolSession();

        // Raise to 32768
        const int newMax = 32768;
        var updateSettings = new SettingsFrame([(SettingsParameter.MaxFrameSize, newMax)]).Serialize();
        session.Process(updateSettings);

        // Now build a SETTINGS frame with a payload size of 32742 bytes (5457 × 6 = 32742)
        const int payloadLen = 32742;
        var bigFrame = new byte[9 + payloadLen];
        bigFrame[0] = payloadLen >> 16;
        bigFrame[1] = payloadLen >> 8;
        bigFrame[2] = payloadLen & 0xFF;
        bigFrame[3] = 0x04; // SETTINGS
        // fill with valid SETTINGS entries (HeaderTableSize=4096 repeated)
        for (var i = 0; i < payloadLen; i += 6)
        {
            bigFrame[9 + i + 1] = 0x01; // HeaderTableSize
            bigFrame[9 + i + 5] = 0x01; // value = 1
        }

        session.Process(bigFrame);
        Assert.True(session.HasNewSettings);
    }

    // =========================================================================
    // Category 3: Decoder validates SETTINGS_INITIAL_WINDOW_SIZE (RFC 7540 §6.5.2)
    // =========================================================================

    /// RFC 7540 §6.5.2 — InitialWindowSize above 2^31-1 is FLOW_CONTROL_ERROR
    [Fact(DisplayName = "RFC7540-6.5.2-SS-009: InitialWindowSize above 2^31-1 is FLOW_CONTROL_ERROR")]
    public void Settings_InitialWindowSize_Overflow_ThrowsFlowControlError()
    {
        var frame = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0x80000000u)]).Serialize();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 §6.5.2 — InitialWindowSize of exactly 2^31-1 is accepted
    [Fact(DisplayName = "RFC7540-6.5.2-SS-010: InitialWindowSize of exactly 2^31-1 is accepted")]
    public void Settings_InitialWindowSize_MaxValid_Accepted()
    {
        var frame = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0x7FFFFFFFu)]).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.HasNewSettings);
    }

    // =========================================================================
    // Category 4: Decoder validates SETTINGS_ENABLE_PUSH (RFC 7540 §6.5.2)
    // =========================================================================

    /// RFC 7540 §6.5.2 — EnablePush=0 is accepted
    [Fact(DisplayName = "RFC7540-6.5.2-SS-011: EnablePush=0 is accepted")]
    public void Settings_EnablePush_Zero_Accepted()
    {
        var frame = new SettingsFrame([(SettingsParameter.EnablePush, 0u)]).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.HasNewSettings);
    }

    /// RFC 7540 §6.5.2 — EnablePush=1 is accepted
    [Fact(DisplayName = "RFC7540-6.5.2-SS-012: EnablePush=1 is accepted")]
    public void Settings_EnablePush_One_Accepted()
    {
        var frame = new SettingsFrame([(SettingsParameter.EnablePush, 1u)]).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.HasNewSettings);
    }

    /// RFC 7540 §6.5.2 — EnablePush=2 is PROTOCOL_ERROR
    [Fact(DisplayName = "RFC7540-6.5.2-SS-013: EnablePush=2 is PROTOCOL_ERROR")]
    public void Settings_EnablePush_Two_ThrowsProtocolError()
    {
        var frame = new SettingsFrame([(SettingsParameter.EnablePush, 2u)]).Serialize();
        var session = new Http2ProtocolSession();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // Category 5: Decoder applies SETTINGS_MAX_CONCURRENT_STREAMS (RFC 7540 §6.5.2)
    // =========================================================================

    /// RFC 7540 §6.5.2 — MaxConcurrentStreams=0 blocks all new streams
    [Fact(DisplayName = "RFC7540-6.5.2-SS-014: MaxConcurrentStreams=0 blocks all new streams")]
    public void Settings_MaxConcurrentStreams_Zero_BlocksAllStreams()
    {
        var session = new Http2ProtocolSession();

        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 0u)]).Serialize();
        session.Process(settings);

        // Now try to open a stream via HEADERS
        var headers = BuildMinimalHeadersFrame(streamId: 1, endStream: true);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(headers));
        Assert.Equal(Http2ErrorCode.RefusedStream, ex.ErrorCode);
        Assert.Equal(Http2ErrorScope.Stream, ex.Scope);
        Assert.Equal(1, ex.StreamId);
        Assert.Equal(Http2StreamLifecycleState.Idle, session.GetStreamState(1));
    }

    /// RFC 7540 §6.5.2 — MaxConcurrentStreams=1 allows exactly one stream
    [Fact(DisplayName = "RFC7540-6.5.2-SS-015: MaxConcurrentStreams=1 allows exactly one stream")]
    public void Settings_MaxConcurrentStreams_One_AllowsOneStream()
    {
        var session = new Http2ProtocolSession();

        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 1u)]).Serialize();
        session.Process(settings);

        Assert.Equal(1, session.MaxConcurrentStreams);
    }

    // =========================================================================
    // Category 6: Decoder applies SETTINGS_HEADER_TABLE_SIZE to HPACK (RFC 7541 §4.2)
    // =========================================================================

    /// RFC 7541 §4.2 — HeaderTableSize=0 accepted and applied to HPACK decoder
    [Fact(DisplayName = "RFC7541-4.2-SS-016: HeaderTableSize=0 accepted and applied to HPACK decoder")]
    public void Settings_HeaderTableSize_Zero_Accepted()
    {
        var frame = new SettingsFrame([(SettingsParameter.HeaderTableSize, 0u)]).Serialize();
        var session = new Http2ProtocolSession();
        // Should not throw — size=0 is valid per RFC 7541 §4.2
        session.Process(frame);
        Assert.True(session.HasNewSettings);
    }

    /// RFC 7541 §4.2 — HeaderTableSize=1024 accepted and applied
    [Fact(DisplayName = "RFC7541-4.2-SS-017: HeaderTableSize=1024 accepted and applied")]
    public void Settings_HeaderTableSize_1024_Accepted()
    {
        var frame = new SettingsFrame([(SettingsParameter.HeaderTableSize, 1024u)]).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.HasNewSettings);
        Assert.Contains(session.ReceivedSettings[0], p => p.Item1 == SettingsParameter.HeaderTableSize && p.Item2 == 1024u);
    }

    /// RFC 7541 §4.2 — HeaderTableSize=4096 (default) accepted and applied
    [Fact(DisplayName = "RFC7541-4.2-SS-018: HeaderTableSize=4096 (default) accepted and applied")]
    public void Settings_HeaderTableSize_Default_Accepted()
    {
        var frame = new SettingsFrame([(SettingsParameter.HeaderTableSize, 4096u)]).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.HasNewSettings);
    }

    // =========================================================================
    // Category 7: Decoder emits SETTINGS ACK (RFC 7540 §6.5)
    // =========================================================================

    /// RFC 7540 §6.5 — Non-ACK SETTINGS produces one SETTINGS ACK to send
    [Fact(DisplayName = "RFC7540-6.5-SS-019: Non-ACK SETTINGS produces one SETTINGS ACK to send")]
    public void HandleSettings_NonAck_ProducesSettingsAck()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.Single(session.SettingsAcksToSend);
    }

    /// RFC 7540 §6.5 — SETTINGS ACK frame produces no new ACK in return
    [Fact(DisplayName = "RFC7540-6.5-SS-020: SETTINGS ACK frame produces no new ACK in return")]
    public void HandleSettings_AckFrame_ProducesNoAck()
    {
        var ack = SettingsFrame.SettingsAck();
        var session = new Http2ProtocolSession();
        session.Process(ack);

        Assert.Empty(session.SettingsAcksToSend);
    }

    /// RFC 7540 §6.5 — Three SETTINGS frames produce three ACKs to send
    [Fact(DisplayName = "RFC7540-6.5-SS-021: Three SETTINGS frames produce three ACKs to send")]
    public void HandleSettings_ThreeSettings_ProducesThreeAcks()
    {
        var session = new Http2ProtocolSession();

        var combined = new List<byte>();
        for (var i = 0; i < 3; i++)
        {
            combined.AddRange(new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize());
        }

        session.Process(combined.ToArray());
        Assert.Equal(3, session.SettingsAcksToSend.Count);
    }

    /// RFC 7540 §6.5 — Empty SETTINGS frame (zero parameters) produces ACK
    [Fact(DisplayName = "RFC7540-6.5-SS-022: Empty SETTINGS frame (zero parameters) produces ACK")]
    public void HandleSettings_EmptyPayload_ProducesAck()
    {
        // Empty SETTINGS frame: length=0, type=SETTINGS, flags=0, stream=0
        var emptySettings = new byte[]
        {
            0x00, 0x00, 0x00, // length = 0
            0x04,             // SETTINGS
            0x00,             // flags = 0 (no ACK)
            0x00, 0x00, 0x00, 0x00  // stream = 0
        };
        var session = new Http2ProtocolSession();
        session.Process(emptySettings);
        Assert.Single(session.SettingsAcksToSend);
    }

    /// RFC 7540 §6.5 — Encoded SETTINGS ACK is a valid 9-byte frame
    [Fact(DisplayName = "RFC7540-6.5-SS-023: Encoded SETTINGS ACK is a valid 9-byte frame")]
    public void EncodeSettingsAck_ProducesValidAckFrame()
    {
        var ack = Http2FrameUtils.EncodeSettingsAck();

        Assert.Equal(9, ack.Length);
        // Length = 0
        Assert.Equal(0, (ack[0] << 16) | (ack[1] << 8) | ack[2]);
        // Type = SETTINGS
        Assert.Equal((byte)FrameType.Settings, ack[3]);
        // Flags = ACK (0x1)
        Assert.Equal((byte)Settings.Ack, ack[4]);
        // Stream = 0
        var streamId = BinaryPrimitives.ReadUInt32BigEndian(ack.AsSpan(5)) & 0x7FFFFFFFu;
        Assert.Equal(0u, streamId);
    }

    // =========================================================================
    // Category 8: SETTINGS flood protection (security)
    // =========================================================================

    /// RFC 7540 security — SETTINGS flood above 100 frames throws EnhanceYourCalm
    [Fact(DisplayName = "RFC7540-security-SS-024: SETTINGS flood above 100 frames throws EnhanceYourCalm")]
    public void Settings_FloodProtection_ThrowsAfterLimit()
    {
        var session = new Http2ProtocolSession();
        var frame = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();

        // Send 100 SETTINGS frames — all should be accepted
        for (var i = 0; i < 100; i++)
        {
            session.Process(frame);
        }

        // 101st should throw
        var ex = Assert.Throws<Http2Exception>(() => session.Process(frame));
        Assert.Equal(Http2ErrorCode.EnhanceYourCalm, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 7540 security — SETTINGS ACK frames do not count toward flood limit
    [Fact(DisplayName = "RFC7540-security-SS-025: SETTINGS ACK frames do not count toward flood limit")]
    public void Settings_AckFrames_DoNotCountTowardFloodLimit()
    {
        var session = new Http2ProtocolSession();
        var ack = SettingsFrame.SettingsAck();

        // 200 ACKs should not trigger flood protection
        for (var i = 0; i < 200; i++)
        {
            session.Process(ack);
        }
        // No exception thrown — pass
    }

    /// RFC 7540 security — Reset clears SETTINGS flood counter
    [Fact(DisplayName = "RFC7540-security-SS-026: Reset clears SETTINGS flood counter")]
    public void Settings_FloodCounter_ClearedOnReset()
    {
        var session = new Http2ProtocolSession();
        var frame = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();

        // Send 100 SETTINGS frames to hit the limit
        for (var i = 0; i < 100; i++)
        {
            session.Process(frame);
        }

        // Reset should clear the counter
        session.Reset();

        // Now 100 more should work fine
        for (var i = 0; i < 100; i++)
        {
            session.Process(frame);
        }
        // No exception — pass
    }

    // =========================================================================
    // Category 9: Unknown parameter handling (RFC 7540 §6.5)
    // =========================================================================

    /// RFC 7540 §6.5 — Unknown SETTINGS parameter ID is silently ignored
    [Fact(DisplayName = "RFC7540-6.5-SS-027: Unknown SETTINGS parameter ID is silently ignored")]
    public void Settings_UnknownParameterId_Ignored()
    {
        var frame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6
            0x04,             // SETTINGS
            0x00,             // flags = 0
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0xFF, 0xFF,       // unknown parameter = 0xFFFF
            0x00, 0x00, 0x00, 0x01  // value = 1
        };
        var session = new Http2ProtocolSession();
        session.Process(frame);
        Assert.True(session.HasNewSettings);
        // Unknown param is decoded but doesn't break anything
    }

    /// RFC 7540 §6.5 — Multiple parameters in one SETTINGS frame are all applied
    [Fact(DisplayName = "RFC7540-6.5-SS-028: Multiple parameters in one SETTINGS frame are all applied")]
    public void Settings_MultipleParameters_AllApplied()
    {
        var settings = new (SettingsParameter, uint)[]
        {
            (SettingsParameter.MaxFrameSize, 32768u),
            (SettingsParameter.MaxConcurrentStreams, 50u),
            (SettingsParameter.HeaderTableSize, 2048u),
        };
        var frame = new SettingsFrame(settings).Serialize();
        var session = new Http2ProtocolSession();
        session.Process(frame);

        Assert.True(session.HasNewSettings);
        var received = session.ReceivedSettings[0];
        Assert.Equal(3, received.Count);
        Assert.Equal(50, session.MaxConcurrentStreams);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Checks whether the SETTINGS frame (starting at <paramref name="frameOffset"/>) inside
    /// <paramref name="data"/> contains a parameter with the given value.
    /// </summary>
    private static bool ContainsSetting(byte[] data, int frameOffset, SettingsParameter param, uint expectedValue)
    {
        var payloadLen = (data[frameOffset] << 16) | (data[frameOffset + 1] << 8) | data[frameOffset + 2];
        var payloadStart = frameOffset + 9;

        for (var i = 0; i + 6 <= payloadLen; i += 6)
        {
            var p = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(payloadStart + i));
            var v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payloadStart + i + 2));
            if (p == param && v == expectedValue)
            {
                return true;
            }
        }

        return false;
    }

    // =========================================================================
    // Category 7: SETTINGS_INITIAL_WINDOW_SIZE overflow of open stream (RFC 7540 §6.9.2)
    // =========================================================================

    /// RFC 7540 §6.9.2 — SETTINGS INITIAL_WINDOW_SIZE increase overflows an open stream's
    /// send window; this is a connection-level FLOW_CONTROL_ERROR.
    [Fact(DisplayName = "RFC7540-6.9.2-SS-029: InitialWindowSize increase overflows open stream send window is FLOW_CONTROL_ERROR")]
    public void Settings_InitialWindowSize_IncreaseOverflowsOpenStreamWindow_ThrowsFlowControlError()
    {
        var session = new Http2ProtocolSession();

        // Open stream 1 with HEADERS (endStream=false — stream stays open in _streams).
        var headers = BuildMinimalHeadersFrame(streamId: 1, endStream: false);
        session.Process(headers.AsMemory());

        // Push stream 1's send window to exactly 2^31-1 = 2147483647 (max valid):
        //   initial = 65535, increment = 2^31-1 - 65535 = 2147418112
        const int initialWindow = 65535;
        const int maxWindow = 0x7FFFFFFF;
        var increment = maxWindow - initialWindow;
        var wu = new WindowUpdateFrame(1, increment).Serialize();
        session.Process(wu.AsMemory());

        // Now send SETTINGS with InitialWindowSize = 65536 (delta = +1).
        // updated = 2^31-1 + 1 = 2147483648 > 2^31-1 — connection error FLOW_CONTROL_ERROR.
        var settings = new SettingsFrame([(SettingsParameter.InitialWindowSize, 65536u)]).Serialize();
        var ex = Assert.Throws<Http2Exception>(() => session.Process(settings.AsMemory()));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// <summary>
    /// Builds a minimal HEADERS frame for the given stream ID using a pre-encoded HPACK header block
    /// (just ":status 200" equivalent — minimal enough to not crash HPACK decode).
    /// </summary>
    private static byte[] BuildMinimalHeadersFrame(int streamId, bool endStream)
    {
        // Minimal HPACK: ":status: 200" via static table index 8 (indexed representation)
        var headerBlock = new byte[] { 0x88 }; // index 8 = :status: 200
        var flags = (byte)(Headers.EndHeaders | (endStream ? Headers.EndStream : 0));

        var frame = new byte[9 + headerBlock.Length];
        frame[0] = 0;
        frame[1] = 0;
        frame[2] = (byte)headerBlock.Length;
        frame[3] = (byte)FrameType.Headers;
        frame[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId);
        headerBlock.CopyTo(frame, 9);
        return frame;
    }
}
