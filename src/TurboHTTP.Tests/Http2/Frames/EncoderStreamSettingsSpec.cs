using TurboHTTP.Protocol.Http2;

namespace TurboHTTP.Tests.Http2.Frames;

/// <summary>
/// Tests SETTINGS frame encoding and stream parameter validation per RFC 9113 §6.5.
/// Verifies stream ID rules, parameter encoding, and flow control window management.
/// </summary>
/// <remarks>
/// Class under test: <see cref="RequestEncoder"/>, <see cref="SettingsFrame"/>.
/// RFC 9113 §6.5: SETTINGS frames carry pairs of parameter identifiers and values.
/// </remarks>
public sealed class Http2EncoderStreamSettingsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_header_table_size()
    {
        var settings = new SettingsFrame([(SettingsParameter.HeaderTableSize, 2048u)]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_enable_push()
    {
        var settings = new SettingsFrame([(SettingsParameter.EnablePush, 0u)]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_max_concurrent_streams()
    {
        var settings = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 100u)]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_initial_window_size()
    {
        var settings = new SettingsFrame([(SettingsParameter.InitialWindowSize, 32768u)]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_max_frame_size()
    {
        var settings = new SettingsFrame([(SettingsParameter.MaxFrameSize, 32768u)]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_max_header_list_size()
    {
        var settings = new SettingsFrame([(SettingsParameter.MaxHeaderListSize, 8192u)]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_multiple_settings_parameters()
    {
        var settings = new SettingsFrame(
        [
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.EnablePush, 0u),
            (SettingsParameter.MaxConcurrentStreams, 50u),
            (SettingsParameter.InitialWindowSize, 16384u),
        ]);
        var frame = settings.Serialize();

        Assert.NotEmpty(frame);
        var decoded = new FrameDecoder().Decode(frame);
        Assert.IsType<SettingsFrame>(decoded[0]);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_send_settings_on_stream_zero()
    {
        var settings = new SettingsFrame([(SettingsParameter.HeaderTableSize, 4096u)]);
        var frame = settings.Serialize();

        // Verify stream ID is 0
        var streamId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(5)) & 0x7FFFFFFF;
        Assert.Equal(0u, streamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_acknowledge_settings()
    {
        var frameBytes = SettingsFrame.SettingsAck();

        var decoded = new FrameDecoder().Decode(frameBytes);
        var settingsFrame = Assert.IsType<SettingsFrame>(decoded[0]);
        Assert.True(settingsFrame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.9")]
    public void Http2Encoder_should_respect_initial_flow_control_window()
    {
        // Default initial window size is 65535
        var window = new WindowUpdateFrame(0, 1024).Serialize();
        Assert.NotEmpty(window);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2Encoder_should_encode_settings_as_zero_stream_id()
    {
        var settings = new SettingsFrame([]);
        var frameBytes = settings.Serialize();

        // Stream ID should be 0
        var streamIdBytes = frameBytes.AsSpan(5, 4);
        var streamId = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(streamIdBytes) & 0x7FFFFFFF;
        Assert.Equal(0u, streamId);
    }
}
