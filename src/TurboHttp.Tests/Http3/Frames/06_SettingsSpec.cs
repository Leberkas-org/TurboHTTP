using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Frames;

public sealed class SettingsFrameSpec
{

    [Theory]
    [Trait("RFC", "RFC9114-7.2.4")]
    [InlineData(Http3SettingId.QpackMaxTableCapacity, 0x01)]
    [InlineData(Http3SettingId.MaxFieldSectionSize, 0x06)]
    [InlineData(Http3SettingId.QpackBlockedStreams, 0x07)]
    public void WellKnownSettingIds_HaveCorrectValues(long actual, long expected)
    {
        Assert.Equal(expected, actual);
    }

    [Theory]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(Http3SettingId.ReservedH2EnablePush)]
    [InlineData(Http3SettingId.ReservedH2MaxConcurrentStreams)]
    [InlineData(Http3SettingId.ReservedH2InitialWindowSize)]
    [InlineData(Http3SettingId.ReservedH2MaxFrameSize)]
    public void ReservedH2Settings_AreDetected(long identifier)
    {
        Assert.True(Http3SettingId.IsReservedH2Setting(identifier));
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Settings_SerializeDeserialize_Roundtrip()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 8192);
        settings.Set(Http3SettingId.QpackMaxTableCapacity, 4096);
        settings.Set(Http3SettingId.QpackBlockedStreams, 16);

        var payload = settings.Serialize();
        var restored = Http3Settings.Deserialize(payload);

        Assert.Equal(8192, restored.MaxFieldSectionSize);
        Assert.Equal(4096, restored.QpackMaxTableCapacity);
        Assert.Equal(16, restored.QpackBlockedStreams);
        Assert.Equal(3, restored.AllParameters.Count);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void EmptySettings_SerializeToZeroBytes()
    {
        var settings = new Http3Settings();
        var payload = settings.Serialize();
        Assert.Empty(payload);

        var restored = Http3Settings.Deserialize(payload);
        Assert.Empty(restored.AllParameters);
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void UnknownSettings_PreservedThroughRoundTrip()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 1024);
        settings.Set(0x33, 999);   // unknown extension setting
        settings.Set(0xFF, 42);    // another unknown

        var payload = settings.Serialize();
        var restored = Http3Settings.Deserialize(payload);

        Assert.Equal(1024, restored.MaxFieldSectionSize);
        Assert.Equal(999, restored[0x33]);
        Assert.Equal(42, restored[0xFF]);
        Assert.Equal(3, restored.AllParameters.Count);
    }


    [Theory]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(0x02)] // SETTINGS_ENABLE_PUSH
    [InlineData(0x03)] // SETTINGS_MAX_CONCURRENT_STREAMS
    [InlineData(0x04)] // SETTINGS_INITIAL_WINDOW_SIZE
    [InlineData(0x05)] // SETTINGS_MAX_FRAME_SIZE
    public void Set_ReservedH2Identifier_Throws(long reservedId)
    {
        var settings = new Http3Settings();
        var ex = Assert.Throws<Http3Exception>(() => settings.Set(reservedId, 0));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("RFC", "RFC9114-7.2.4.1")]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    public void Deserialize_ReservedH2Identifier_Throws(long reservedId)
    {
        var payload = BuildSingleSettingPayload(reservedId, 0);
        var ex = Assert.Throws<Http3Exception>(() => Http3Settings.Deserialize(payload));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void Deserialize_DuplicateIdentifier_Throws()
    {
        var payload = BuildDuplicatePayload();
        var ex = Assert.Throws<Http3Exception>(() => Http3Settings.Deserialize(payload));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildSingleSettingPayload(long identifier, long value)
    {
        var buf = new byte[16];
        var pos = 0;
        pos += Protocol.Http3.QuicVarInt.Encode(identifier, buf.AsSpan(pos));
        pos += Protocol.Http3.QuicVarInt.Encode(value, buf.AsSpan(pos));
        return buf[..pos];
    }

    private static byte[] BuildDuplicatePayload()
    {
        // Two entries with same identifier 0x06
        var buf = new byte[16];
        var span = buf.AsSpan();
        var pos = 0;

        pos += Protocol.Http3.QuicVarInt.Encode(0x06, span[pos..]);
        pos += Protocol.Http3.QuicVarInt.Encode(100, span[pos..]);
        pos += Protocol.Http3.QuicVarInt.Encode(0x06, span[pos..]);
        pos += Protocol.Http3.QuicVarInt.Encode(200, span[pos..]);

        return buf[..pos];
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void AbsentSettings_ReturnDefaults()
    {
        var settings = new Http3Settings();
        Assert.Null(settings.MaxFieldSectionSize);
        Assert.Equal(0, settings.QpackMaxTableCapacity);
        Assert.Equal(0, settings.QpackBlockedStreams);
        Assert.Null(settings[0x99]); // unknown absent setting
    }


    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void ToFrame_CreatesValidFrame()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 16384);
        settings.Set(Http3SettingId.QpackMaxTableCapacity, 256);

        var frame = settings.ToFrame();
        Assert.Equal(Http3FrameType.Settings, frame.Type);
        Assert.Equal(2, frame.Parameters.Count);

        // Serialize frame and verify it round-trips
        var bytes = frame.Serialize();
        Assert.Equal(frame.SerializedSize, bytes.Length);
    }
}
