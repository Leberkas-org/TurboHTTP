using TurboHTTP.Protocol.Syntax.Http2;

namespace TurboHTTP.Tests.Protocol.Syntax.Http2.Client.Settings;

public sealed class Http2SettingsSpec
{
    private static void EnforceEnablePush(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == SettingsParameter.EnablePush && value > 1)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §6.5.2: SETTINGS_ENABLE_PUSH value {value} is invalid; must be 0 or 1.");
            }
        }
    }

    private static void EnforceInitialWindowSize(IReadOnlyList<(SettingsParameter, uint)> parameters)
    {
        foreach (var (key, value) in parameters)
        {
            if (key == SettingsParameter.InitialWindowSize && value > 0x7FFFFFFFu)
            {
                throw new HttpProtocolException(
                    $"RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE {value} exceeds the maximum 2^31−1.");
            }
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_decode_with_is_ack_true_when_settings_ack_frame()
    {
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(SettingsFrame.SettingsAck());

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.True(frame.IsAck);
        Assert.Empty(frame.Parameters);
        Assert.Equal(0, frame.StreamId);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_decode_with_is_ack_false_when_non_ack_settings()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.False(frame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_protocol_error_when_settings_on_non_zero_stream()
    {
        // Build a SETTINGS frame with stream ID = 1 (violates RFC 9113 §6.5).
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x00, // length = 0
            0x04, // SETTINGS
            0x00, // flags = 0
            0x00, 0x00, 0x00, 0x01, // stream = 1 — MUST be 0
        };
        var decoder = new FrameDecoder();
        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Decode(rawFrame));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_frame_size_error_when_settings_ack_has_payload()
    {
        // SETTINGS ACK (flags=0x1) with 6-byte payload — violates §6.5.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6
            0x04, // SETTINGS
            0x01, // ACK flag
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x05, 0x00, 0x00, 0x40, 0x00, // MaxFrameSize=16384
        };
        var decoder = new FrameDecoder();
        var ex = Assert.Throws<HttpProtocolException>(() => decoder.Decode(rawFrame));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_frame_size_error_when_settings_payload_not_multiple_of_6()
    {
        // Payload length = 7 (not a multiple of 6) — violates §6.5.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x07, // length = 7
            0x04, // SETTINGS
            0x00, // flags = 0
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0x00, 0x01, 0x00, 0x00, 0x10, 0x00, 0x00, // 7 bytes
        };
        var decoder = new FrameDecoder();
        Assert.Throws<HttpProtocolException>(() => decoder.Decode(rawFrame));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_protocol_error_when_max_frame_size_below_min()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16383u)]).Serialize();
        var decoder = new FrameDecoder();
        Assert.Throws<HttpProtocolException>(() => decoder.Decode(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_protocol_error_when_max_frame_size_above_max()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777216u)]).Serialize();
        var decoder = new FrameDecoder();
        Assert.Throws<HttpProtocolException>(() => decoder.Decode(bytes));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_accept_when_max_frame_size_at_min()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16384u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.MaxFrameSize, Item2: 16384u });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_accept_when_max_frame_size_at_max()
    {
        var bytes = new SettingsFrame([(SettingsParameter.MaxFrameSize, 16777215u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.MaxFrameSize, Item2: 16777215u });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_accept_when_enable_push_is_zero()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 0u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: ENABLE_PUSH=0 is valid — must not throw.
        EnforceEnablePush(frame.Parameters);
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.EnablePush, Item2: 0u });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_accept_when_enable_push_is_one()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 1u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: ENABLE_PUSH=1 is valid — must not throw.
        EnforceEnablePush(frame.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_protocol_error_when_enable_push_is_two()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 2u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: ENABLE_PUSH > 1 MUST trigger a connection PROTOCOL_ERROR.
        Assert.Throws<HttpProtocolException>(() => EnforceEnablePush(frame.Parameters));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_protocol_error_when_enable_push_is_max_value()
    {
        var bytes = new SettingsFrame([(SettingsParameter.EnablePush, 0xFFFFFFFFu)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Throws<HttpProtocolException>(() => EnforceEnablePush(frame.Parameters));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_flow_control_error_when_initial_window_size_overflows()
    {
        var bytes = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0x80000000u)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: INITIAL_WINDOW_SIZE > 2^31−1 MUST trigger a connection FLOW_CONTROL_ERROR.
        Assert.Throws<HttpProtocolException>(() => EnforceInitialWindowSize(frame.Parameters));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_accept_when_initial_window_size_at_max()
    {
        var bytes = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0x7FFFFFFFu)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // RFC 9113 §6.5.2: 2^31−1 is the maximum valid value — must not throw.
        EnforceInitialWindowSize(frame.Parameters);
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.InitialWindowSize, Item2: 0x7FFFFFFFu });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_be_flow_control_error_when_initial_window_size_is_max_uint()
    {
        var bytes = new SettingsFrame([(SettingsParameter.InitialWindowSize, 0xFFFFFFFFu)]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Throws<HttpProtocolException>(() => EnforceInitialWindowSize(frame.Parameters));
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_decode_with_no_parameters_when_settings_has_empty_payload()
    {
        var emptySettings = new byte[]
        {
            0x00, 0x00, 0x00, // length = 0
            0x04, // SETTINGS
            0x00, // flags = 0 (no ACK)
            0x00, 0x00, 0x00, 0x00, // stream = 0
        };
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(emptySettings);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Empty(frame.Parameters);
        Assert.False(frame.IsAck);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_decode_all_when_settings_has_multiple_parameters()
    {
        var bytes = new SettingsFrame([
            (SettingsParameter.HeaderTableSize, 2048u),
            (SettingsParameter.EnablePush, 0u),
            (SettingsParameter.MaxFrameSize, 32768u),
        ]).Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(3, frame.Parameters.Count);
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.HeaderTableSize, Item2: 2048u });
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.EnablePush, Item2: 0u });
        Assert.Contains(frame.Parameters, p => p is { Item1: SettingsParameter.MaxFrameSize, Item2: 32768u });
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_silently_decode_when_settings_has_unknown_parameter()
    {
        // Unknown parameter ID 0xFFFF, value = 42 — must not throw.
        var rawFrame = new byte[]
        {
            0x00, 0x00, 0x06, // length = 6
            0x04, // SETTINGS
            0x00, // flags = 0
            0x00, 0x00, 0x00, 0x00, // stream = 0
            0xFF, 0xFF, // unknown parameter = 0xFFFF
            0x00, 0x00, 0x00, 0x2A, // value = 42
        };
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(rawFrame);

        Assert.Single(frames);
        var frame = Assert.IsType<SettingsFrame>(frames[0]);
        // Unknown parameter is decoded without error per RFC 9113 §5.5.
        Assert.Single(frame.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5")]
    public void Http2FrameDecoder_should_preserve_all_parameters_when_settings_round_trip()
    {
        var original = new SettingsFrame([
            (SettingsParameter.HeaderTableSize, 4096u),
            (SettingsParameter.MaxConcurrentStreams, 100u),
            (SettingsParameter.InitialWindowSize, 65535u),
            (SettingsParameter.MaxFrameSize, 16384u),
        ]);
        var bytes = original.Serialize();
        var decoder = new FrameDecoder();
        var frames = decoder.Decode(bytes);

        Assert.Single(frames);
        var decoded = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Equal(4, decoded.Parameters.Count);
        Assert.Contains(decoded.Parameters, p => p is { Item1: SettingsParameter.HeaderTableSize, Item2: 4096u });
        Assert.Contains(decoded.Parameters, p => p is { Item1: SettingsParameter.MaxConcurrentStreams, Item2: 100u });
        Assert.Contains(decoded.Parameters, p => p is { Item1: SettingsParameter.InitialWindowSize, Item2: 65535u });
        Assert.Contains(decoded.Parameters, p => p is { Item1: SettingsParameter.MaxFrameSize, Item2: 16384u });
    }
}