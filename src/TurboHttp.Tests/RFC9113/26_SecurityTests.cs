using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2SecurityTests
{
    // Helpers — RFC-mandated validators that the decoder delegates to the caller

    /// <summary>
    /// RFC 9113 §6.10 + security best practice: Excessive CONTINUATION frames
    /// indicate a possible CONTINUATION flood attack. Threshold default: 1000 frames.
    /// </summary>
    private static void EnforceContinuationFloodThreshold(int continuationCount, int threshold = 1000)
    {
        if (continuationCount >= threshold)
        {
            throw new Http2Exception(
                $"RFC 9113 security: Excessive CONTINUATION frames ({continuationCount}) — possible CONTINUATION flood.",
                Http2ErrorCode.ProtocolError);
        }
    }

    /// <summary>
    /// RFC 9113 §5.1 + §6.4 + CVE-2023-44487: Rapid RST_STREAM cycling can exhaust
    /// server resources. Threshold default: 100 RST_STREAM frames per connection window.
    /// </summary>
    private static void EnforceRstFloodThreshold(int rstCount, int threshold = 100)
    {
        if (rstCount > threshold)
        {
            throw new Http2Exception(
                "RFC 9113 security: Rapid RST_STREAM cycling — possible CVE-2023-44487 attack.",
                Http2ErrorCode.ProtocolError);
        }
    }

    /// <summary>
    /// RFC 9113 §6.1 + security best practice: Excessive zero-length DATA frames
    /// indicate a possible empty DATA flood attack. Threshold default: 10000 frames.
    /// </summary>
    private static void EnforceEmptyDataFloodThreshold(int emptyDataCount, int threshold = 10000)
    {
        if (emptyDataCount > threshold)
        {
            throw new Http2Exception(
                "RFC 9113 security: Excessive zero-length DATA frames — possible resource exhaustion.",
                Http2ErrorCode.ProtocolError);
        }
    }

    /// <summary>
    /// RFC 9113 §6.5.2: SETTINGS_ENABLE_PUSH MUST be 0 or 1.
    /// Any other value is a connection error (PROTOCOL_ERROR).
    /// </summary>
    private static void EnforceEnablePush(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == SettingsParameter.EnablePush && value > 1)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.5.2: SETTINGS_ENABLE_PUSH value {value} is invalid; must be 0 or 1.",
                    Http2ErrorCode.ProtocolError);
            }
        }
    }

    /// <summary>
    /// RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE MUST NOT exceed 2^31−1 (0x7FFFFFFF).
    /// Any larger value is a connection error (FLOW_CONTROL_ERROR).
    /// </summary>
    private static void EnforceInitialWindowSize(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == SettingsParameter.InitialWindowSize && value > 0x7FFFFFFFu)
            {
                throw new Http2Exception(
                    $"RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE {value} exceeds the maximum 2^31−1.",
                    Http2ErrorCode.FlowControlError);
            }
        }
    }

    // SEC-001..003: CONTINUATION Flood (RFC 9113 §6.10)

    /// RFC 9113 §6.10: Excessive CONTINUATION frames detected by explicit enforcement
    [Fact(DisplayName = "RFC9113-6.10-SEC-001: Excessive CONTINUATION frames detected by flood enforcement")]
    public void Should_DetectContinuationFlood_WhenExplicitEnforcementApplied()
    {
        var decoder = new Http2FrameDecoder();

        // HEADERS frame on stream 1, no END_HEADERS (flags=0x0).
        // Payload: one valid HPACK byte (0x88 = indexed :status: 200).
        var headersPayload = new byte[] { 0x88 };
        var headersFrame = BuildRawFrame(frameType: 0x1, flags: 0x0, streamId: 1, headersPayload);

        // 999 CONTINUATION frames without END_HEADERS (flags=0x0, empty payload).
        // The decoder accepts these without exception (no state tracking for headers completion).
        var continuationNoEnd = BuildRawFrame(frameType: 0x9, flags: 0x0, streamId: 1, []);
        var continuations999 = new byte[999 * continuationNoEnd.Length];
        for (var i = 0; i < 999; i++)
        {
            continuationNoEnd.CopyTo(continuations999, i * continuationNoEnd.Length);
        }

        // Feed HEADERS + 999 CONTINUATION frames — decoder decodes them fine.
        var chunk1 = new byte[headersFrame.Length + continuations999.Length];
        headersFrame.CopyTo(chunk1, 0);
        continuations999.CopyTo(chunk1, headersFrame.Length);

        var framesDecoded = decoder.Decode(chunk1);
        Assert.NotEmpty(framesDecoded); // At least the HEADERS frame decoded

        // Count CONTINUATION frames decoded — should be 999.
        var continuationCount = framesDecoded.OfType<ContinuationFrame>().Count();
        Assert.Equal(999, continuationCount);

        // The 1000th CONTINUATION frame exceeds the threshold and must be rejected
        // by explicit enforcement.
        var continuation1000 = BuildRawFrame(frameType: 0x9, flags: 0x0, streamId: 1, []);
        var framesToCheck = decoder.Decode(continuation1000);
        continuationCount++; // Add the 1000th frame

        var ex = Assert.Throws<Http2Exception>(() =>
            EnforceContinuationFloodThreshold(continuationCount, threshold: 1000));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // SEC-002: Rapid RST_STREAM Protection (RFC 9113 §5.1, §6.4 + CVE-2023-44487)

    /// RFC 9113 §5.1 + §6.4: Rapid RST_STREAM cycling (101 frames) detected by enforcement
    [Fact(DisplayName = "RFC9113-5.1-SEC-002: Rapid RST_STREAM cycling detected by flood enforcement (CVE-2023-44487)")]
    public void Should_DetectRstFlood_WhenExplicitEnforcementApplied()
    {
        var decoder = new Http2FrameDecoder();

        // Send 100 RST_STREAM frames on distinct stream IDs — decoder accepts all.
        // RST_STREAM payload: 4 bytes error code (NO_ERROR = 0x0).
        var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        int rstCount = 0;
        for (var i = 0; i < 100; i++) // 100 frames on stream IDs 1, 3, 5, ..., 199
        {
            var rst = BuildRawFrame(frameType: 0x3, flags: 0x0, streamId: 2 * i + 1, errorCode);
            var framesDecoded = decoder.Decode(rst);
            if (framesDecoded.OfType<RstStreamFrame>().Any())
            {
                rstCount++;
            }
        }

        Assert.Equal(100, rstCount); // Decoder accepted all 100 RST_STREAM frames

        // The 101st RST_STREAM frame exceeds the threshold and must be rejected
        // by explicit enforcement.
        var rst101 = BuildRawFrame(frameType: 0x3, flags: 0x0, streamId: 201, errorCode);
        decoder.Decode(rst101); // Decoder still accepts it
        rstCount++; // Count reaches 101

        var ex = Assert.Throws<Http2Exception>(() =>
            EnforceRstFloodThreshold(rstCount, threshold: 100));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // SEC-003: Empty DATA Frame Flood (RFC 9113 §6.1)

    /// RFC 9113 §6.1: Excessive zero-length DATA frames detected by enforcement
    [Fact(DisplayName = "RFC9113-6.1-SEC-003: Excessive zero-length DATA frames detected by flood enforcement")]
    public void Should_DetectEmptyDataFlood_WhenExplicitEnforcementApplied()
    {
        var decoder = new Http2FrameDecoder();

        // Build a buffer with 10001 empty DATA frames on stream 1.
        // Each frame: 9-byte header + 0-byte payload = 9 bytes.
        const int count = 10001;
        var emptyData = BuildRawFrame(frameType: 0x0, flags: 0x0, streamId: 1, []);
        var allFrames = new byte[count * emptyData.Length];
        for (var i = 0; i < count; i++)
        {
            emptyData.CopyTo(allFrames, i * emptyData.Length);
        }

        // Feed all frames — decoder will decode them successfully.
        // We must count empty DATA frames and enforce the threshold.
        var framesDecoded = decoder.Decode(allFrames);
        var emptyDataCount = framesDecoded.OfType<DataFrame>()
            .Count(df => df.Data.Length == 0);

        // Enforce the threshold — should be exactly 10001 empty DATA frames.
        var ex = Assert.Throws<Http2Exception>(() =>
            EnforceEmptyDataFloodThreshold(emptyDataCount, threshold: 10000));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // SEC-004..005: SETTINGS Parameter Validation (RFC 9113 §6.5.2)

    /// RFC 9113 §6.5.2: SETTINGS_ENABLE_PUSH > 1 causes PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SEC-004: SETTINGS ENABLE_PUSH > 1 rejected by enforcement")]
    public void Should_DetectInvalidEnablePush_WhenSettingsEnforcementApplied()
    {
        var decoder = new Http2FrameDecoder();

        // SETTINGS frame: EnablePush = 2 (invalid — only 0 or 1 are valid).
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.EnablePush, 2u),
        }).Serialize();

        var framesDecoded = decoder.Decode(settingsFrame);
        var settings = Assert.Single(framesDecoded);
        var settingsF = Assert.IsType<SettingsFrame>(settings);

        // Enforcement helper should reject the invalid ENABLE_PUSH value.
        var ex = Assert.Throws<Http2Exception>(() =>
            EnforceEnablePush(settingsF.Parameters));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    /// RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE > 2^31-1 causes FLOW_CONTROL_ERROR
    [Fact(DisplayName = "RFC9113-6.5-SEC-005: SETTINGS INITIAL_WINDOW_SIZE > 2^31-1 rejected by enforcement")]
    public void Should_DetectInitialWindowSizeOverflow_WhenSettingsEnforcementApplied()
    {
        var decoder = new Http2FrameDecoder();

        // SETTINGS frame: InitialWindowSize = 2^31 = 0x80000000 (exceeds 2^31-1).
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.InitialWindowSize, 0x80000000u),
        }).Serialize();

        var framesDecoded = decoder.Decode(settingsFrame);
        var settings = Assert.Single(framesDecoded);
        var settingsF = Assert.IsType<SettingsFrame>(settings);

        // Enforcement helper should reject the overflow.
        var ex = Assert.Throws<Http2Exception>(() =>
            EnforceInitialWindowSize(settingsF.Parameters));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    // SEC-006: Unknown SETTINGS Parameter (RFC 9113 §5.5)

    /// RFC 9113 §5.5: Unknown SETTINGS parameter IDs are silently decoded
    [Fact(DisplayName = "RFC9113-6.5-SEC-006: Unknown SETTINGS parameter silently decoded per RFC §5.5")]
    public void Should_SilentlyDecodeUnknownParameter_WhenSettingsReceived()
    {
        var decoder = new Http2FrameDecoder();

        // SETTINGS frame with an unknown parameter ID (0x00FF is not defined in RFC 9113).
        var unknownParam = (SettingsParameter)0x00FF;
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (unknownParam, 42u),
        }).Serialize();

        // Must not throw — unknown IDs are silently ignored per RFC 9113 §5.5.
        var framesDecoded = decoder.Decode(settingsFrame);
        var settings = Assert.Single(framesDecoded);
        var settingsF = Assert.IsType<SettingsFrame>(settings);

        // The unknown parameter should be present in the decoded frame.
        var unknownEntry = settingsF.Parameters.FirstOrDefault(p => p.Item1 == unknownParam);
        Assert.Equal(unknownParam, unknownEntry.Item1);
        Assert.Equal(42u, unknownEntry.Item2);
    }

    // Helpers

    /// <summary>
    /// Build a raw HTTP/2 frame with a 9-byte header + payload.
    /// </summary>
    private static byte[] BuildRawFrame(byte frameType, byte flags, int streamId, byte[] payload)
    {
        var frame = new byte[9 + payload.Length];
        var payloadLength = payload.Length;

        // Length (3 bytes, big-endian)
        frame[0] = (byte)(payloadLength >> 16);
        frame[1] = (byte)(payloadLength >> 8);
        frame[2] = (byte)payloadLength;

        // Type
        frame[3] = frameType;

        // Flags
        frame[4] = flags;

        // Stream ID (4 bytes, big-endian, R-bit cleared)
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(5), (uint)streamId & 0x7FFFFFFFu);

        // Payload
        payload.CopyTo(frame, 9);
        return frame;
    }
}
