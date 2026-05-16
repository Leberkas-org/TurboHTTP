using System.Buffers;

namespace TurboHTTP.Protocol.Syntax.Http2;

internal static class PrefaceBuilder
{
    public static (IMemoryOwner<byte> Owner, int Length) Build(
        int initialWindowSize,
        int headerTableSize = 4096,
        int maxFrameSize = 16384)
    {
        const int frameHeaderSize = 9;
        var magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8;

        var settingsParams = new (SettingsParameter, uint)[]
        {
            (SettingsParameter.HeaderTableSize, (uint)headerTableSize),
            (SettingsParameter.EnablePush, 0),
            (SettingsParameter.InitialWindowSize, (uint)initialWindowSize),
            (SettingsParameter.MaxFrameSize, (uint)maxFrameSize),
        };

        var settingsPayloadSize = settingsParams.Length * 6;
        var needsWindowUpdate = initialWindowSize > 65535;
        const int windowUpdatePayloadSize = 4;
        var totalSize = magic.Length + frameHeaderSize + settingsPayloadSize;
        if (needsWindowUpdate)
        {
            totalSize += frameHeaderSize + windowUpdatePayloadSize;
        }

        var owner = MemoryPool<byte>.Shared.Rent(totalSize);
        var w = SpanWriter.Create(owner.Memory.Span);

        w.WriteBytes(magic);

        w.WriteUInt24BigEndian(settingsPayloadSize);
        w.WriteByte((byte)FrameType.Settings);
        w.WriteByte(0);
        w.WriteUInt32BigEndian(0);

        foreach (var (key, val) in settingsParams)
        {
            w.WriteUInt16BigEndian((ushort)key);
            w.WriteUInt32BigEndian(val);
        }

        if (!needsWindowUpdate) return (owner, totalSize);

        var windowUpdateIncrement = initialWindowSize - 65535;
        w.WriteUInt24BigEndian(windowUpdatePayloadSize);
        w.WriteByte((byte)FrameType.WindowUpdate);
        w.WriteByte(0);
        w.WriteUInt32BigEndian(0);
        w.WriteUInt32BigEndian((uint)windowUpdateIncrement);

        return (owner, totalSize);
    }
}