using Akka.Actor;
using Akka.Configuration;
using Akka.Streams;
using TurboHttp.Internal;
using TurboHttp.Transport.Connection;

namespace TurboHttp.StreamTests.Dispatchers;

/// <summary>
/// Verifies that TurboHttp's custom Akka.NET dispatchers are correctly configured,
/// resolvable from ActorSystem, and applied to the right actors.
/// </summary>
public sealed class DispatcherConfigurationSpec : IAsyncDisposable
{
    private readonly ActorSystem _system;

    public DispatcherConfigurationSpec()
    {
        var config = TurboHttpDispatchers.CreateConfig(TurboClientOptions.DefaultMaxEndpointSubstreams);
        _system = ActorSystem.Create("dispatcher-spec-" + Guid.NewGuid(), config);
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(Timeout = 5000)]
    public void CreateConfig_should_produce_ForkJoinDispatcher_for_io_dispatcher()
    {
        var config = TurboHttpDispatchers.CreateConfig(256);
        var type = config.GetString("akka.actor.turbohttp-io-dispatcher.type");

        Assert.Equal("ForkJoinDispatcher", type);
    }

    [Fact(Timeout = 5000)]
    public void CreateConfig_should_produce_ForkJoinDispatcher_for_stream_dispatcher()
    {
        var config = TurboHttpDispatchers.CreateConfig(256);
        var type = config.GetString("akka.actor.turbohttp-stream-dispatcher.type");

        Assert.Equal("ForkJoinDispatcher", type);
    }

    [Theory(Timeout = 5000)]
    [InlineData(32u)]
    [InlineData(64u)]
    [InlineData(128u)]
    [InlineData(256u)]
    [InlineData(512u)]
    [InlineData(1024u)]
    public void CreateConfig_should_clamp_stream_thread_count(uint maxEndpointSubstreams)
    {
        var config = TurboHttpDispatchers.CreateConfig(maxEndpointSubstreams);
        var threadCount = config.GetInt(
            "akka.actor.turbohttp-stream-dispatcher.dedicated-thread-pool.thread-count");

        Assert.InRange(threadCount, Environment.ProcessorCount, 64);
    }

    [Theory(Timeout = 5000)]
    [InlineData(32u)]
    [InlineData(256u)]
    [InlineData(1024u)]
    public void CreateConfig_should_clamp_io_thread_count(uint maxEndpointSubstreams)
    {
        var config = TurboHttpDispatchers.CreateConfig(maxEndpointSubstreams);
        var threadCount = config.GetInt(
            "akka.actor.turbohttp-io-dispatcher.dedicated-thread-pool.thread-count");

        Assert.InRange(threadCount, 4, 16);
    }

    [Fact(Timeout = 5000)]
    public void IoDispatcher_should_be_resolvable_from_ActorSystem()
    {
        Assert.True(
            _system.Dispatchers.HasDispatcher(TurboHttpDispatchers.IoDispatcher));
    }

    [Fact(Timeout = 5000)]
    public void StreamDispatcher_should_be_resolvable_from_ActorSystem()
    {
        Assert.True(
            _system.Dispatchers.HasDispatcher(TurboHttpDispatchers.StreamDispatcher));
    }

    [Fact(Timeout = 5000)]
    public void WithIoDispatcher_should_apply_when_available()
    {
        var props = Props.Create(() => new ConnectionManagerActor(TimeSpan.FromMinutes(5)));

        Assert.Equal(TurboHttpDispatchers.IoDispatcher, props.WithIoDispatcher(_system).Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void WithIoDispatcher_should_fall_back_to_default_when_missing()
    {
        using var bareSystem = ActorSystem.Create("bare-" + Guid.NewGuid());
        var props = Props.Create(() => new ConnectionManagerActor(TimeSpan.FromMinutes(5)));

        // Should NOT have the custom dispatcher — falls back to default
        Assert.NotEqual(TurboHttpDispatchers.IoDispatcher, props.WithIoDispatcher(bareSystem).Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void WithStreamDispatcher_should_apply_when_available()
    {
        var props = Props.Create(() => new ConnectionManagerActor(TimeSpan.FromMinutes(5)));

        Assert.Equal(TurboHttpDispatchers.StreamDispatcher, props.WithStreamDispatcher(_system).Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void WithStreamDispatcher_should_fall_back_to_default_when_missing()
    {
        using var bareSystem = ActorSystem.Create("bare-" + Guid.NewGuid());
        var settings = ActorMaterializerSettings.Create(bareSystem);

        // Should NOT have the custom dispatcher — falls back to default
        Assert.NotEqual(TurboHttpDispatchers.StreamDispatcher, settings.WithStreamDispatcher(bareSystem).Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public void CreateConfig_should_use_background_threads()
    {
        var config = TurboHttpDispatchers.CreateConfig(256);

        Assert.Equal("background",
            config.GetString("akka.actor.turbohttp-io-dispatcher.dedicated-thread-pool.threadtype"));
        Assert.Equal("background",
            config.GetString("akka.actor.turbohttp-stream-dispatcher.dedicated-thread-pool.threadtype"));
    }

    [Fact(Timeout = 5000)]
    public void CreateConfig_should_set_expected_throughput_values()
    {
        var config = TurboHttpDispatchers.CreateConfig(256);

        Assert.Equal(32, config.GetInt("akka.actor.turbohttp-io-dispatcher.throughput"));
        Assert.Equal(64, config.GetInt("akka.actor.turbohttp-stream-dispatcher.throughput"));
    }

    [Fact(Timeout = 5000)]
    public void CreateConfig_should_scale_stream_threads_with_substreams()
    {
        // With high MaxEndpointSubstreams, thread count should be higher than with low
        var configLow = TurboHttpDispatchers.CreateConfig(32);
        var configHigh = TurboHttpDispatchers.CreateConfig(512);

        var threadsLow = configLow.GetInt(
            "akka.actor.turbohttp-stream-dispatcher.dedicated-thread-pool.thread-count");
        var threadsHigh = configHigh.GetInt(
            "akka.actor.turbohttp-stream-dispatcher.dedicated-thread-pool.thread-count");

        Assert.True(threadsHigh >= threadsLow,
            $"Expected high substreams ({threadsHigh}) >= low substreams ({threadsLow})");
    }
}
