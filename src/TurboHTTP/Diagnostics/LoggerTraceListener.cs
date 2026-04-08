using Microsoft.Extensions.Logging;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Routes <see cref="TraceEvent"/> instances to <see cref="ILoggerFactory"/>,
/// creating one <see cref="ILogger"/> per <see cref="TurboTraceCategory"/>.
/// Logger names follow the pattern <c>TurboHttp.Trace.{Category}</c>.
/// </summary>
public sealed class LoggerTraceListener : ITurboTraceListener
{
    private readonly Dictionary<TurboTraceCategory, ILogger> _loggers;
    private readonly TurboTraceCategory _enabledCategories;
    private readonly TurboTraceLevel _minimumLevel;

    /// <summary>
    /// Creates a new listener that routes trace events to loggers from the given factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to create category loggers from.</param>
    /// <param name="categories">Bitwise combination of categories to enable.</param>
    /// <param name="minimumLevel">Minimum trace level to accept.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="loggerFactory"/> is null.</exception>
    public LoggerTraceListener(
        ILoggerFactory loggerFactory,
        TurboTraceCategory categories = TurboTraceCategory.All,
        TurboTraceLevel minimumLevel = TurboTraceLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _enabledCategories = categories;
        _minimumLevel = minimumLevel;
        _loggers = CreateLoggers(loggerFactory);
    }

    /// <inheritdoc />
    public bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category)
    {
        return level >= _minimumLevel && (category & _enabledCategories) != 0;
    }

    /// <inheritdoc />
    public void Write(in TraceEvent evt)
    {
        if (!_loggers.TryGetValue(evt.Category, out var logger)) return;
        var logLevel = (LogLevel)evt.Level;
        if (!logger.IsEnabled(logLevel)) return;
        var message = evt.FormatMessage();
        logger.Log(logLevel, "[{SourceType}#{SourceHash:X8}] {Message}",
            evt.SourceType, evt.SourceHash, message);
    }

    private static Dictionary<TurboTraceCategory, ILogger> CreateLoggers(ILoggerFactory loggerFactory)
    {
        return new Dictionary<TurboTraceCategory, ILogger>
        {
            [TurboTraceCategory.Connection] = loggerFactory.CreateLogger("TurboHTTP.Trace.Connection"),
            [TurboTraceCategory.Protocol] = loggerFactory.CreateLogger("TurboHTTP.Trace.Protocol"),
            [TurboTraceCategory.Request] = loggerFactory.CreateLogger("TurboHTTP.Trace.Request"),
            [TurboTraceCategory.Response] = loggerFactory.CreateLogger("TurboHTTP.Trace.Response"),
            [TurboTraceCategory.Cache] = loggerFactory.CreateLogger("TurboHTTP.Trace.Cache"),
            [TurboTraceCategory.Redirect] = loggerFactory.CreateLogger("TurboHTTP.Trace.Redirect"),
            [TurboTraceCategory.Retry] = loggerFactory.CreateLogger("TurboHTTP.Trace.Retry"),
            [TurboTraceCategory.Pool] = loggerFactory.CreateLogger("TurboHTTP.Trace.Pool"),
            [TurboTraceCategory.Transport] = loggerFactory.CreateLogger("TurboHTTP.Trace.Transport"),
            [TurboTraceCategory.Stream] = loggerFactory.CreateLogger("TurboHTTP.Trace.Stream"),
        };
    }
}