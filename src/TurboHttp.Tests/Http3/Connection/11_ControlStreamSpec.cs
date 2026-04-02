using TurboHttp.Protocol.Http3;

namespace TurboHttp.Tests.Http3.Connection;

public sealed class ControlStreamSpec
{
    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void OpenLocalStream_OpensExactlyOne()
    {
        var cs = new Http3ControlStream();
        Assert.Equal(ControlStreamState.NotOpened, cs.LocalState);

        var bytes = cs.OpenLocalStream();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
        Assert.Equal(ControlStreamState.Active, cs.LocalState);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void OpenLocalStream_Twice_ThrowsStreamCreationError()
    {
        var cs = new Http3ControlStream();
        cs.OpenLocalStream();

        var ex = Assert.Throws<Http3Exception>(() => cs.OpenLocalStream());
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void OnRemoteControlStreamOpened_TransitionsToAwaitingSettings()
    {
        var cs = new Http3ControlStream();
        Assert.Equal(ControlStreamState.NotOpened, cs.RemoteState);

        cs.OnRemoteControlStreamOpened();

        Assert.Equal(ControlStreamState.AwaitingSettings, cs.RemoteState);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void OnRemoteControlStreamOpened_Twice_ThrowsStreamCreationError()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteControlStreamOpened());
        Assert.Equal(Http3ErrorCode.StreamCreationError, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void OnRemoteFrame_FirstFrameNotSettings_ThrowsMissingSettings()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var goaway = new Http3GoAwayFrame(0);
        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(goaway));
        Assert.Equal(Http3ErrorCode.MissingSettings, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-7.2.4")]
    public void OnRemoteFrame_DuplicateSettings_ThrowsFrameUnexpected()
    {
        var cs = new Http3ControlStream();
        cs.OnRemoteControlStreamOpened();

        var settingsFrame = new Http3SettingsFrame(new List<(long, long)>());
        cs.OnRemoteFrame(settingsFrame);

        var ex = Assert.Throws<Http3Exception>(() => cs.OnRemoteFrame(settingsFrame));
        Assert.Equal(Http3ErrorCode.FrameUnexpected, ex.ErrorCode);
    }

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-6.2.1")]
    public void OnLocalControlStreamClosed_ThrowsClosedCriticalStream()
    {
        var cs = new Http3ControlStream();
        cs.OpenLocalStream();

        var ex = Assert.Throws<Http3Exception>(() => cs.OnLocalControlStreamClosed());
        Assert.Equal(Http3ErrorCode.ClosedCriticalStream, ex.ErrorCode);
        Assert.Equal(ControlStreamState.Closed, cs.LocalState);
    }

    [Fact]
    [Trait("RFC", "RFC9114-7.2.1")]
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

    [Fact]
    [Trait("RFC", "RFC9114-7.2.2")]
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

    [Fact]
    [Trait("RFC", "RFC9114-7.2.6")]
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
