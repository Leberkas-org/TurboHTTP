using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

public sealed class SettingsExchangeTests
{
    // ───────────────────────── Client Sends SETTINGS First ─────────────────────────

    [Fact(DisplayName = "RFC-9114-6.2.1-se-001: Client sends SETTINGS as first frame on control stream")]
    public void SendSettings_IsFirstFrameOnControlStream()
    {
        var cs = new Http3ControlStream();
        var bytes = cs.OpenLocalStream();
        var span = bytes.AsSpan();

        // First: stream type = 0x00 (Control)
        Assert.True(QuicVarInt.TryDecode(span, out var streamType, out var consumed));
        Assert.Equal((long)Http3StreamType.Control, streamType);
        span = span[consumed..];

        // Second: SETTINGS frame type = 0x04
        Assert.True(QuicVarInt.TryDecode(span, out var frameType, out consumed));
        Assert.Equal((long)Http3FrameType.Settings, frameType);
    }

    [Fact(DisplayName = "RFC-9114-6.2.1-se-002: Client sends custom settings in SETTINGS frame")]
    public void SendSettings_IncludesCustomParameters()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 16384);
        settings.Set(Http3SettingId.QpackMaxTableCapacity, 4096);

        var cs = new Http3ControlStream();
        var bytes = cs.OpenLocalStream(settings);

        // Verify non-empty (stream type + frame with payload)
        Assert.True(bytes.Length > 4);
        Assert.Equal(ControlStreamState.Active, cs.LocalState);
    }

    [Fact(DisplayName = "RFC-9114-6.2.1-se-003: Client with empty settings sends zero-payload SETTINGS")]
    public void SendSettings_EmptySettings_ProducesMinimalFrame()
    {
        var cs = new Http3ControlStream();
        var bytes = cs.OpenLocalStream();
        var span = bytes.AsSpan();

        // Stream type (1 byte: 0x00)
        QuicVarInt.TryDecode(span, out _, out var consumed);
        span = span[consumed..];

        // Frame type (1 byte: 0x04)
        QuicVarInt.TryDecode(span, out _, out consumed);
        span = span[consumed..];

        // Frame length (1 byte: 0x00 for empty payload)
        QuicVarInt.TryDecode(span, out var length, out consumed);
        Assert.Equal(0, length);
    }

    [Fact(DisplayName = "RFC-9114-6.2.1-se-004: Sending settings twice is connection error")]
    public void SendSettings_Twice_ThrowsStreamCreationError()
    {
        var cs = new Http3ControlStream();
        cs.OpenLocalStream();

        var ex = Assert.Throws<Http3Exception>(() => cs.OpenLocalStream());
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    // ───────────────────────── Server SETTINGS Parsed and Applied ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7.2.4-se-005: Server SETTINGS are parsed and stored")]
    public void ReceiveServerSettings_ParsedAndStored()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingId.MaxFieldSectionSize, 8192),
            (Http3SettingId.QpackMaxTableCapacity, 2048),
            (Http3SettingId.QpackBlockedStreams, 100),
        });

        cs.OnRemoteFrame(settingsFrame);

        Assert.Equal(ControlStreamState.Active, cs.RemoteState);
        Assert.NotNull(cs.RemoteSettings);
        Assert.Equal(8192, cs.RemoteSettings!.MaxFieldSectionSize);
        Assert.Equal(2048, cs.RemoteSettings.QpackMaxTableCapacity);
        Assert.Equal(100, cs.RemoteSettings.QpackBlockedStreams);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-006: Empty server SETTINGS are valid")]
    public void ReceiveServerSettings_Empty_IsValid()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        Assert.Equal(ControlStreamState.Active, cs.RemoteState);
        Assert.NotNull(cs.RemoteSettings);
        Assert.Null(cs.RemoteSettings!.MaxFieldSectionSize);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-007: Unknown server settings are preserved (extension tolerance)")]
    public void ReceiveServerSettings_UnknownSettings_Preserved()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>
        {
            (0x33, 999),   // Unknown extension setting
            (0xFF, 42),    // Another unknown
        });

        cs.OnRemoteFrame(settingsFrame);

        Assert.Equal(999, cs.RemoteSettings![0x33]);
        Assert.Equal(42, cs.RemoteSettings[0xFF]);
    }

    [Fact(DisplayName = "RFC-9114-6.2.1-se-008: First frame on remote control stream must be SETTINGS")]
    public void ReceiveServerFrame_NotSettings_ThrowsMissingSettings()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var goaway = new Http3GoAwayFrame(0);
        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(goaway));
        Assert.Equal(Http3ErrorCode.MissingSettings, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-009: RemoteMaxFieldSectionSize reflects server setting")]
    public void RemoteMaxFieldSectionSize_ReflectsServerSetting()
    {
        var cs = new Http3ControlStream();
        Assert.Null(cs.RemoteMaxFieldSectionSize);

        cs.OnRemoteControlStreamOpened();
        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingId.MaxFieldSectionSize, 16384)
        });
        cs.OnRemoteFrame(settingsFrame);

        Assert.Equal(16384, cs.RemoteMaxFieldSectionSize);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-010: No MaxFieldSectionSize means no limit")]
    public void RemoteMaxFieldSectionSize_NotSet_ReturnsNull()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingId.QpackMaxTableCapacity, 4096)
        });
        cs.OnRemoteFrame(settingsFrame);

        Assert.Null(cs.RemoteMaxFieldSectionSize);
    }

    // ───────────────────────── SETTINGS_MAX_FIELD_SECTION_SIZE Enforced ─────────────────────────

    [Fact(DisplayName = "RFC-9114-4.2.2-se-011: Field section under limit passes validation")]
    public void ValidateFieldSectionSize_UnderLimit_Passes()
    {
        var cs = SetupWithRemoteMaxFieldSectionSize(4096);

        // Small headers: 2 fields × (name + value + 32) well under 4096
        var headers = new List<(string Name, string Value)>
        {
            (":method", "GET"),
            (":path", "/"),
        };

        cs.ValidateFieldSectionSize(headers); // Should not throw
    }

    [Fact(DisplayName = "RFC-9114-4.2.2-se-012: Field section at exact limit passes validation")]
    public void ValidateFieldSectionSize_AtExactLimit_Passes()
    {
        // Calculate exact size: 1 field with name "x" (1) + value "y" (1) + 32 = 34
        var cs = SetupWithRemoteMaxFieldSectionSize(34);

        var headers = new List<(string Name, string Value)>
        {
            ("x", "y"),
        };

        cs.ValidateFieldSectionSize(headers); // Should not throw
    }

    [Fact(DisplayName = "RFC-9114-4.2.2-se-013: Field section exceeding limit throws ExcessiveLoad")]
    public void ValidateFieldSectionSize_ExceedsLimit_ThrowsExcessiveLoad()
    {
        var cs = SetupWithRemoteMaxFieldSectionSize(100);

        // 4 headers × (7 + 1 + 32) = 160, exceeds 100
        var headers = new List<(string Name, string Value)>
        {
            (":method", "G"),
            (":path", "/"),
            (":scheme", "h"),
            (":authority", "a"),
        };

        var ex = Assert.Throws<Http3Exception>(
            () => cs.ValidateFieldSectionSize(headers));
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
        Assert.Contains("SETTINGS_MAX_FIELD_SECTION_SIZE", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-4.2.2-se-014: No MAX_FIELD_SECTION_SIZE means no limit enforced")]
    public void ValidateFieldSectionSize_NoLimit_AlwaysPasses()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();
        cs.OnRemoteFrame(new Http3SettingsFrame(new List<(long, long)>()));

        // Even a huge header list should pass
        var headers = new List<(string Name, string Value)>();
        for (var i = 0; i < 1000; i++)
        {
            headers.Add(($"x-header-{i}", new string('v', 1000)));
        }

        cs.ValidateFieldSectionSize(headers); // Should not throw
    }

    [Fact(DisplayName = "RFC-9114-4.2.2-se-015: CalculateFieldSectionSize uses 32-byte overhead per field")]
    public void CalculateFieldSectionSize_Uses32ByteOverhead()
    {
        var headers = new List<(string Name, string Value)>
        {
            ("abc", "def"),    // 3 + 3 + 32 = 38
            ("x", ""),         // 1 + 0 + 32 = 33
        };

        var size = Http3ControlStream.CalculateFieldSectionSize(headers);
        Assert.Equal(71, size);
    }

    [Fact(DisplayName = "RFC-9114-4.2.2-se-016: Empty header list has zero field section size")]
    public void CalculateFieldSectionSize_EmptyList_ReturnsZero()
    {
        var size = Http3ControlStream.CalculateFieldSectionSize(
            new List<(string Name, string Value)>());
        Assert.Equal(0, size);
    }

    [Fact(DisplayName = "RFC-9114-4.2.2-se-017: ValidateFieldSectionSize with pre-calculated size")]
    public void ValidateFieldSectionSize_PreCalculated_EnforcesLimit()
    {
        var cs = SetupWithRemoteMaxFieldSectionSize(500);

        cs.ValidateFieldSectionSize(499L); // OK
        cs.ValidateFieldSectionSize(500L); // OK (at limit)

        var ex = Assert.Throws<Http3Exception>(
            () => cs.ValidateFieldSectionSize(501L));
        Assert.Equal(Http3ErrorCode.ExcessiveLoad, ex.ErrorCode);
    }

    // ───────────────────────── HTTP/2 Settings Rejected ─────────────────────────

    [Theory(DisplayName = "RFC-9114-7.2.4.1-se-018: Reserved HTTP/2 settings in SETTINGS frame are rejected")]
    [InlineData(0x02, "ENABLE_PUSH")]
    [InlineData(0x03, "MAX_CONCURRENT_STREAMS")]
    [InlineData(0x04, "INITIAL_WINDOW_SIZE")]
    [InlineData(0x05, "MAX_FRAME_SIZE")]
    public void ReceiveServerSettings_ReservedH2Setting_Throws(long reservedId, string _)
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        // Build frame with reserved HTTP/2 setting
        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>
        {
            (reservedId, 42)
        });

        // OnRemoteFrame calls Http3Settings.Set() which rejects reserved IDs
        var ex = Assert.Throws<Http3Exception>(
            () => cs.OnRemoteFrame(settingsFrame));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory(DisplayName = "RFC-9114-7.2.4.1-se-019: RejectForbiddenH2Settings catches all reserved identifiers")]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    public void RejectForbiddenH2Settings_ReservedId_Throws(long reservedId)
    {
        var parameters = new List<(long, long)> { (reservedId, 0) };

        var ex = Assert.Throws<Http3Exception>(
            () => Http3SettingId.RejectForbiddenH2Settings(parameters));
        Assert.Equal(Http3ErrorCode.SettingsError, ex.ErrorCode);
        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HTTP/2", ex.Message);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4.1-se-020: Valid HTTP/3 settings pass H2 rejection check")]
    public void RejectForbiddenH2Settings_ValidSettings_Passes()
    {
        var parameters = new List<(long, long)>
        {
            (Http3SettingId.QpackMaxTableCapacity, 4096),
            (Http3SettingId.MaxFieldSectionSize, 8192),
            (Http3SettingId.QpackBlockedStreams, 16),
            (0x33, 999), // Unknown extension
        };

        Http3SettingId.RejectForbiddenH2Settings(parameters); // Should not throw
    }

    [Theory(DisplayName = "RFC-9114-7.2.4.1-se-021: Client cannot include HTTP/2 settings in local SETTINGS")]
    [InlineData(0x02)]
    [InlineData(0x03)]
    [InlineData(0x04)]
    [InlineData(0x05)]
    public void LocalSettings_ReservedH2Setting_ThrowsOnSet(long reservedId)
    {
        var settings = new Http3Settings();
        Assert.Throws<Http3Exception>(() => settings.Set(reservedId, 0));
    }

    // ───────────────────────── Full Exchange Lifecycle ─────────────────────────

    [Fact(DisplayName = "RFC-9114-7.2.4-se-022: Full bidirectional settings exchange succeeds")]
    public void FullExchange_BothSidesComplete()
    {
        var clientSettings = new Http3Settings();
        clientSettings.Set(Http3SettingId.MaxFieldSectionSize, 16384);
        clientSettings.Set(Http3SettingId.QpackMaxTableCapacity, 4096);

        var cs = new Http3ControlStream();

        // Step 1: Client sends SETTINGS
        Assert.Equal(ControlStreamState.NotOpened, cs.LocalState);
        var bytes = cs.OpenLocalStream(clientSettings);
        Assert.Equal(ControlStreamState.Active, cs.LocalState);
        Assert.True(bytes.Length > 0);

        // Step 2: Server control stream opens and sends SETTINGS
        Assert.NotEqual(ControlStreamState.Active, cs.RemoteState);
        cs.OnRemoteControlStreamOpened();

        var serverSettings = new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingId.MaxFieldSectionSize, 8192),
            (Http3SettingId.QpackBlockedStreams, 50),
        });
        cs.OnRemoteFrame(serverSettings);

        // Step 3: Both sides active
        Assert.Equal(ControlStreamState.Active, cs.RemoteState);
        Assert.Equal(8192, cs.RemoteMaxFieldSectionSize);
        Assert.Equal(16384, clientSettings.MaxFieldSectionSize);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-023: Server SETTINGS with duplicate identifier is rejected")]
    public void ReceiveServerSettings_DuplicateIdentifier_Throws()
    {
        // Http3Settings.Deserialize() rejects duplicates
        var payload = BuildDuplicatePayload(Http3SettingId.MaxFieldSectionSize, 1024, 2048);
        Assert.Throws<Http3Exception>(() => Http3Settings.Deserialize(payload));
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-024: Second SETTINGS frame on control stream is connection error")]
    public void ReceiveSecondSettings_ThrowsFrameUnexpected()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var ex = Assert.Throws<Http3Exception>(
            () => cs.OnRemoteFrame(settingsFrame));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC-9114-7.2.4-se-025: LocalSettings are accessible via Http3Settings before sending")]
    public void LocalSettings_AccessibleBeforeSending()
    {
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 32768);

        Assert.Equal(32768, settings.MaxFieldSectionSize);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static Http3ControlStream SetupWithRemoteMaxFieldSectionSize(long maxSize)
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();
        cs.OnRemoteFrame(new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingId.MaxFieldSectionSize, maxSize)
        }));
        return cs;
    }

    private static byte[] BuildDuplicatePayload(long identifier, long value1, long value2)
    {
        var buf = new byte[32];
        var pos = 0;
        pos += QuicVarInt.Encode(identifier, buf.AsSpan(pos));
        pos += QuicVarInt.Encode(value1, buf.AsSpan(pos));
        pos += QuicVarInt.Encode(identifier, buf.AsSpan(pos));
        pos += QuicVarInt.Encode(value2, buf.AsSpan(pos));
        return buf[..pos];
    }
}
