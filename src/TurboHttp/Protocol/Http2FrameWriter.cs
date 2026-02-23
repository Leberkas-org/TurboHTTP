using System;
using System.Buffers.Binary;

namespace TurboHttp.Protocol;

public static class Http2FrameWriter
{
    private const int FrameHeaderSize = 9;

    public static void WriteFrameHeader(Span<byte> span, int payloadLength, FrameType type, byte flags, int streamId)
    {
        span[0] = (byte)(payloadLength >> 16);
        span[1] = (byte)(payloadLength >> 8);
        span[2] = (byte)payloadLength;
        span[3] = (byte)type;
        span[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(span[5..], (uint)streamId & 0x7FFFFFFFu);
    }

    // ========================================================================
    // DATA FRAME
    // ========================================================================
    public static int WriteDataFrame(
        Span<byte> destination,
        int streamId,
        ReadOnlySpan<byte> payload,
        bool endStream = false)
    {
        var flags = endStream ? (byte)DataFlags.EndStream : (byte)DataFlags.None;

        WriteFrameHeader(destination, payload.Length, FrameType.Data, flags, streamId);

        if (payload.Length > 0)
        {
            payload.CopyTo(destination[FrameHeaderSize..]);
        }

        return FrameHeaderSize + payload.Length;
    }

    public static void WriteDataFrameHeader(
        Span<byte> destination,
        int streamId,
        int payloadLength,
        bool endStream = false)
    {
        var flags = endStream ? (byte)DataFlags.EndStream : (byte)DataFlags.None;
        WriteFrameHeader(destination, payloadLength, FrameType.Data, flags, streamId);
    }

    // ========================================================================
    // HEADERS FRAME
    // ========================================================================
    public static int WriteHeadersFrame(Span<byte> destination, int streamId, ReadOnlySpan<byte> headerBlock,
        bool endStream = false, bool endHeaders = true)
    {
        var flags = HeadersFlags.None;
        if (endStream) flags |= HeadersFlags.EndStream;
        if (endHeaders) flags |= HeadersFlags.EndHeaders;

        WriteFrameHeader(destination, headerBlock.Length, FrameType.Headers, (byte)flags, streamId);
        headerBlock.CopyTo(destination[FrameHeaderSize..]);

        return FrameHeaderSize + headerBlock.Length;
    }

    // ========================================================================
    // CONTINUATION FRAME
    // ========================================================================
    public static int WriteContinuationFrame(Span<byte> destination, int streamId, ReadOnlySpan<byte> headerBlock,
        bool endHeaders = true)
    {
        var flags = endHeaders ? (byte)ContinuationFlags.EndHeaders : (byte)0;

        WriteFrameHeader(destination, headerBlock.Length, FrameType.Continuation, flags, streamId);
        headerBlock.CopyTo(destination[FrameHeaderSize..]);

        return FrameHeaderSize + headerBlock.Length;
    }

    // ========================================================================
    // SETTINGS FRAME
    // ========================================================================
    public static int WriteSettingsFrame(Span<byte> destination,
        ReadOnlySpan<(SettingsParameter Key, uint Value)> parameters)
    {
        var payloadSize = parameters.Length * 6;

        WriteFrameHeader(destination, payloadSize, FrameType.Settings, 0, 0);

        var span = destination[FrameHeaderSize..];
        foreach (var (key, val) in parameters)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)key);
            BinaryPrimitives.WriteUInt32BigEndian(span[2..], val);
            span = span[6..];
        }

        return FrameHeaderSize + payloadSize;
    }

    public static int WriteSettingsAck(Span<byte> destination)
    {
        WriteFrameHeader(destination, 0, FrameType.Settings, (byte)SettingsFlags.Ack, 0);
        return FrameHeaderSize;
    }

    // ========================================================================
    // RST_STREAM FRAME
    // ========================================================================
    public static int WriteRstStreamFrame(Span<byte> destination, int streamId, Http2ErrorCode errorCode)
    {
        WriteFrameHeader(destination, 4, FrameType.RstStream, 0, streamId);
        BinaryPrimitives.WriteUInt32BigEndian(destination[FrameHeaderSize..], (uint)errorCode);
        return FrameHeaderSize + 4;
    }

    // ========================================================================
    // WINDOW_UPDATE FRAME
    // ========================================================================
    public static int WriteWindowUpdateFrame(Span<byte> destination, int streamId, int increment)
    {
        if (increment is < 1 or > 0x7FFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(increment));
        }

        WriteFrameHeader(destination, 4, FrameType.WindowUpdate, 0, streamId);
        BinaryPrimitives.WriteUInt32BigEndian(destination[FrameHeaderSize..], (uint)increment & 0x7FFFFFFFu);
        return FrameHeaderSize + 4;
    }

    // ========================================================================
    // PING FRAME
    // ========================================================================
    public static int WritePingFrame(Span<byte> destination, ReadOnlySpan<byte> data, bool isAck = false)
    {
        if (data.Length != 8)
        {
            throw new ArgumentException("PING data must be exactly 8 bytes.", nameof(data));
        }

        var flags = isAck ? (byte)PingFlags.Ack : (byte)0;
        WriteFrameHeader(destination, 8, FrameType.Ping, flags, 0);
        data.CopyTo(destination[FrameHeaderSize..]);
        return FrameHeaderSize + 8;
    }

    // ========================================================================
    // GOAWAY FRAME
    // ========================================================================
    public static int WriteGoAwayFrame(Span<byte> destination, int lastStreamId, Http2ErrorCode errorCode,
        ReadOnlySpan<byte> debugData = default)
    {
        var payloadSize = 8 + debugData.Length;

        WriteFrameHeader(destination, payloadSize, FrameType.GoAway, 0, 0);

        var span = destination[FrameHeaderSize..];
        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)lastStreamId & 0x7FFFFFFFu);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)errorCode);

        if (debugData.Length > 0)
        {
            debugData.CopyTo(span[8..]);
        }

        return FrameHeaderSize + payloadSize;
    }
}