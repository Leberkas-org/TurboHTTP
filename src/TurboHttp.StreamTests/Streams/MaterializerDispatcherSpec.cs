using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using TurboHttp.Internal;

namespace TurboHttp.StreamTests.Streams;

/// <summary>
/// Verifies that the Akka.Streams materializer uses the dedicated
/// <see cref="TurboHttpDispatchers.StreamDispatcher"/> and can execute pipelines on it.
/// </summary>
public sealed class MaterializerDispatcherSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public MaterializerDispatcherSpec()
        : base(ActorSystem.Create(
            "mat-dispatcher-spec-" + Guid.NewGuid(),
            TurboHttpDispatchers.CreateConfig(TurboClientOptions.DefaultMaxEndpointSubstreams)))
    {
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithInputBuffer(initialSize: 16, maxSize: 64)
            .WithDispatcher(TurboHttpDispatchers.StreamDispatcher);

        _materializer = Sys.Materializer(settings: settings);
    }

    [Fact(Timeout = 5000)]
    public void Materializer_settings_should_use_stream_dispatcher()
    {
        var settings = ActorMaterializerSettings.Create(Sys)
            .WithDispatcher(TurboHttpDispatchers.StreamDispatcher);

        Assert.Equal(TurboHttpDispatchers.StreamDispatcher, settings.Dispatcher);
    }

    [Fact(Timeout = 5000)]
    public async Task Pipeline_should_materialize_with_stream_dispatcher()
    {
        var result = await Source.From([1, 2, 3])
            .RunWith(Sink.Seq<int>(), _materializer);

        Assert.Equal([1, 2, 3], result);
    }

    [Fact(Timeout = 5000)]
    public async Task Pipeline_should_handle_concurrent_streams_on_dedicated_dispatcher()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(i => Source.Single(i)
                .RunWith(Sink.First<int>(), _materializer))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(50, results.Length);
        Assert.Equal(Enumerable.Range(0, 50), results.Order());
    }
}
