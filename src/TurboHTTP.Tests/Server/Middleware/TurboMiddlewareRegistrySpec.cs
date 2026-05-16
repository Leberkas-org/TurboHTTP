using Akka;
using Akka.Streams.Dsl;
using TurboHTTP.Server.Middleware;

namespace TurboHTTP.Tests.Server.Middleware;

public sealed class TurboMiddlewareRegistrySpec
{
    [Fact(Timeout = 5000)]
    public void Registry_should_be_empty_by_default()
    {
        var registry = new TurboMiddlewareRegistry();
        var stages = registry.Resolve(null!);
        Assert.Empty(stages);
    }

    [Fact(Timeout = 5000)]
    public void Add_generic_should_register_stage()
    {
        var registry = new TurboMiddlewareRegistry();
        registry.Add<FakeStage>();
        var stages = registry.Resolve(null!);
        Assert.Single(stages);
        Assert.IsType<FakeStage>(stages[0]);
    }

    [Fact(Timeout = 5000)]
    public void Add_factory_should_register_stage()
    {
        var registry = new TurboMiddlewareRegistry();
        registry.Add(_ => new FakeStage());
        var stages = registry.Resolve(null!);
        Assert.Single(stages);
    }

    [Fact(Timeout = 5000)]
    public void Resolve_should_preserve_registration_order()
    {
        var registry = new TurboMiddlewareRegistry();
        registry.Add<FakeStage>();
        registry.Add<FakeStage2>();
        var stages = registry.Resolve(null!);
        Assert.Equal(2, stages.Count);
        Assert.IsType<FakeStage>(stages[0]);
        Assert.IsType<FakeStage2>(stages[1]);
    }

    private sealed class FakeStage : IServerBidiStage
    {
        public BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>
            Create(IServiceProvider services)
            => BidiFlow.FromFlows(
                Flow.Create<HttpRequestMessage>(),
                Flow.Create<HttpResponseMessage>());
    }

    private sealed class FakeStage2 : IServerBidiStage
    {
        public BidiFlow<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage, NotUsed>
            Create(IServiceProvider services)
            => BidiFlow.FromFlows(
                Flow.Create<HttpRequestMessage>(),
                Flow.Create<HttpResponseMessage>());
    }
}