using System;
using System.Buffers.Binary;

namespace TurboHttp.Protocol;

/// <summary>
/// Utility methods for encoding HTTP/2 frames to byte arrays.
/// Used by integration tests and connection helpers that need to send control frames.
/// </summary>
public static class Http2FrameUtils
{
    /// <summary>
    /// Encodes a PING frame.
    /// </summary>
    public static byte[] EncodePing(byte[] data)
    {
        var frame = new PingFrame(data, isAck: false);
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes a PING ACK frame (response to a PING).
    /// </summary>
    public static byte[] EncodePingAck(byte[] data)
    {
        var frame = new PingFrame(data, isAck: true);
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes a GOAWAY frame.
    /// </summary>
    public static byte[] EncodeGoAway(int lastStreamId, Http2ErrorCode errorCode)
    {
        var frame = new GoAwayFrame(lastStreamId, errorCode);
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes a GOAWAY frame with an optional debug message.
    /// </summary>
    public static byte[] EncodeGoAway(int lastStreamId, Http2ErrorCode errorCode, string? debugMessage = null)
    {
        var debugData = debugMessage != null ? System.Text.Encoding.UTF8.GetBytes(debugMessage) : null;
        var frame = new GoAwayFrame(lastStreamId, errorCode, debugData);
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes an RST_STREAM frame.
    /// </summary>
    public static byte[] EncodeRstStream(int streamId, Http2ErrorCode errorCode)
    {
        var frame = new RstStreamFrame(streamId, errorCode);
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes a WINDOW_UPDATE frame.
    /// </summary>
    public static byte[] EncodeWindowUpdate(int streamId, int increment)
    {
        var frame = new WindowUpdateFrame(streamId, increment);
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes a SETTINGS frame with the given parameters.
    /// </summary>
    public static byte[] EncodeSettings(ReadOnlySpan<(SettingsParameter Key, uint Value)> parameters)
    {
        var frame = new SettingsFrame(parameters.ToArray());
        return frame.Serialize();
    }

    /// <summary>
    /// Encodes a SETTINGS ACK frame (empty SETTINGS frame with ACK flag).
    /// </summary>
    public static byte[] EncodeSettingsAck()
    {
        var frame = new SettingsFrame([], isAck: true);
        return frame.Serialize();
    }

    /// <summary>
    /// Builds HTTP/2 connection preface: magic string + default SETTINGS frame.
    /// RFC 7540 §3.5
    /// </summary>
    public static byte[] BuildConnectionPreface()
    {
        const int frameHeaderSize = 9;
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();

        // Default SETTINGS: HeaderTableSize, EnablePush, InitialWindowSize, MaxFrameSize
        var settingsParams = new (SettingsParameter, uint)[]
        {
            (SettingsParameter.HeaderTableSize, 4096),
            (SettingsParameter.EnablePush, 0),
            (SettingsParameter.InitialWindowSize, 65535),
            (SettingsParameter.MaxFrameSize, 16384),
        };

        var payloadSize = settingsParams.Length * 6;
        var result = new byte[magic.Length + frameHeaderSize + payloadSize];

        magic.CopyTo(result, 0);

        // Write SETTINGS frame header (streamId=0, no flags)
        var frameHeaderSpan = result.AsSpan(magic.Length, frameHeaderSize);
        frameHeaderSpan[0] = (byte)(payloadSize >> 16);
        frameHeaderSpan[1] = (byte)(payloadSize >> 8);
        frameHeaderSpan[2] = (byte)payloadSize;
        frameHeaderSpan[3] = (byte)FrameType.Settings;
        frameHeaderSpan[4] = 0; // flags
        BinaryPrimitives.WriteUInt32BigEndian(frameHeaderSpan[5..], 0); // streamId=0

        // Write SETTINGS parameters
        var settingsSpan = result.AsSpan(magic.Length + frameHeaderSize);
        foreach (var (key, val) in settingsParams)
        {
            BinaryPrimitives.WriteUInt16BigEndian(settingsSpan, (ushort)key);
            BinaryPrimitives.WriteUInt32BigEndian(settingsSpan[2..], val);
            settingsSpan = settingsSpan[6..];
        }

        return result;
    }
}
