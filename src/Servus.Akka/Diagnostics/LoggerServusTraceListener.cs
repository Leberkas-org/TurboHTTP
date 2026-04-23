using Microsoft.Extensions.Logging;

namespace Servus.Akka.Diagnostics;

/// <summary>
/// Routes <see cref="ServusTraceEvent"/> instances to <see cref="ILoggerFactory"/>,
/// creating one <see cref="ILogger"/> per <see cref="ServusTraceCategory"/>.
/// Logger names follow the pattern <c>Servus.Akka.Trace.{Category}</c>.
/// </summary>
internal sealed class LoggerServusTraceListener : IServusTraceListener
{
    private readonly Dictionary<ServusTraceCategory, ILogger> _loggers;
    private readonly ServusTraceCategory _enabledCategories;
    private readonly ServusTraceLevel _minimumLevel;

    public LoggerServusTraceListener(
        ILoggerFactory loggerFactory,
        ServusTraceCategory categories = ServusTraceCategory.All,
        ServusTraceLevel minimumLevel = ServusTraceLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _enabledCategories = categories;
        _minimumLevel = minimumLevel;
        _loggers = CreateLoggers(loggerFactory);
    }

    /// <inheritdoc />
    public bool IsEnabled(ServusTraceLevel level, ServusTraceCategory category)
    {
        return level >= _minimumLevel && (category & _enabledCategories) != 0;
    }

    /// <inheritdoc />
    public void Write(in ServusTraceEvent evt)
    {
        if (!_loggers.TryGetValue(evt.Category, out var logger)) return;
        var logLevel = (LogLevel)evt.Level;
        if (!logger.IsEnabled(logLevel)) return;
        var message = evt.FormatMessage();
        logger.Log(logLevel, "[{SourceType}#{SourceHash:X8}] {Message}",
            evt.SourceType, evt.SourceHash, message);
    }

    private static Dictionary<ServusTraceCategory, ILogger> CreateLoggers(ILoggerFactory loggerFactory)
    {
        return new Dictionary<ServusTraceCategory, ILogger>
        {
            [ServusTraceCategory.Connection] = loggerFactory.CreateLogger("Servus.Akka.Trace.Connection"),
            [ServusTraceCategory.Dns] = loggerFactory.CreateLogger("Servus.Akka.Trace.Dns"),
            [ServusTraceCategory.Tls] = loggerFactory.CreateLogger("Servus.Akka.Trace.Tls"),
            [ServusTraceCategory.Pool] = loggerFactory.CreateLogger("Servus.Akka.Trace.Pool"),
        };
    }
}
