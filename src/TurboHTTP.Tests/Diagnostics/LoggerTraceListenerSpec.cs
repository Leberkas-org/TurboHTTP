using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Servus.Core.Diagnostics;
using TurboHTTP.Diagnostics;
using static Servus.Core.Servus;

namespace TurboHTTP.Tests.Diagnostics;

[Collection("OTEL")]
public sealed class LoggerTraceListenerSpec : IDisposable
{
    private readonly TestLoggerFactory _factory = new();

    public void Dispose()
    {
        Tracing.Disable();
    }

    [Fact(Timeout = 5000)]
    public void Write_should_call_logger_with_correct_level()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Warning(this, "test message");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void InfoLevel_should_map_to_information()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Info(this, "msg");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Single(logger.LogEntries);
        Assert.Equal(LogLevel.Information, logger.LogEntries[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void DebugLevel_should_map_to_debug()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Debug(this, "msg");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Equal(LogLevel.Debug, logger.LogEntries[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void WarningLevel_should_map_to_warning()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Warning(this, "msg");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Equal(LogLevel.Warning, logger.LogEntries[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void ErrorLevel_should_map_to_error()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Error(this, "msg");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Equal(LogLevel.Error, logger.LogEntries[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void TraceLevel_should_map_to_trace()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Trace(this, "msg");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Equal(LogLevel.Trace, logger.LogEntries[0].Level);
    }

    [Fact(Timeout = 5000)]
    public void Write_should_include_source_type_and_hash()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Debug(this, "hello");

        var logger = _factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        var entry = Assert.Single(logger.LogEntries);
        Assert.Contains("LoggerTraceListenerSpec", entry.Message);
    }

    [Fact(Timeout = 5000)]
    public void Write_should_skip_format_when_logger_disabled()
    {
        var factory = new TestLoggerFactory(enabledLevel: LogLevel.Error);
        var listener = new LoggerTraceListener(factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Debug(this, "msg");

        var logger = factory.CreatedLoggers["TurboHTTP.Trace.Protocol"];
        Assert.Empty(logger.LogEntries);
    }

    [Fact(Timeout = 5000)]
    public void NullFactory_should_throw_argument_null_exception()
    {
        Assert.Throws<ArgumentNullException>(() => new LoggerTraceListener(null!));
    }

    [Fact(Timeout = 5000)]
    public void LoggerNames_should_follow_pattern()
    {
        var listener = new LoggerTraceListener(_factory);
        Tracing.Configure(listener);

        Tracing.For("Protocol").Debug(this, "test");
        Tracing.For("Request").Debug(this, "test");

        Assert.True(_factory.CreatedLoggers.ContainsKey("TurboHTTP.Trace.Protocol"));
        Assert.True(_factory.CreatedLoggers.ContainsKey("TurboHTTP.Trace.Request"));
    }

    [Fact(Timeout = 5000)]
    public void DiExtension_should_register_singleton_and_configure()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddTurboLoggerTracing();

        var provider = services.BuildServiceProvider();
        var listener = provider.GetRequiredService<IServusTraceListener>();

        Assert.NotNull(listener);
        Assert.IsType<LoggerTraceListener>(listener);
    }

    private sealed class TestLoggerFactory(LogLevel enabledLevel = LogLevel.Trace) : ILoggerFactory
    {
        public Dictionary<string, TestLogger> CreatedLoggers { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            var logger = new TestLogger(enabledLevel);
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

    private sealed class TestLogger(LogLevel enabledLevel = LogLevel.Trace) : ILogger
    {
        public List<LogEntry> LogEntries { get; } = [];

        public bool IsEnabled(LogLevel logLevel) => logLevel >= enabledLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
