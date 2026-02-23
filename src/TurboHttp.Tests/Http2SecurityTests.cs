using System;
using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http2SecurityTests
{
    // ── HPACK String Length Limits ────────────────────────────────────────────

    [Fact(DisplayName = "SEC-h2-001: HPACK literal name exceeding limit causes COMPRESSION_ERROR")]
    public void Should_ThrowHpackException_When_LiteralNameExceedsLimit()
    {
        var hpack = new HpackDecoder();
        hpack.SetMaxStringLength(10);

        // Literal Header Field without Indexing, new name (prefix 0x00, nameIdx = 0).
        // Encode name of length 11 (exceeds limit of 10).
        var nameBytes = new byte[11];
        Array.Fill(nameBytes, (byte)'x');

        var block = new byte[2 + nameBytes.Length + 2]; // prefix + nameLen byte + name + valueLen byte + empty value
        block[0] = 0x00;          // Literal without Indexing, nameIdx=0
        block[1] = (byte)nameBytes.Length; // string length (not Huffman)
        nameBytes.CopyTo(block, 2);
        block[2 + nameBytes.Length] = 0x00; // value length = 0

        var ex = Assert.Throws<HpackException>(() => hpack.Decode(block));
        Assert.Contains("exceeds maximum", ex.Message);
    }

    [Fact(DisplayName = "SEC-h2-002: HPACK literal value exceeding limit causes COMPRESSION_ERROR")]
    public void Should_ThrowHpackException_When_LiteralValueExceedsLimit()
    {
        var hpack = new HpackDecoder();
        hpack.SetMaxStringLength(10);

        // Literal Header Field with Incremental Indexing (prefix 0x40), static name index 1 (:authority).
        // Encode value of length 11 (exceeds limit of 10).
        var valueBytes = new byte[11];
        Array.Fill(valueBytes, (byte)'v');

        var block = new byte[1 + 1 + valueBytes.Length]; // prefix(with nameIdx) + valueLen + value
        block[0] = 0x41;                    // 0x40 | 1 = literal+indexing, name from static index 1
        block[1] = (byte)valueBytes.Length; // string length (not Huffman)
        valueBytes.CopyTo(block, 2);

        var ex = Assert.Throws<HpackException>(() => hpack.Decode(block));
        Assert.Contains("exceeds maximum", ex.Message);
    }

    // ── CONTINUATION Frame Flood ──────────────────────────────────────────────

    [Fact(DisplayName = "SEC-h2-003: Excessive CONTINUATION frames rejected")]
    public void Should_ThrowHttp2Exception_When_1000ContinuationFramesReceived()
    {
        var decoder = new Http2Decoder();

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
        decoder.TryDecode(chunk1, out _);

        // The 1000th CONTINUATION frame should trigger the protection.
        var continuation1000 = BuildRawFrame(frameType: 0x9, flags: 0x0, streamId: 1, []);
        var ex = Assert.Throws<Http2Exception>(() =>
            decoder.TryDecode(continuation1000, out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Rapid Reset Stream Protection (CVE-2023-44487) ───────────────────────

    [Fact(DisplayName = "SEC-h2-004: Rapid RST_STREAM cycling triggers protection (CVE-2023-44487)")]
    public void Should_ThrowHttp2Exception_When_101RstStreamFramesReceived()
    {
        var decoder = new Http2Decoder();

        // Send 100 RST_STREAM frames on distinct stream IDs — should all be accepted.
        // RST_STREAM payload: 4 bytes error code (NO_ERROR = 0x0).
        var errorCode = new byte[] { 0x00, 0x00, 0x00, 0x00 };

        for (var i = 0; i < 100; i++) // 100 frames on stream IDs 1, 3, 5, ..., 199
        {
            var rst = BuildRawFrame(frameType: 0x3, flags: 0x0, streamId: 2 * i + 1, errorCode);
            decoder.TryDecode(rst, out _);
        }

        // The 101st RST_STREAM frame should trigger rapid-reset protection.
        var rst101 = BuildRawFrame(frameType: 0x3, flags: 0x0, streamId: 201, errorCode);
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(rst101, out _));

        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── Excessive Zero-Length DATA Frame Protection ───────────────────────────

    [Fact(DisplayName = "SEC-h2-005: Excessive zero-length DATA frames rejected")]
    public void Should_ThrowHttp2Exception_When_10001EmptyDataFramesReceived()
    {
        var decoder = new Http2Decoder();

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
        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(allFrames, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    // ── SETTINGS Validation ───────────────────────────────────────────────────

    [Fact(DisplayName = "SEC-h2-006: SETTINGS_ENABLE_PUSH value >1 causes PROTOCOL_ERROR")]
    public void Should_ThrowHttp2Exception_When_EnablePushExceedsOne()
    {
        var decoder = new Http2Decoder();

        // SETTINGS frame: EnablePush = 2 (invalid — only 0 or 1 are valid).
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.EnablePush, 2u),
        }).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(settingsFrame, out _));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "SEC-h2-007: SETTINGS_INITIAL_WINDOW_SIZE >2^31-1 causes FLOW_CONTROL_ERROR")]
    public void Should_ThrowHttp2Exception_When_InitialWindowSizeExceedsMax()
    {
        var decoder = new Http2Decoder();

        // SETTINGS frame: InitialWindowSize = 2^31 = 0x80000000 (exceeds 2^31-1).
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (SettingsParameter.InitialWindowSize, 0x80000000u),
        }).Serialize();

        var ex = Assert.Throws<Http2Exception>(() => decoder.TryDecode(settingsFrame, out _));
        Assert.Equal(Http2ErrorCode.FlowControlError, ex.ErrorCode);
    }

    [Fact(DisplayName = "SEC-h2-008: Unknown SETTINGS ID silently ignored")]
    public void Should_NotThrow_When_UnknownSettingsIdReceived()
    {
        var decoder = new Http2Decoder();

        // SETTINGS frame with an unknown parameter ID (0x00FF is not defined in RFC 7540).
        var unknownParam = (SettingsParameter)0x00FF;
        var settingsFrame = new SettingsFrame(new List<(SettingsParameter, uint)>
        {
            (unknownParam, 42u),
        }).Serialize();

        // Must not throw — unknown IDs are silently ignored per RFC 7540 §4.1.
        var decoded = decoder.TryDecode(settingsFrame, out var result);
        Assert.True(decoded);
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
