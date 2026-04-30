using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Quic;

namespace Servus.Akka.Tests.Transport.Quic;

public sealed class QuicConnectionStageSpec : TestKit
{
    private readonly IMaterializer _materializer;

    public QuicConnectionStageSpec()
    {
        _materializer = Sys.Materializer();
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_materialize_without_error()
    {
        var stage = new QuicConnectionStage(TestActor);
        var flow = Flow.FromGraph(stage);

        var (sourceQueue, sinkQueue) = Source
            .Queue<ITransportOutbound>(1, OverflowStrategy.Fail)
            .ViaMaterialized(flow, Keep.Left)
            .ToMaterialized(Sink.Queue<ITransportInbound>(), Keep.Both)
            .Run(_materializer);

        Assert.NotNull(sourceQueue);
        Assert.NotNull(sinkQueue);
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_have_correct_shape()
    {
        var stage = new QuicConnectionStage(TestActor);

        Assert.NotNull(stage.Shape);
        Assert.Equal("QuicConnection.In", stage.Shape.Inlet.Name);
        Assert.Equal("QuicConnection.Out", stage.Shape.Outlet.Name);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_pass_ConnectTransport_to_state_machine()
    {
        var options = new QuicTransportOptions
        {
            Host = "localhost",
            Port = 443
        };

        var stage = new QuicConnectionStage(TestActor);
        var flow = Flow.FromGraph(stage);

        var (sourceQueue, sinkQueue) = Source
            .Queue<ITransportOutbound>(1, OverflowStrategy.Fail)
            .ViaMaterialized(flow, Keep.Left)
            .ToMaterialized(Sink.Queue<ITransportInbound>(), Keep.Both)
            .Run(_materializer);

        // Push ConnectTransport
        await sourceQueue.OfferAsync(new ConnectTransport(options));

        // Expect Acquire message on TestActor from state machine
        var msg = ExpectMsg<QuicConnectionManagerActor.Acquire>(TimeSpan.FromSeconds(2),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(msg);
        Assert.Equal("localhost", msg.Options.Host);
        Assert.Equal(443, msg.Options.Port);
    }

    [Fact(Timeout = 5000)]
    public void Stage_shape_inlet_outlet_are_correctly_named()
    {
        var stage = new QuicConnectionStage(TestActor);

        Assert.NotNull(stage.Shape.Inlet);
        Assert.NotNull(stage.Shape.Outlet);
        Assert.Equal("QuicConnection.In", stage.Shape.Inlet.Name);
        Assert.Equal("QuicConnection.Out", stage.Shape.Outlet.Name);
    }
}