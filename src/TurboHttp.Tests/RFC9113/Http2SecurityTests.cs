using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests.RFC9113;

public sealed class Http2SecurityTests
{
    // ── CONTINUATION Frame Flood ──────────────────────────────────────────────

    [Fact(DisplayName = "SEC-h2-003: Excessive CONTINUATION frames rejected")]
    public void Should_ThrowHttp2Exception_When_1000ContinuationFramesReceived()
    {
        var session = new Http2ProtocolSession();

        // HEADERS frame on stream 1, no END_HEADERS (flags=0x0).
        // Payload: one valid HPACK byte (0x88 = indexed :status: 200).
        var headersPayload = new byte[] { 0x88 };
        var headersFrame = BuildRawFrame(frameType: 0x1, flags: 0x0, streamId: 1, headersPayload);

        // 999 CONTINUATION frames without END_HEADERS (flags=0x0, empty payload).
        // These should all be accepted without exception.
        var continuationNoEnd = BuildRawFrame(frameType: 0x9, flags: 0x0, streamId: 1, []);
        var continuations999 = new byte[999 * continuationNoEnd.Length];
        for (var i = 0; i < 999; i++)
        {
            continuationNoEnd.CopyTo(continuations999, i * continuationNoEnd.Length);
        }

        // Feed HEADERS + 999 CONTINUATION frames — no exception yet.
        var chunk1 = new byte[headersFrame.Length + continuations999.Length];
        headersFrame.CopyTo(chunk1, 0);
        continuations999.CopyTo(chunk1, headersFrame.Length);
        session.Process(chunk1);

        // The 1000th CONTINUATION frame should trigger the protection.
        var continuation1000 = BuildRawFrame(frameType: 0x9, flags: 0x0, streamId: 1, []);
        var ex = Assert.Throws<Http2Exception>(() =>
            session.Process(continuation1000));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Rapid Reset Stream Protection (CVE-2023-44487) ───────────────────────

    [Fact(DisplayName = "SEC-h2-004: Rapid RST_STREAM cycling triggers protection (CVE-2023-44487)")]
    public void Should_ThrowHttp2Exception_When_101RstStreamFramesReceived()
    {
        var session = new Http2ProtocolSession();

        // Send 100 RST_STREAM frames on distinct stream IDs — should all be accepted.
        // RST_STREAM payload: 4 bytes error code (NO_ERROR = 0x0).
        var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        for (var i = 0; i < 100; i++) // 100 frames on stream IDs 1, 3, 5, ..., 199
        {
            var rst = BuildRawFrame(frameType: 0x3, flags: 0x0, streamId: 2 * i + 1, errorCode);
            session.Process(rst);
        }

        // The 101st RST_STREAM frame should trigger rapid-reset protection.
        var rst101 = BuildRawFrame(frameType: 0x3, flags: 0x0, streamId: 201, errorCode);
        var ex = Assert.Throws<Http2Exception>(() => session.Process(rst101));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Excessive Zero-Length DATA Frame Protection ───────────────────────────

    [Fact(DisplayName = "SEC-h2-005: Excessive zero-length DATA frames rejected")]
    public void Should_ThrowHttp2Exception_When_10001EmptyDataFramesReceived()
    {
        var session = new Http2ProtocolSession();

        // Build a buffer with 10001 empty DATA frames on stream 1.
        // Each frame: 9-byte header + 0-byte payload = 9 bytes.
        const int count = 10001;
        var emptyData = BuildRawFrame(frameType: 0x0, flags: 0x0, streamId: 1, []);
        var allFrames = new byte[count * emptyData.Length];
        for (var i = 0; i < count; i++)
        {
            emptyData.CopyTo(allFrames, i * emptyData.Length);
        }

        // Feed all frames — the 10001st empty DATA frame should throw.
        var ex = Assert.Throws<Http2Exception>(() => session.Process(allFrames));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── SETTINGS Validation ───────────────────────────────────────────────────

    [Fact(DisplayName = "SEC-h2-006: SETTINGS_ENABLE_PUSH value >1 causes PROTOCOL_ERROR")]
    public void Should_ThrowHttp2Exception_When_EnablePushExceedsOne()
    {
        var session = new Http2ProtocolSession();

        // SETTINGS frame: EnablePush = 2 (invalid — only 0 or 1 are valid).
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.EnablePush, 2u),
        }).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(settingsFrame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "SEC-h2-007: SETTINGS_INITIAL_WINDOW_SIZE >2^31-1 causes FLOW_CONTROL_ERROR")]
    public void Should_ThrowHttp2Exception_When_InitialWindowSizeExceedsMax()
    {
        var session = new Http2ProtocolSession();

        // SETTINGS frame: InitialWindowSize = 2^31 = 0x80000000 (exceeds 2^31-1).
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.InitialWindowSize, 0x80000000u),
        }).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => session.Process(settingsFrame));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "SEC-h2-008: Unknown SETTINGS ID silently ignored")]
    public void Should_NotThrow_When_UnknownSettingsIdReceived()
    {
        var session = new Http2ProtocolSession();

        // SETTINGS frame with an unknown parameter ID (0x00FF is not defined in RFC 7540).
        var unknownParam = (SettingsParameter)0x00FF;
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (unknownParam, 42u),
        }).Serialize();

        // Must not throw — unknown IDs are silently ignored per RFC 7540 §4.1.
        Assert.NotEmpty(session.Process(settingsFrame));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
