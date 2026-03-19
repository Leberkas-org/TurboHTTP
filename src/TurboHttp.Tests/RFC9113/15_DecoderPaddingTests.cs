using System;
using System.Buffers.Binary;
using TurboHttp.Protocol.RFC9113;

namespace TurboHttp.Tests.RFC9113;

/// <summary>
/// Tests for PADDED flag handling in DATA (§6.1) and HEADERS (§6.2) frames.
/// Covers RFC 9113 SHOULD-level requirements for padding stripping and error detection.
/// </summary>
public sealed class Http2DecoderPaddingTests
{
    private readonly Http2FrameDecoder _decoder = new();

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a raw DATA frame with the PADDED flag set.
    /// Layout: [9-byte header] [padLength:1] [data] [padding]
    /// </summary>
    private static byte[] BuildPaddedDataFrame(int streamId, byte[] data, byte padLength, bool endStream = false)
    {
        var payloadLength = 1 + data.Length + padLength;
        var buf = new byte[9 + payloadLength];
        var span = buf.AsSpan();

        // Frame header
        span[0] = (byte)(payloadLength >> 16);
        span[1] = (byte)(payloadLength >> 8);
        span[2] = (byte)payloadLength;
        span[3] = (byte)FrameType.Data;
        span[4] = (byte)((endStream ? 0x01 : 0x00) | 0x08); // END_STREAM | PADDED
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)streamId & 0x7FFFFFFFu);

        // Payload
        span[9] = padLength;
        data.CopyTo(span[10..]);
        // Padding bytes are already zero-initialized

        return buf;
    }

    /// <summary>
    /// Builds a raw HEADERS frame with the PADDED flag set.
    /// Layout: [9-byte header] [padLength:1] [headerBlock] [padding]
    /// </summary>
    private static byte[] BuildPaddedHeadersFrame(int streamId, byte[] headerBlock, byte padLength,
        bool endStream = false, bool endHeaders = true, bool priority = false,
        int streamDep = 0, byte weight = 0)
    {
        var priorityLen = priority ? 5 : 0;
        var payloadLength = 1 + priorityLen + headerBlock.Length + padLength;
        var buf = new byte[9 + payloadLength];
        var span = buf.AsSpan();

        // Frame header
        span[0] = (byte)(payloadLength >> 16);
        span[1] = (byte)(payloadLength >> 8);
        span[2] = (byte)payloadLength;
        span[3] = (byte)FrameType.Headers;
        byte flags = 0x08; // PADDED
        if (endStream)
        {
            flags |= 0x01;
        }
        if (endHeaders)
        {
            flags |= 0x04;
        }
        if (priority)
        {
            flags |= 0x20;
        }
        span[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)streamId & 0x7FFFFFFFu);

        // Payload: pad length
        var offset = 9;
        span[offset++] = padLength;

        // Priority fields (optional)
        if (priority)
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[offset..], (uint)streamDep & 0x7FFFFFFFu);
            offset += 4;
            span[offset++] = weight;
        }

        // Header block
        headerBlock.CopyTo(span[offset..]);
        // Padding bytes are already zero-initialized

        return buf;
    }

    /// <summary>
    /// Builds a raw DATA frame where padLength exceeds the remaining payload.
    /// </summary>
    private static byte[] BuildInvalidPaddedDataFrame(int streamId, byte padLengthField, int totalPayloadLength)
    {
        var buf = new byte[9 + totalPayloadLength];
        var span = buf.AsSpan();

        span[0] = (byte)(totalPayloadLength >> 16);
        span[1] = (byte)(totalPayloadLength >> 8);
        span[2] = (byte)totalPayloadLength;
        span[3] = (byte)FrameType.Data;
        span[4] = 0x08; // PADDED
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)streamId & 0x7FFFFFFFu);

        span[9] = padLengthField;

        return buf;
    }

    // ── DATA §6.1 — PADDED flag ────────────────────────────────────────────

    [Fact(DisplayName = "RFC9113-6.1-PAD-001: DATA with PADDED flag strips padding, payload correct")]
    public void Decode_DataPadded_StripsPadding()
    {
        var payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var raw = BuildPaddedDataFrame(streamId: 1, data: payload, padLength: 3);

        var frames = _decoder.Decode(raw);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(1, df.StreamId);
        Assert.Equal(payload, df.Data.ToArray());
        Assert.False(df.EndStream);
    }

    [Fact(DisplayName = "RFC9113-6.1-PAD-002: DATA with maximum pad length (254 bytes)")]
    public void Decode_DataPadded_MaxPadLength()
    {
        var payload = new byte[] { 0x01 };
        byte padLength = 254;
        var raw = BuildPaddedDataFrame(streamId: 3, data: payload, padLength: padLength, endStream: true);

        var frames = _decoder.Decode(raw);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(3, df.StreamId);
        Assert.Equal(payload, df.Data.ToArray());
        Assert.True(df.EndStream);
    }

    [Fact(DisplayName = "RFC9113-6.1-PAD-003: DATA with pad_length > frame_length triggers PROTOCOL_ERROR")]
    public void Decode_DataPadded_PadLengthExceedsPayload_Throws()
    {
        // Total payload = 2 bytes (padLength field + 1 byte), but padLength claims 5
        var raw = BuildInvalidPaddedDataFrame(streamId: 1, padLengthField: 5, totalPayloadLength: 2);

        var ex = Assert.Throws<Http2Exception>(() => _decoder.Decode(raw));
        Assert.Contains("pad_length exceeds payload size", ex.Message);
    }

    [Fact(DisplayName = "RFC9113-6.1-PAD-004: DATA with PADDED flag and zero-length data")]
    public void Decode_DataPadded_ZeroLengthData()
    {
        var payload = Array.Empty<byte>();
        var raw = BuildPaddedDataFrame(streamId: 5, data: payload, padLength: 10);

        var frames = _decoder.Decode(raw);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Empty(df.Data.ToArray());
    }

    [Fact(DisplayName = "RFC9113-6.1-PAD-005: DATA with PADDED flag and zero padding")]
    public void Decode_DataPadded_ZeroPadding()
    {
        var payload = new byte[] { 0xDE, 0xAD };
        var raw = BuildPaddedDataFrame(streamId: 7, data: payload, padLength: 0);

        var frames = _decoder.Decode(raw);

        Assert.Single(frames);
        var df = Assert.IsType<DataFrame>(frames[0]);
        Assert.Equal(payload, df.Data.ToArray());
    }

    // ── HEADERS §6.2 — PADDED flag ─────────────────────────────────────────

    [Fact(DisplayName = "RFC9113-6.2-PAD-001: HEADERS with PADDED flag strips padding, header block correct")]
    public void Decode_HeadersPadded_StripsPadding()
    {
        var headerBlock = new byte[] { 0x82, 0x86, 0x84 }; // some HPACK bytes
        var raw = BuildPaddedHeadersFrame(streamId: 1, headerBlock: headerBlock, padLength: 5);

        var frames = _decoder.Decode(raw);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(1, hf.StreamId);
        Assert.Equal(headerBlock, hf.HeaderBlockFragment.ToArray());
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-6.2-PAD-002: HEADERS with PRIORITY flag skips priority fields, headers correct")]
    public void Decode_HeadersPriority_SkipsPriorityFields()
    {
        var headerBlock = new byte[] { 0x82, 0x86 };
        // Build without padding, just PRIORITY
        var priorityLen = 5;
        var payloadLength = priorityLen + headerBlock.Length;
        var buf = new byte[9 + payloadLength];
        var span = buf.AsSpan();

        span[0] = (byte)(payloadLength >> 16);
        span[1] = (byte)(payloadLength >> 8);
        span[2] = (byte)payloadLength;
        span[3] = (byte)FrameType.Headers;
        span[4] = 0x04 | 0x20; // END_HEADERS | PRIORITY
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], 1u); // stream ID 1

        // Priority: stream dep=0, weight=15
        BinaryPrimitives.WriteUInt32BigEndian(span[9..], 0u);
        span[13] = 15;
        headerBlock.CopyTo(span[14..]);

        var frames = _decoder.Decode(buf);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(headerBlock, hf.HeaderBlockFragment.ToArray());
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-6.2-PAD-003: HEADERS with PADDED + PRIORITY combined")]
    public void Decode_HeadersPaddedAndPriority_StripsAll()
    {
        var headerBlock = new byte[] { 0x82, 0x86, 0x84, 0x41, 0x8A };
        var raw = BuildPaddedHeadersFrame(
            streamId: 3,
            headerBlock: headerBlock,
            padLength: 7,
            endStream: true,
            endHeaders: true,
            priority: true,
            streamDep: 0,
            weight: 16);

        var frames = _decoder.Decode(raw);

        Assert.Single(frames);
        var hf = Assert.IsType<HeadersFrame>(frames[0]);
        Assert.Equal(3, hf.StreamId);
        Assert.Equal(headerBlock, hf.HeaderBlockFragment.ToArray());
        Assert.True(hf.EndStream);
        Assert.True(hf.EndHeaders);
    }

    [Fact(DisplayName = "RFC9113-6.2-PAD-004: HEADERS with pad_length > frame_length triggers PROTOCOL_ERROR")]
    public void Decode_HeadersPadded_PadLengthExceedsPayload_Throws()
    {
        // Build a HEADERS frame with PADDED flag where padLength is too large
        // Total payload = 2 bytes, padLength claims 5 → exceeds
        var buf = new byte[9 + 2];
        var span = buf.AsSpan();
        span[0] = 0;
        span[1] = 0;
        span[2] = 2; // payload length = 2
        span[3] = (byte)FrameType.Headers;
        span[4] = 0x08 | 0x04; // PADDED | END_HEADERS
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], 1u); // stream ID
        span[9] = 5; // padLength = 5, but only 1 byte remains → error

        var ex = Assert.Throws<Http2Exception>(() => _decoder.Decode(buf));
        Assert.Contains("pad_length exceeds payload size", ex.Message);
    }
}
