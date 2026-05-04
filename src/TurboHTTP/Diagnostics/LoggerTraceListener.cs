using Microsoft.Extensions.Logging;
using Servus.Core.Diagnostics;

namespace TurboHTTP.Diagnostics;

internal sealed class LoggerTraceListener : IServusTraceListener
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, ILogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public LoggerTraceListener(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
    }

    public bool IsEnabled(TraceLevel level, string category)
    {
        var logger = GetOrCreateLogger(category);
        return logger.IsEnabled(MapLevel(level));
    }

    public void Write(in TraceEvent evt)
    {
        var logger = GetOrCreateLogger(evt.Category);
        var logLevel = MapLevel(evt.Level);
        if (!logger.IsEnabled(logLevel))
        {
            return;
        }

        var message = evt.FormatMessage();
        logger.Log(logLevel, "[{SourceType}#{SourceHash:X8}] {Message}",
            evt.SourceType, evt.SourceHash, message);
    }

    private ILogger GetOrCreateLogger(string category)
    {
        if (!_loggers.TryGetValue(category, out var logger))
        {
            logger = _loggerFactory.CreateLogger(string.Concat("TurboHTTP.Trace.", category));
            _loggers[category] = logger;
        }

        return logger;
    }

    private static LogLevel MapLevel(TraceLevel level)
    {
        return level switch
        {
            TraceLevel.Trace => LogLevel.Trace,
            TraceLevel.Debug => LogLevel.Debug,
            TraceLevel.Info => LogLevel.Information,
            TraceLevel.Warning => LogLevel.Warning,
            TraceLevel.Error => LogLevel.Error,
            _ => LogLevel.None,
        };
    }
}
