using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHTTP.Internal;
using TurboHTTP.Streams;

namespace TurboHTTP.StreamTests.Streams;

/// <summary>
/// Verifies that <see cref="TransportRegistry"/> correctly registers and retrieves
/// transport factories by HTTP version using a fluent builder pattern.
/// </summary>
public sealed class TransportRegistrySpec
{
    [Fact(Timeout = 5000)]
    public void Register_should_return_this_for_fluent_chaining()
    {
        var registry = new TransportRegistry();
        var result = registry.Register(HttpVersion.Version11, new DelegateTransportFactory(MockTransport));

        Assert.Same(registry, result);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_accept_multiple_versions()
    {
        var registry = new TransportRegistry()
            .Register(HttpVersion.Version11, new DelegateTransportFactory(MockTransport))
            .Register(HttpVersion.Version20, new DelegateTransportFactory(MockTransport));

        // Get should succeed without throwing for both versions
        var flow11 = registry.Get(HttpVersion.Version11);
        Assert.NotNull(flow11);

        var flow20 = registry.Get(HttpVersion.Version20);
        Assert.NotNull(flow20);
    }

    [Fact(Timeout = 5000)]
    public void Get_should_throw_for_unregistered_version()
    {
        var registry = new TransportRegistry()
            .Register(HttpVersion.Version11, new DelegateTransportFactory(MockTransport));

        Assert.Throws<InvalidOperationException>(() => registry.Get(HttpVersion.Version20));
    }

    [Fact(Timeout = 5000)]
    public void Get_should_return_flow_from_registered_factory()
    {
        var registry = new TransportRegistry()
            .Register(HttpVersion.Version11, new DelegateTransportFactory(MockTransport));

        var flow = registry.Get(HttpVersion.Version11);

        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_allow_overwriting_existing_version()
    {
        var factory1 = new DelegateTransportFactory(MockTransport);
        var factory2 = new DelegateTransportFactory(MockTransport);

        var registry = new TransportRegistry()
            .Register(HttpVersion.Version11, factory1)
            .Register(HttpVersion.Version11, factory2);

        // Should not throw — the second registration overwrites the first
        var flow = registry.Get(HttpVersion.Version11);
        Assert.NotNull(flow);
    }

    [Fact(Timeout = 5000)]
    public void Register_should_throw_when_factory_is_null()
    {
        var registry = new TransportRegistry();

        Assert.Throws<ArgumentNullException>(() =>
            registry.Register(HttpVersion.Version11, null!));
    }

    private static Flow<IOutputItem, IInputItem, NotUsed> MockTransport()
    {
        // Return a flow that discards output items and emits nothing (for testing registration only)
        return Flow.Create<IOutputItem>()
            .SelectMany(_ => new List<IInputItem>());
    }
}
