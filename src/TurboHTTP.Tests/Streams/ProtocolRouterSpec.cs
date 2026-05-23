using TurboHTTP.Server;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Streams;

public sealed class ProtocolRouterSpec
{
    private static readonly TurboServerOptions DefaultOptions = new();

    [Fact(Timeout = 5000)]
    public void ResolveEngine_should_return_http10_for_http10_version()
    {
        var engine = ProtocolRouter.ResolveEngine(new Version(1, 0), DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<Http10ServerEngine>(engine);
    }

    [Fact(Timeout = 5000)]
    public void ResolveEngine_should_return_http11_for_http11_version()
    {
        var engine = ProtocolRouter.ResolveEngine(new Version(1, 1), DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<Http11ServerEngine>(engine);
    }

    [Fact(Timeout = 5000)]
    public void ResolveEngine_should_return_http20_for_http20_version()
    {
        var engine = ProtocolRouter.ResolveEngine(new Version(2, 0), DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<Http20ServerEngine>(engine);
    }

    [Fact(Timeout = 5000)]
    public void ResolveEngine_should_return_http30_for_http30_version()
    {
        var engine = ProtocolRouter.ResolveEngine(new Version(3, 0), DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<Http30ServerEngine>(engine);
    }

    [Fact(Timeout = 5000)]
    public void ResolveEngine_should_return_http11_for_unknown_version()
    {
        var engine = ProtocolRouter.ResolveEngine(new Version(4, 0), DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<Http11ServerEngine>(engine);
    }

    [Fact(Timeout = 5000)]
    public void ResolveNegotiating_should_return_negotiating_engine()
    {
        var engine = ProtocolRouter.ResolveNegotiating(DefaultOptions);

        Assert.NotNull(engine);
        Assert.IsType<NegotiatingServerEngine>(engine);
    }
}