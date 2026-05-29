using Akka.Streams;
using Akka.Streams.Dsl;
using Microsoft.AspNetCore.Http.Features;
using TurboHTTP.Server.Context.Features;
using TurboHTTP.Streams.Stages.Server;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.Tests.Streams.Stages.Server;

public sealed class ResponseDispatcherHubSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    public async Task ResponseDispatcherHub_should_dispatch_to_correct_connection()
    {
        var hub = new ResponseDispatcherHub();

        var fc1 = new FeatureCollection();
        fc1.Set(new ConnectionRoutingFeature { ConnectionId = 1 });

        var fc2 = new FeatureCollection();
        fc2.Set(new ConnectionRoutingFeature { ConnectionId = 2 });

        // Create a delayed source to give subscribers time to register
        // Cast to IFeatureCollection to avoid type inference issues
        var items = new IFeatureCollection[] { fc1, fc2 };
        var dispatcher = Source.From(items)
            .Delay(TimeSpan.FromMilliseconds(100), DelayOverflowStrategy.Backpressure)
            .ToMaterialized((IGraph<SinkShape<IFeatureCollection>, IResponseDispatcher<IFeatureCollection>>)hub, Keep.Right)
            .Run(Materializer);

        // Subscribe to connections
        var source1 = dispatcher.Subscribe(1);
        var source2 = dispatcher.Subscribe(2);

        // Now collect results
        var collectTask1 = source1.RunWith(Sink.Seq<IFeatureCollection>(), Materializer);
        var collectTask2 = source2.RunWith(Sink.Seq<IFeatureCollection>(), Materializer);

        var results1 = await collectTask1;
        var results2 = await collectTask2;

        Assert.Single(results1);
        Assert.Equal(1, results1[0].Get<ConnectionRoutingFeature>()?.ConnectionId);

        Assert.Single(results2);
        Assert.Equal(2, results2[0].Get<ConnectionRoutingFeature>()?.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseDispatcherHub_should_drop_unroutable_responses()
    {
        var hub = new ResponseDispatcherHub();

        var fcUnroutable = new FeatureCollection();
        fcUnroutable.Set(new ConnectionRoutingFeature { ConnectionId = 999 });

        var fcRoutable = new FeatureCollection();
        fcRoutable.Set(new ConnectionRoutingFeature { ConnectionId = 1 });

        var items = new IFeatureCollection[] { fcUnroutable, fcRoutable };
        var dispatcher = Source.From(items)
            .Delay(TimeSpan.FromMilliseconds(100), DelayOverflowStrategy.Backpressure)
            .ToMaterialized((IGraph<SinkShape<IFeatureCollection>, IResponseDispatcher<IFeatureCollection>>)hub, Keep.Right)
            .Run(Materializer);

        var source1 = dispatcher.Subscribe(1);
        var collectTask = source1.RunWith(Sink.Seq<IFeatureCollection>(), Materializer);

        var results = await collectTask;

        Assert.Single(results);
        Assert.Equal(1, results[0].Get<ConnectionRoutingFeature>()?.ConnectionId);
    }

    [Fact(Timeout = 5000)]
    public async Task ResponseDispatcherHub_should_complete_sources_on_upstream_finish()
    {
        var hub = new ResponseDispatcherHub();

        // Emit an unroutable item (won't be delivered to subscriber)
        // This gives time for registration while still testing completion
        var noMatch = new FeatureCollection();
        noMatch.Set(new ConnectionRoutingFeature { ConnectionId = 999 });
        var items = new IFeatureCollection[] { noMatch };

        var dispatcher = Source.From(items)
            .Delay(TimeSpan.FromMilliseconds(100), DelayOverflowStrategy.Backpressure)
            .ToMaterialized((IGraph<SinkShape<IFeatureCollection>, IResponseDispatcher<IFeatureCollection>>)hub, Keep.Right)
            .Run(Materializer);

        var source1 = dispatcher.Subscribe(1);
        var collectTask = source1.RunWith(Sink.Seq<IFeatureCollection>(), Materializer);

        var results = await collectTask;

        Assert.Empty(results);
    }
}
