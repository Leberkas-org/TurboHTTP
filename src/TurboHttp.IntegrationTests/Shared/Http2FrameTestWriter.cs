using System.Buffers.Binary;
using TurboHttp.Protocol;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Test helper for manually writing HTTP/2 frame bytes.
/// Used to create invalid or edge-case frames for decoder error testing.
/// NOT for production use — bypass normal frame validation.
/// </summary>
internal static class Http2FrameTestWriter
{
    /// <summary>
    /// Writes a DATA frame header + payload to buffer.
    /// Used to create invalid frames (e.g., streamId=0) for error testing.
    /// </summary>
    public static void WriteDataFrame(byte[] buffer, int streamId, ReadOnlySpan<byte> payload, bool endStream)
    {
        if (buffer.Length < 9 + payload.Length)
            throw new ArgumentException("Buffer too small");

        // Frame header: length(24) + type(8) + flags(8) + streamId(31)
        buffer[0] = (byte)(payload.Length >> 16);
        buffer[1] = (byte)(payload.Length >> 8);
        buffer[2] = (byte)payload.Length;
        buffer[3] = (byte)FrameType.Data;
        buffer[4] = endStream ? (byte)0x01 : (byte)0x00;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(5), (uint)streamId);

        // Payload
        payload.CopyTo(buffer.AsSpan(9));
    }

    /// <summary>
    /// Writes a HEADERS frame header + payload to buffer.
    /// Used to create frames with specific streamIds for error testing.
    /// </summary>
    public static void WriteHeadersFrame(byte[] buffer, int streamId, ReadOnlySpan<byte> headerBlock, bool endStream, bool endHeaders)
    {
        if (buffer.Length < 9 + headerBlock.Length)
            throw new ArgumentException("Buffer too small");

        // Frame header
        buffer[0] = (byte)(headerBlock.Length >> 16);
        buffer[1] = (byte)(headerBlock.Length >> 8);
        buffer[2] = (byte)headerBlock.Length;
        buffer[3] = (byte)FrameType.Headers;
        buffer[4] = (byte)((endHeaders ? 0x04 : 0x00) | (endStream ? 0x01 : 0x00));
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(5), (uint)streamId);

        // Payload
        headerBlock.CopyTo(buffer.AsSpan(9));
    }

    /// <summary>
    /// Writes a raw frame header to buffer.
    /// Used for creating frames with invalid types or combinations.
    /// </summary>
    public static void WriteFrameHeader(byte[] buffer, int payloadLength, FrameType frameType, byte flags, int streamId)
    {
        if (buffer.Length < 9)
            throw new ArgumentException("Buffer too small for frame header");

        buffer[0] = (byte)(payloadLength >> 16);
        buffer[1] = (byte)(payloadLength >> 8);
        buffer[2] = (byte)payloadLength;
        buffer[3] = (byte)frameType;
        buffer[4] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(5), (uint)streamId);
    }
}
