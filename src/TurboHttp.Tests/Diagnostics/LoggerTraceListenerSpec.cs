using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class LoggerTraceListenerSpec : IDisposable
{
    private readonly TestLoggerFactory _factory = new();

    public void Dispose()
    {
        TurboTrace.Disable();
    }

    [Fact]
    public void Constructor_should_create_logger_per_category()
    {
        _ = new LoggerTraceListener(_factory);

        Assert.Equal(10, _factory.CreatedLoggers.Count);
    }

    [Fact]
    public void Write_should_call_logger_with_correct_level()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, TurboTraceCategory.Protocol,
            "TestSource", 0x12345678, "test message", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
    }

    [Fact]
    public void InfoLevel_should_map_to_information()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Info, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Information, logger.LogEntries[0].Level);
    }

    [Fact]
    public void DebugLevel_should_map_to_debug()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Debug, logger.LogEntries[0].Level);
    }

    [Fact]
    public void WarningLevel_should_map_to_warning()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
    }

    [Fact]
    public void ErrorLevel_should_map_to_error()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Error, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Error, logger.LogEntries[0].Level);
    }

    [Fact]
    public void TraceLevel_should_map_to_trace()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Trace, logger.LogEntries[0].Level);
    }

    [Fact]
    public void IsEnabled_should_respect_minimum_level()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Warning);

        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Protocol));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Info, TurboTraceCategory.Protocol));
        Assert.True(listener.IsEnabled(TurboTraceLevel.Warning, TurboTraceCategory.Protocol));
        Assert.True(listener.IsEnabled(TurboTraceLevel.Error, TurboTraceCategory.Protocol));
    }

    [Fact]
    public void IsEnabled_should_respect_category_filter()
    {
        var listener = new LoggerTraceListener(_factory, TurboTraceCategory.Protocol);

        Assert.True(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Protocol));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Connection));
    }

    [Fact]
    public void Write_should_include_source_type_and_hash()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "MyDecoder", 0x1A2B3C4D, "hello", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        var entry = Assert.Single(logger.LogEntries);
        Assert.Contains("MyDecoder", entry.Message);
        Assert.Contains("1A2B3C4D", entry.Message);
    }

    [Fact]
    public void Write_should_skip_format_when_logger_disabled()
    {
        var factory = new TestLoggerFactory(enabledLevel: LogLevel.Error);
        var listener = new LoggerTraceListener(factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Empty(logger.LogEntries);
    }

    [Fact]
    public void NullFactory_should_throw_argument_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggerTraceListener(null!));
    }

    [Fact]
    public void LoggerNames_should_follow_pattern()
    {
        _ = new LoggerTraceListener(_factory);

        var expectedNames = new[]
        {
            "TurboHttp.Trace.Connection",
            "TurboHttp.Trace.Protocol",
            "TurboHttp.Trace.Request",
            "TurboHttp.Trace.Response",
            "TurboHttp.Trace.Cache",
            "TurboHttp.Trace.Redirect",
            "TurboHttp.Trace.Retry",
            "TurboHttp.Trace.Pool",
            "TurboHttp.Trace.Transport",
            "TurboHttp.Trace.Stream",
        };

        foreach (var name in expectedNames)
        {
            Assert.True(_factory.CreatedLoggers.ContainsKey(name), $"Expected logger '{name}' was not created");
        }
    }

    [Fact]
    public void CombinedCategoryFilter_should_work()
    {
        var listener = new LoggerTraceListener(
            _factory,
            TurboTraceCategory.Protocol | TurboTraceCategory.Connection);

        Assert.True(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Protocol));
        Assert.True(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Connection));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Request));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Cache));
    }

    [Fact]
    public void DiExtension_should_register_singleton_and_configure()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTurboLoggerTracing(TurboTraceCategory.Protocol, TurboTraceLevel.Debug);

        var provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<ITurboTraceListener>();

        Assert.NotNull(listener);
        Assert.IsType<LoggerTraceListener>(listener);
        Assert.True(TurboTrace.ShouldTrace(TurboTraceCategory.Protocol, TurboTraceLevel.Debug));
    }


    private sealed class TestLoggerFactory : ILoggerFactory
    {
        private readonly LogLevel _enabledLevel;

        public Dictionary<string, TestLogger> CreatedLoggers { get; } = new();

        public TestLoggerFactory(LogLevel enabledLevel = LogLevel.Trace)
        {
            _enabledLevel = enabledLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new TestLogger(_enabledLevel);
            CreatedLoggers[categoryName] = logger;
            return logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger : ILogger
    {
        private readonly LogLevel _enabledLevel;

        public List<LogEntry> LogEntries { get; } = new();

        public TestLogger(LogLevel enabledLevel = LogLevel.Trace)
        {
            _enabledLevel = enabledLevel;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _enabledLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
