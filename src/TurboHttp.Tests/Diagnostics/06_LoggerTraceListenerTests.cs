using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TurboHttp.Diagnostics;

namespace TurboHttp.Tests.Diagnostics;

[Collection("TurboTrace")]
public sealed class LoggerTraceListenerTests : IDisposable
{
    private readonly TestLoggerFactory _factory = new();

    public void Dispose()
    {
        TurboTrace.Disable();
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-001: Constructor creates logger per category")]
    public void Constructor_CreatesLoggerPerCategory()
    {
        _ = new LoggerTraceListener(_factory);

        Assert.Equal(10, _factory.CreatedLoggers.Count);
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-002: Write calls ILogger.Log with correct level")]
    public void Write_CallsLoggerWithCorrectLevel()
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

    [Fact(DisplayName = "Diagnostics-LoggerListener-003: Info level maps to LogLevel.Information")]
    public void InfoLevel_MapsToInformation()
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

    [Fact(DisplayName = "Diagnostics-LoggerListener-004: Debug level maps to LogLevel.Debug")]
    public void DebugLevel_MapsToDebug()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Debug, logger.LogEntries[0].Level);
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-005: Warning level maps to LogLevel.Warning")]
    public void WarningLevel_MapsToWarning()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Warning, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-006: Error level maps to LogLevel.Error")]
    public void ErrorLevel_MapsToError()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Error, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Error, logger.LogEntries[0].Level);
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-007: Trace level maps to LogLevel.Trace")]
    public void TraceLevel_MapsToTrace()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Trace);
        var evt = new TraceEvent(
            Stopwatch.GetTimestamp(), TurboTraceLevel.Trace, TurboTraceCategory.Protocol,
            "Test", 0, "msg", 0, null, null, null);

        listener.Write(in evt);

        var logger = _factory.CreatedLoggers["TurboHttp.Trace.Protocol"];
        Assert.Equal(LogLevel.Trace, logger.LogEntries[0].Level);
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-008: IsEnabled respects minimum level")]
    public void IsEnabled_RespectsMinimumLevel()
    {
        var listener = new LoggerTraceListener(_factory, minimumLevel: TurboTraceLevel.Warning);

        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Protocol));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Info, TurboTraceCategory.Protocol));
        Assert.True(listener.IsEnabled(TurboTraceLevel.Warning, TurboTraceCategory.Protocol));
        Assert.True(listener.IsEnabled(TurboTraceLevel.Error, TurboTraceCategory.Protocol));
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-009: IsEnabled respects category filter")]
    public void IsEnabled_RespectsCategoryFilter()
    {
        var listener = new LoggerTraceListener(_factory, TurboTraceCategory.Protocol);

        Assert.True(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Protocol));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Connection));
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-010: Write includes SourceType and SourceHash in output")]
    public void Write_IncludesSourceTypeAndHash()
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

    [Fact(DisplayName = "Diagnostics-LoggerListener-011: Write skips FormatMessage when logger not enabled")]
    public void Write_SkipsFormatWhenLoggerDisabled()
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

    [Fact(DisplayName = "Diagnostics-LoggerListener-012: Null ILoggerFactory throws ArgumentNullException")]
    public void NullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggerTraceListener(null!));
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-013: Logger name follows TurboHttp.Trace.Category pattern")]
    public void LoggerNames_FollowPattern()
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

    [Fact(DisplayName = "Diagnostics-LoggerListener-014: Combined category filter works correctly")]
    public void CombinedCategoryFilter_Works()
    {
        var listener = new LoggerTraceListener(
            _factory,
            TurboTraceCategory.Protocol | TurboTraceCategory.Connection);

        Assert.True(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Protocol));
        Assert.True(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Connection));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Request));
        Assert.False(listener.IsEnabled(TurboTraceLevel.Debug, TurboTraceCategory.Cache));
    }

    [Fact(DisplayName = "Diagnostics-LoggerListener-015: DI extension registers singleton and configures TurboTrace")]
    public void DiExtension_RegistersSingletonAndConfigures()
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

    // ── Test doubles ─────────────────────────────────────────────────

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

        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
