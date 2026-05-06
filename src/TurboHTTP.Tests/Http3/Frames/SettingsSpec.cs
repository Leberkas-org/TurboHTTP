using TurboHTTP.Protocol.Http3;

namespace TurboHTTP.Tests.Http3.Frames;

public sealed class SettingsFrameSpec
{
    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    [InlineData(SettingsIdentifier.QpackMaxTableCapacity, 0x01)]
    [InlineData(SettingsIdentifier.MaxFieldSectionSize, 0x06)]
    [InlineData(SettingsIdentifier.QpackBlockedStreams, 0x07)]
    public void Settings_should_have_correct_values_for_well_known_ids(long actual, long expected)
    {
        Assert.Equal(expected, actual);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(SettingsIdentifier.ReservedH2EnablePush)]
    [InlineData(SettingsIdentifier.ReservedH2MaxConcurrentStreams)]
    [InlineData(SettingsIdentifier.ReservedH2InitialWindowSize)]
    [InlineData(SettingsIdentifier.ReservedH2MaxFrameSize)]
    public void Settings_should_detect_reserved_http2_settings(long identifier)
    {
        Assert.True(SettingsIdentifier.IsReservedH2Setting(identifier));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_roundtrip_serialize_deserialize()
    {
        var settings = new Settings();
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, 8192);
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, 4096);
        settings.Set(SettingsIdentifier.QpackBlockedStreams, 16);

        var payload = settings.Serialize();
        var restored = Settings.Deserialize(payload);

        Assert.Equal(8192, restored.MaxFieldSectionSize);
        Assert.Equal(4096, restored.QpackMaxTableCapacity);
        Assert.Equal(16, restored.QpackBlockedStreams);
        Assert.Equal(3, restored.AllParameters.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_serialize_to_zero_bytes_when_empty()
    {
        var settings = new Settings();
        var payload = settings.Serialize();
        Assert.Empty(payload);

        var restored = Settings.Deserialize(payload);
        Assert.Empty(restored.AllParameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_preserve_unknown_settings_through_roundtrip()
    {
        var settings = new Settings();
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, 1024);
        settings.Set(0x33, 999); // unknown extension setting
        settings.Set(0xFF, 42); // another unknown

        var payload = settings.Serialize();
        var restored = Settings.Deserialize(payload);

        Assert.Equal(1024, restored.MaxFieldSectionSize);
        Assert.Equal(999, restored[0x33]);
        Assert.Equal(42, restored[0xFF]);
        Assert.Equal(3, restored.AllParameters.Count);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(0x02)] // SETTINGS_ENABLE_PUSH
    [InlineData(0x03)] // SETTINGS_MAX_CONCURRENT_STREAMS
    [InlineData(0x04)] // SETTINGS_INITIAL_WINDOW_SIZE
    [InlineData(0x05)] // SETTINGS_MAX_FRAME_SIZE
    public void Settings_should_throw_when_setting_reserved_http2_identifier(long reservedId)
    {
        var settings = new Settings();
        var ex = Assert.Throws<Http3Exception>(() => settings.Set(reservedId, 0));
        Assert.Equal(ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    public void Settings_should_throw_when_deserializing_reserved_http2_identifier(long reservedId)
    {
        var payload = BuildSingleSettingPayload(reservedId, 0);
        var ex = Assert.Throws<Http3Exception>(() => Settings.Deserialize(payload));
        Assert.Equal(ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_throw_when_deserializing_duplicate_identifier()
    {
        var payload = BuildDuplicatePayload();
        var ex = Assert.Throws<Http3Exception>(() => Settings.Deserialize(payload));
        Assert.Equal(ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildSingleSettingPayload(long identifier, long value)
    {
        var buf = new byte[16];
        var pos = 0;
        pos += QuicVarInt.Encode(identifier, buf.AsSpan(pos));
        pos += QuicVarInt.Encode(value, buf.AsSpan(pos));
        return buf[..pos];
    }

    private static byte[] BuildDuplicatePayload()
    {
        // Two entries with same identifier 0x06
        var buf = new byte[16];
        var span = buf.AsSpan();
        var pos = 0;

        pos += QuicVarInt.Encode(0x06, span[pos..]);
        pos += QuicVarInt.Encode(100, span[pos..]);
        pos += QuicVarInt.Encode(0x06, span[pos..]);
        pos += QuicVarInt.Encode(200, span[pos..]);

        return buf[..pos];
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_return_defaults_when_absent()
    {
        var settings = new Settings();
        Assert.Null(settings.MaxFieldSectionSize);
        Assert.Equal(0, settings.QpackMaxTableCapacity);
        Assert.Equal(0, settings.QpackBlockedStreams);
        Assert.Null(settings[0x99]); // unknown absent setting
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_should_create_valid_frame_via_toframe()
    {
        var settings = new Settings();
        settings.Set(SettingsIdentifier.MaxFieldSectionSize, 16384);
        settings.Set(SettingsIdentifier.QpackMaxTableCapacity, 256);

        var frame = settings.ToFrame();
        Assert.Equal(FrameType.Settings, frame.Type);
        Assert.Equal(2, frame.Parameters.Count);

        // Serialize frame and verify it round-trips
        var bytes = frame.Serialize();
        Assert.Equal(frame.SerializedSize, bytes.Length);
    }
}