using TurboHTTP.Protocol.Http2;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Http2.StreamState;

/// <summary>
/// Tests MAX_CONCURRENT_STREAMS tracking in Http20Engine per RFC 9113 §6.5.2.
/// Verifies the engine exposes the configured limit and passes it to the underlying connection stage.
/// </summary>
/// <remarks>
/// Class under test: <see cref="Http20Engine"/>.
/// RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS indicates the maximum number of concurrent
/// streams that the sender will allow. Initially, there is no limit to this value.
/// </remarks>
public sealed class Http20EngineMaxConcurrentStreamsSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_default_to_int_max_value_when_no_max_concurrent_streams_specified()
    {
        var engine = new Http20Engine();
        Assert.Equal(int.MaxValue, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_use_unlimited_streams_when_parameterless_constructor_used()
    {
        var engine = new Http20Engine();
        Assert.Equal(Http20Engine.DefaultMaxConcurrentStreams, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_store_custom_value_when_max_concurrent_streams_provided_in_constructor()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 50);
        Assert.Equal(50, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_store_single_stream_limit_when_max_concurrent_streams_is_1()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 1);
        Assert.Equal(1, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_store_zero_value_when_max_concurrent_streams_is_0()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 0);
        Assert.Equal(0, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_have_default_constant_equal_to_int_max_value()
    {
        Assert.Equal(int.MaxValue, Http20Engine.DefaultMaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_use_default_max_concurrent_streams_when_only_window_size_specified()
    {
        var engine = new Http20Engine(initialWindowSize: 32768);
        Assert.Equal(int.MaxValue, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void Http20Engine_should_create_flow_successfully_when_custom_max_concurrent_streams_specified()
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: 10);
        var flow = engine.CreateFlow();
        Assert.NotNull(flow);
    }

    [Theory(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void Http20Engine_should_store_correct_value_when_various_max_concurrent_streams_provided(int maxStreams)
    {
        var engine = new Http20Engine(initialWindowSize: 65535, maxConcurrentStreams: maxStreams);
        Assert.Equal(maxStreams, engine.MaxConcurrentStreams);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void SettingsFrame_should_decode_max_concurrent_streams_parameter_when_settings_frame_contains_it()
    {
        var frame = new SettingsFrame([(SettingsParameter.MaxConcurrentStreams, 42u)]);
        var parameter = frame.Parameters.Single();
        Assert.Equal(SettingsParameter.MaxConcurrentStreams, parameter.Item1);
        Assert.Equal(42u, parameter.Item2);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void SettingsFrame_should_have_no_parameters_when_settings_frame_is_ack()
    {
        var frame = new SettingsFrame([], isAck: true);
        Assert.True(frame.IsAck);
        Assert.Empty(frame.Parameters);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-6.5.2")]
    public void SettingsFrame_should_include_max_concurrent_streams_when_settings_frame_has_multiple_parameters()
    {
        var frame = new SettingsFrame([
            (SettingsParameter.InitialWindowSize, 32768u),
            (SettingsParameter.MaxConcurrentStreams, 128u),
            (SettingsParameter.MaxFrameSize, 16384u),
        ]);

        var maxStreams = frame.Parameters
            .Where(p => p.Item1 == SettingsParameter.MaxConcurrentStreams)
            .Select(p => (int)p.Item2)
            .SingleOrDefault();

        Assert.Equal(128, maxStreams);
    }
}
