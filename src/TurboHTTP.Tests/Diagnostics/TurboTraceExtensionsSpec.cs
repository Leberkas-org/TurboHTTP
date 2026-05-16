using Microsoft.Extensions.DependencyInjection;
using Servus.Core.Diagnostics;
using TurboHTTP.Diagnostics;
using static Servus.Core.Servus;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboTraceExtensionsSpec : IDisposable
{
    public void Dispose()
    {
        Tracing.Disable();
    }

    [Fact(Timeout = 5000)]
    public void AddTurboLoggerTracing_should_register_listener()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTurboLoggerTracing();

        var provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<IServusTraceListener>();

        Assert.NotNull(listener);
        Assert.IsType<LoggerTraceListener>(listener);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboLoggerTracing_should_return_collection_for_chaining()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddTurboLoggerTracing();

        Assert.Same(services, result);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboTracing_should_register_custom_listener()
    {
        var services = new ServiceCollection();
        var customListener = new MockTraceListener();

        services.AddTurboTracing(customListener);

        var provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<IServusTraceListener>();

        Assert.Same(customListener, listener);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboTracing_should_return_collection_for_chaining()
    {
        var services = new ServiceCollection();
        var customListener = new MockTraceListener();

        var result = services.AddTurboTracing(customListener);

        Assert.Same(services, result);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboTracing_should_throw_when_listener_null()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentNullException>(() =>
            services.AddTurboTracing(null!)
        );

        Assert.NotNull(ex);
    }

    private sealed class MockTraceListener : IServusTraceListener
    {
        public List<TraceEvent> Events { get; } = [];

        public bool IsEnabled(TraceLevel level, string category) => true;

        public void Write(in TraceEvent evt) => Events.Add(evt);
    }
}