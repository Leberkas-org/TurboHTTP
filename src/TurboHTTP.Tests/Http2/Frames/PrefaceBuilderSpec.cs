using System.Buffers.Binary;
using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

public sealed class PrefaceBuilderSpec
{
    private const int MagicLength = 24;
    private const int FrameHeaderSize = 9;
    private const int SettingSize = 6;
    private const int SettingsCount = 4; // HeaderTableSize, EnablePush, InitialWindowSize, MaxFrameSize

    private static ReadOnlySpan<byte> ParseSettings(ReadOnlySpan<byte> preface, out bool hasWindowUpdate)
    {
        var settingsPayload = preface.Slice(MagicLength + FrameHeaderSize, SettingsCount * SettingSize);
        hasWindowUpdate = preface.Length > MagicLength + FrameHeaderSize + SettingsCount * SettingSize;
        return settingsPayload;
    }

    private static (SettingsParameter Key, uint Value) ReadSetting(ReadOnlySpan<byte> span, int index)
    {
        var offset = index * SettingSize;
        var key = (SettingsParameter)BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
        var value = BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 2)..]);
        return (key, value);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_default_header_table_size_4096()
    {
        var (owner, length) = PrefaceBuilder.Build(65535);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 0);

        Assert.Equal(SettingsParameter.HeaderTableSize, key);
        Assert.Equal(4096u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_custom_header_table_size_when_specified()
    {
        var (owner, length) = PrefaceBuilder.Build(65535, headerTableSize: 8192);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 0);

        Assert.Equal(SettingsParameter.HeaderTableSize, key);
        Assert.Equal(8192u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_custom_max_frame_size_when_specified()
    {
        var (owner, length) = PrefaceBuilder.Build(65535, maxFrameSize: 32768);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 3);

        Assert.Equal(SettingsParameter.MaxFrameSize, key);
        Assert.Equal(32768u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-3.4")]
    public void PrefaceBuilder_should_emit_enable_push_0()
    {
        var (owner, length) = PrefaceBuilder.Build(65535);
        var span = owner.Memory.Span[..length];
        var settings = ParseSettings(span, out _);

        var (key, value) = ReadSetting(settings, 1);

        Assert.Equal(SettingsParameter.EnablePush, key);
        Assert.Equal(0u, value);
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void PrefaceBuilder_should_include_window_update_when_initial_window_exceeds_65535()
    {
        const int largeWindow = 64 * 1024 * 1024;
        var (owner, length) = PrefaceBuilder.Build(largeWindow);
        var span = owner.Memory.Span[..length];

        ParseSettings(span, out var hasWindowUpdate);

        Assert.True(hasWindowUpdate, "Expected WINDOW_UPDATE frame for window > 65535");
        owner.Dispose();
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void PrefaceBuilder_should_not_include_window_update_when_initial_window_is_65535()
    {
        var (owner, length) = PrefaceBuilder.Build(65535);
        var span = owner.Memory.Span[..length];

        ParseSettings(span, out var hasWindowUpdate);

        Assert.False(hasWindowUpdate, "No WINDOW_UPDATE expected when window == 65535");
        owner.Dispose();
    }
}