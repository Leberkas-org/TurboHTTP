using System.Buffers;
using System.Buffers.Binary;

namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Builds the HTTP/2 connection preface (RFC 9113 §3.4):
/// magic octets + SETTINGS frame + optional WINDOW_UPDATE.
/// Extracted from Http20EncoderStage for independent testability.
/// </summary>
public static class PrefaceBuilder
{
    public static (IMemoryOwner<byte> Owner, int Length) Build(int windowSize)
    {
        const int frameHeaderSize = 9;
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        var settingsParams = new (SettingsParameter, uint)[]
        {
            (SettingsParameter.HeaderTableSize, 4096),
            (SettingsParameter.EnablePush, 0),
            (SettingsParameter.InitialWindowSize, (uint)windowSize),
            (SettingsParameter.MaxFrameSize, 16384),
        };

        var settingsPayloadSize = settingsParams.Length * 6;
        var needsWindowUpdate = windowSize > 65535;
        const int windowUpdatePayloadSize = 4;
        var totalSize = magic.Length + frameHeaderSize + settingsPayloadSize;
        if (needsWindowUpdate)
        {
            totalSize += frameHeaderSize + windowUpdatePayloadSize;
        }

        var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var result = owner.Memory.Span;
        magic.CopyTo(result);
        var offset = magic.Length;

        var frameHeaderSpan = result.Slice(offset, frameHeaderSize);
        frameHeaderSpan[0] = (byte)(settingsPayloadSize >> 16);
        frameHeaderSpan[1] = (byte)(settingsPayloadSize >> 8);
        frameHeaderSpan[2] = (byte)settingsPayloadSize;
        frameHeaderSpan[3] = (byte)FrameType.Settings;
        frameHeaderSpan[4] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(frameHeaderSpan[5..], 0);
        offset += frameHeaderSize;

        var settingsSpan = result.Slice(offset, settingsPayloadSize);
        foreach (var (key, val) in settingsParams)
        {
            BinaryPrimitives.WriteUInt16BigEndian(settingsSpan, (ushort)key);
            BinaryPrimitives.WriteUInt32BigEndian(settingsSpan[2..], val);
            settingsSpan = settingsSpan[6..];
        }

        offset += settingsPayloadSize;

        if (needsWindowUpdate)
        {
            var windowUpdateIncrement = windowSize - 65535;
            var winSpan = result[offset..];
            winSpan[0] = 0;
            winSpan[1] = 0;
            winSpan[2] = windowUpdatePayloadSize;
            winSpan[3] = (byte)FrameType.WindowUpdate;
            winSpan[4] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(winSpan[5..], 0);
            BinaryPrimitives.WriteUInt32BigEndian(winSpan[9..], (uint)windowUpdateIncrement);
        }

        return (owner, totalSize);
    }
}
