using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TurboHTTP.Diagnostics;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class TurboTraceExtensionsSpec : IDisposable
{
    public void Dispose()
    {
        TurboTrace.Disable();
    }

    [Fact(Timeout = 5000)]
    public void AddTurboLoggerTracing_should_register_listener()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTurboLoggerTracing();

        var provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<ITurboTraceListener>();

        Assert.NotNull(listener);
        Assert.IsType<LoggerTraceListener>(listener);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboLoggerTracing_should_configure_trace()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTurboLoggerTracing(TurboTraceCategory.Protocol);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ITurboTraceListener>();

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }

    [Fact(Timeout = 5000)]
    public void AddTurboLoggerTracing_should_filter_by_category()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTurboLoggerTracing(TurboTraceCategory.Protocol);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ITurboTraceListener>();

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Connection, TurboTraceLevel.Debug));
    }

    [Fact(Timeout = 5000)]
    public void AddTurboLoggerTracing_should_filter_by_minimum_level()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTurboLoggerTracing(TurboTraceCategory.All, TurboTraceLevel.Warning);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ITurboTraceListener>();

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Warning));
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
        var listener = provider.GetRequiredService<ITurboTraceListener>();

        Assert.Same(customListener, listener);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboTracing_should_configure_trace_immediately()
    {
        var services = new ServiceCollection();
        var customListener = new MockTraceListener();

        services.AddTurboTracing(customListener, TurboTraceCategory.Protocol);

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
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
            services.AddTurboTracing(null!, TurboTraceCategory.All)
        );

        Assert.NotNull(ex);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboTracing_should_filter_by_category()
    {
        var services = new ServiceCollection();
        var customListener = new MockTraceListener();

        services.AddTurboTracing(customListener, TurboTraceCategory.Request);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ITurboTraceListener>();

        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Request, TurboTraceLevel.Debug));
        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Response, TurboTraceLevel.Debug));
    }

    [Fact(Timeout = 5000)]
    public void AddTurboTracing_should_filter_by_minimum_level()
    {
        var services = new ServiceCollection();
        var customListener = new MockTraceListener();

        services.AddTurboTracing(customListener, TurboTraceCategory.All, TurboTraceLevel.Info);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<ITurboTraceListener>();

        Assert.False(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Info));
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpMetrics_should_add_meter()
    {
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddTurboHttpMetrics()
            .Build();

        Assert.NotNull(meterProvider);
    }

    [Fact(Timeout = 5000)]
    public void AddTurboHttpTracing_should_add_source()
    {
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddTurboHttpTracing()
            .Build();

        Assert.NotNull(tracerProvider);
    }

    private sealed class MockTraceListener : ITurboTraceListener
    {
        public List<TraceEvent> Events { get; } = [];

        public bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category) => true;

        public void Write(in TraceEvent evt) => Events.Add(evt);
    }
}
