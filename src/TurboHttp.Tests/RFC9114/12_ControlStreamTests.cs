using TurboHttp.Protocol.RFC9000;
using TurboHttp.Protocol.RFC9114;

namespace TurboHttp.Tests.RFC9114;

public sealed class ControlStreamTests
{
    [Fact(DisplayName = "RFC9114-6.2.1-CS-001: Client opens exactly one control stream")]
    public void OpenLocalStream_OpensExactlyOne()
    {
        var cs = new Http3ControlStream();
        Assert.Equal(ControlStreamState.NotOpened, cs.LocalState);

        var bytes = cs.OpenLocalStream();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(ControlStreamState.Active, cs.LocalState);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-002: Opening second local control stream is connection error")]
    public void OpenLocalStream_Twice_ThrowsStreamCreationError()
    {
        var cs = new Http3ControlStream();
        cs.OpenLocalStream();

        var ex = Assert.Throws<Http3Exception>(() => cs.OpenLocalStream());
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-003: SETTINGS frame is first frame on local control stream")]
    public void OpenLocalStream_SerializesStreamTypeAndSettings()
    {
        var cs = new Http3ControlStream();
        var bytes = cs.OpenLocalStream();
        var span = bytes.AsSpan();

        // First byte(s): stream type = 0x00 (Control)
        Assert.True(QuicVarInt.TryDecode(span, out var streamType, out var consumed));
        Assert.Equal((long)Http3StreamType.Control, streamType);
        span = span[consumed..];

        // Next: SETTINGS frame (type=0x04, length, payload)
        Assert.True(QuicVarInt.TryDecode(span, out var frameType, out consumed));
        Assert.Equal((long)Http3FrameType.Settings, frameType);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-004: Client sends custom settings on control stream")]
    public void OpenLocalStream_WithSettings_IncludesParameters()
    {
        var cs = new Http3ControlStream();
        var settings = new Http3Settings();
        settings.Set(Http3SettingId.MaxFieldSectionSize, 8192);

        var bytes = cs.OpenLocalStream(settings);
        var span = bytes.AsSpan();

        // Skip stream type
        QuicVarInt.TryDecode(span, out _, out var consumed);
        span = span[consumed..];

        // Skip frame type
        QuicVarInt.TryDecode(span, out _, out consumed);
        span = span[consumed..];

        // Read frame length
        QuicVarInt.TryDecode(span, out var length, out consumed);
        span = span[consumed..];

        // Payload should contain the setting
        Assert.True(length > 0);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-005: Receiving server control stream transitions to AwaitingSettings")]
    public void OnRemoteControlStreamOpened_TransitionsToAwaitingSettings()
    {
        var cs = new Http3ControlStream();
        Assert.Equal(ControlStreamState.NotOpened, cs.RemoteState);

        cs.OnRemoteControlStreamOpened();

        Assert.Equal(ControlStreamState.AwaitingSettings, cs.RemoteState);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-006: Receiving second server control stream is connection error")]
    public void OnRemoteControlStreamOpened_Twice_ThrowsStreamCreationError()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteControlStreamOpened());
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-007: First frame on remote control stream must be SETTINGS")]
    public void OnRemoteFrame_FirstFrameNotSettings_ThrowsMissingSettings()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var goaway = new Http3GoAwayFrame(0);
        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(goaway));
        Assert.Equal(Http3ErrorCode.MissingSettings, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-008: SETTINGS as first frame activates remote control stream")]
    public void OnRemoteFrame_SettingsFirst_ActivatesStream()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>
        {
            (Http3SettingId.MaxFieldSectionSize, 4096)
        });

        cs.OnRemoteFrame(settingsFrame);

        Assert.Equal(ControlStreamState.Active, cs.RemoteState);
        Assert.NotNull(cs.RemoteSettings);
        Assert.Equal(4096, cs.RemoteSettings!.MaxFieldSectionSize);
    }

    [Fact(DisplayName = "RFC9114-7.2.4-CS-009: Second SETTINGS frame on control stream is connection error")]
    public void OnRemoteFrame_DuplicateSettings_ThrowsFrameUnexpected()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(settingsFrame));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-010: Control stream closure is connection error")]
    public void OnRemoteControlStreamClosed_ThrowsClosedCriticalStream()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteControlStreamClosed());
        Assert.Equal(Http3ErrorCode.ClosedCriticalStream, ex.ErrorCode);
        Assert.Equal(ControlStreamState.Closed, cs.RemoteState);
    }

    [Fact(DisplayName = "RFC9114-6.2.1-CS-011: Local control stream closure is connection error")]
    public void OnLocalControlStreamClosed_ThrowsClosedCriticalStream()
    {
        var cs = new Http3ControlStream();
        cs.OpenLocalStream();

        var ex = Assert.Throws<Http3Exception>(() => cs.OnLocalControlStreamClosed());
        Assert.Equal(Http3ErrorCode.ClosedCriticalStream, ex.ErrorCode);
        Assert.Equal(ControlStreamState.Closed, cs.LocalState);
    }

    [Fact(DisplayName = "RFC9114-7.2.1-CS-012: DATA frame on control stream is connection error")]
    public void OnRemoteFrame_DataOnControlStream_ThrowsFrameUnexpected()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var dataFrame = new Http3DataFrame(new byte[] { 0x01, 0x02 });
        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(dataFrame));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.2-CS-013: HEADERS frame on control stream is connection error")]
    public void OnRemoteFrame_HeadersOnControlStream_ThrowsFrameUnexpected()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var headersFrame = new Http3HeadersFrame(new byte[] { 0x01 });
        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(headersFrame));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact(DisplayName = "RFC9114-7.2.6-CS-014: GOAWAY frame is valid on active control stream")]
    public void OnRemoteFrame_GoAwayOnActiveControlStream_Succeeds()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var goaway = new Http3GoAwayFrame(0);
        cs.OnRemoteFrame(goaway); // Should not throw
    }
}
