using Akka.Actor;
using Servus.Akka.IO.Quic;

namespace Servus.Akka.Tests.IO.Quic;

public sealed class QuicConnectionStageSpec
{
    [Fact(Timeout = 5000)]
    public void Stage_should_create_successfully()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            allowConnectionMigration: true);

        Assert.NotNull(stage);
        Assert.NotNull(stage.Shape);
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_have_inlet_and_outlet()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            allowConnectionMigration: true);

        var shape = stage.Shape;
        Assert.NotNull(shape.Inlet);
        Assert.NotNull(shape.Outlet);
    }

    [Fact(Timeout = 5000)]
    public void Stage_with_migration_disabled_should_initialize()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            allowConnectionMigration: false);

        Assert.NotNull(stage);
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_support_multiple_instantiation()
    {
        for (var i = 0; i < 5; i++)
        {
            var stage = new QuicConnectionStage(
                ActorRefs.Nobody,
                allowConnectionMigration: true);

            Assert.NotNull(stage);
        }
    }

    [Fact(Timeout = 5000)]
    public void Stage_shape_inlet_outlet_not_null()
    {
        var stage = new QuicConnectionStage(
            ActorRefs.Nobody,
            allowConnectionMigration: true);

        var shape = stage.Shape;
        Assert.NotNull(shape.Inlet);
        Assert.NotNull(shape.Outlet);
    }

    [Fact(Timeout = 5000)]
    public void Stage_shape_inlet_matches_outlet()
    {
        var stage = new QuicConnectionStage(ActorRefs.Nobody);

        var shape = stage.Shape;
        Assert.Same(shape, stage.Shape);
    }

    [Fact(Timeout = 5000)]
    public void Stage_with_connection_migration_default_should_work()
    {
        var stage = new QuicConnectionStage(ActorRefs.Nobody);

        Assert.NotNull(stage);
    }

    [Fact(Timeout = 5000)]
    public void Multiple_stages_should_be_independent()
    {
        var stage1 = new QuicConnectionStage(
            ActorRefs.Nobody,
            allowConnectionMigration: true);

        var stage2 = new QuicConnectionStage(
            ActorRefs.Nobody,
            allowConnectionMigration: false);

        Assert.NotSame(stage1, stage2);
        Assert.NotSame(stage1.Shape, stage2.Shape);
    }

    [Fact(Timeout = 5000)]
    public void Stage_inlet_should_be_named_correctly()
    {
        var stage = new QuicConnectionStage(ActorRefs.Nobody);

        Assert.Equal("QuicConnection.In", stage.Shape.Inlet.Name);
    }

    [Fact(Timeout = 5000)]
    public void Stage_outlet_should_be_named_correctly()
    {
        var stage = new QuicConnectionStage(ActorRefs.Nobody);

        Assert.Equal("QuicConnection.Out", stage.Shape.Outlet.Name);
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_have_single_inlet_and_outlet()
    {
        var stage = new QuicConnectionStage(ActorRefs.Nobody);

        Assert.Single(stage.Shape.Inlets);
        Assert.Single(stage.Shape.Outlets);
    }
}