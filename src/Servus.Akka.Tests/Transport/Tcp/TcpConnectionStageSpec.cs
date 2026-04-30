using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Servus.Akka.Transport;
using Servus.Akka.Transport.Tcp;

namespace Servus.Akka.Tests.Transport.Tcp;

public sealed class TcpConnectionStageSpec : TestKit
{
    private readonly IMaterializer _materializer;
    private readonly IPoolingStrategy _poolingStrategy;

    public TcpConnectionStageSpec()
    {
        _materializer = Sys.Materializer();
        _poolingStrategy = new TestPoolingStrategy();
    }

    private sealed class TestPoolingStrategy : IPoolingStrategy
    {
        public PoolAction OnDisconnect(object lease, DisconnectReason reason) => PoolAction.Dispose;
        public PoolAction OnUpstreamFinish(object lease) => PoolAction.Reuse;
    }

    [Fact(Timeout = 5000)]
    public void Stage_should_materialize_without_error()
    {
        var stage = new TcpConnectionStage(TestActor, _poolingStrategy);
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
        var stage = new TcpConnectionStage(TestActor, _poolingStrategy);

        Assert.NotNull(stage.Shape);
        Assert.Equal("TcpConnection.In", stage.Shape.Inlet.Name);
        Assert.Equal("TcpConnection.Out", stage.Shape.Outlet.Name);
    }

    [Fact(Timeout = 5000)]
    public void Stage_shape_inlet_should_accept_ITransportOutbound()
    {
        var stage = new TcpConnectionStage(TestActor, _poolingStrategy);

        Assert.NotNull(stage.Shape.Inlet);
        // Inlet is typed to ITransportOutbound via FlowShape<ITransportOutbound, ITransportInbound>
        Assert.IsAssignableFrom<Inlet<ITransportOutbound>>(stage.Shape.Inlet);
    }

    [Fact(Timeout = 5000)]
    public void Stage_shape_outlet_should_emit_ITransportInbound()
    {
        var stage = new TcpConnectionStage(TestActor, _poolingStrategy);

        Assert.NotNull(stage.Shape.Outlet);
        // Outlet is typed to ITransportInbound via FlowShape<ITransportOutbound, ITransportInbound>
        Assert.IsAssignableFrom<Outlet<ITransportInbound>>(stage.Shape.Outlet);
    }

    [Fact(Timeout = 5000)]
    public async Task Stage_should_accept_ConnectTransport()
    {
        var options = new TcpTransportOptions
        {
            Host = "127.0.0.1",
            Port = 8080
        };

        var stage = new TcpConnectionStage(TestActor, _poolingStrategy);
        var flow = Flow.FromGraph(stage);

        var (sourceQueue, sinkQueue) = Source
            .Queue<ITransportOutbound>(1, OverflowStrategy.Fail)
            .ViaMaterialized(flow, Keep.Left)
            .ToMaterialized(Sink.Queue<ITransportInbound>(), Keep.Both)
            .Run(_materializer);

        // Push ConnectTransport onto the stage inlet
        await sourceQueue.OfferAsync(new ConnectTransport(options));

        // Expect Acquire message on TestActor from state machine
        var msg = ExpectMsg<TcpConnectionManagerActor.Acquire>(TimeSpan.FromSeconds(2),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(msg);
        Assert.Equal("127.0.0.1", msg.Options.Host);
        Assert.Equal(8080, msg.Options.Port);
    }
}