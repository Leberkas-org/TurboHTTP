using Akka.Actor;
using Akka.Streams;
using Servus.Akka.IO;
using Servus.Akka.IO.Tcp;

namespace Servus.Akka.Tests.IO.Tcp;

public sealed class TcpConnectionStageSpec
{
    [Fact(Timeout = 5000)]
    public void TcpConnectionStage_should_have_correct_shape()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody);

        Assert.NotNull(stage.Shape);
        Assert.Single(stage.Shape.Inlets);
        Assert.Single(stage.Shape.Outlets);
    }

    [Fact(Timeout = 5000)]
    public void TcpConnectionStage_inlet_should_be_named_correctly()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody);

        Assert.Equal("TcpConnection.In", stage.Shape.Inlet.Name);
    }

    [Fact(Timeout = 5000)]
    public void TcpConnectionStage_outlet_should_be_named_correctly()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody);

        Assert.Equal("TcpConnection.Out", stage.Shape.Outlet.Name);
    }

    [Fact(Timeout = 5000)]
    public void TcpConnectionStage_inlet_should_accept_output_items()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody);

        Assert.IsType<Inlet<IOutputItem>>(stage.Shape.Inlet);
    }

    [Fact(Timeout = 5000)]
    public void TcpConnectionStage_outlet_should_produce_input_items()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody);

        Assert.IsType<Outlet<IInputItem>>(stage.Shape.Outlet);
    }

    [Fact(Timeout = 5000)]
    public void TcpConnectionStage_shape_should_have_flow_shape()
    {
        var stage = new TcpConnectionStage(ActorRefs.Nobody);

        Assert.NotNull(stage.Shape);
        Assert.Equal(stage.Shape.Inlet, stage.Shape.Inlets[0]);
        Assert.Equal(stage.Shape.Outlet, stage.Shape.Outlets[0]);
    }
}
