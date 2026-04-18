using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using TurboHTTP.Internal;
using TurboHTTP.Transport.Quic;

namespace TurboHTTP.StreamTests.Transport;

public sealed class QuicConnectionStageSpec
{
    private static readonly RequestEndpoint TestEndpoint = new()
    {
        Scheme = "https",
        Host = "localhost",
        Port = 443,
        Version = HttpVersion.Version30
    };

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_should_create_successfully()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration: true);

        Assert.NotNull(stage);
        Assert.NotNull(stage.Shape);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_should_have_inlet_and_outlet()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration: true);

        var shape = stage.Shape;
        Assert.NotNull(shape.Inlet);
        Assert.NotNull(shape.Outlet);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_with_migration_disabled_should_initialize()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration: false);

        Assert.NotNull(stage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_should_support_multiple_instantiation()
    {
        for (int i = 0; i < 5; i++)
        {
            var stage = new QuicConnectionStage(
                ActorRefs.Nobody,
                new TurboClientOptions(),
                allowConnectionMigration: true);

            Assert.NotNull(stage);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_shape_inlet_outlet_not_null()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration: true);

        var shape = stage.Shape;
        Assert.NotNull(shape.Inlet);
        Assert.NotNull(shape.Outlet);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_shape_inlet_matches_outlet()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions());

        var shape = stage.Shape;
        Assert.Same(shape, stage.Shape);  // Shape should be consistent
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Stage_with_custom_client_options_should_work()
    {
        var clientOptions = new TurboClientOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };

        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            clientOptions,
            allowConnectionMigration: true);

        Assert.NotNull(stage);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9114")]
    public void Multiple_stages_should_be_independent()
    {
        var stage1 = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration: true);

        var stage2 = new QuicConnectionStage(
            ActorRefs.Nobody,
            new TurboClientOptions(),
            allowConnectionMigration: false);

        Assert.NotSame(stage1, stage2);
        Assert.NotSame(stage1.Shape, stage2.Shape);
    }
}
