using System.Diagnostics;

namespace TurboHTTP.Diagnostics;

/// <summary>
/// Immutable trace event with deferred message formatting.
/// Stores the template and up to 3 arguments; <see cref="FormatMessage"/>
/// allocates a formatted string only when called.
/// </summary>
public readonly struct TraceEvent
{
    /// <summary>Timestamp from <see cref="Stopwatch.GetTimestamp"/>.</summary>
    public long TimestampTicks { get; }

    /// <summary>Severity level of this event.</summary>
    public TurboTraceLevel Level { get; }

    /// <summary>Category that produced this event.</summary>
    public TurboTraceCategory Category { get; }

    /// <summary>Short type name of the source object (from <c>GetType().Name</c>).</summary>
    public string SourceType { get; }

    /// <summary>Identity hash of the source object (from <c>GetHashCode()</c>).</summary>
    public int SourceHash { get; }

    /// <summary>Format template (compatible with <see cref="string.Format(string,object?)"/>).</summary>
    public string Template { get; }

    private readonly object?[] _args;

    internal TraceEvent(
        long timestampTicks,
        TurboTraceLevel level,
        TurboTraceCategory category,
        string sourceType,
        int sourceHash,
        string template,
        params object?[] args)
    {
        TimestampTicks = timestampTicks;
        Level = level;
        Category = category;
        SourceType = sourceType;
        SourceHash = sourceHash;
        Template = template;
        _args = args;
    }

    /// <summary>
    /// Formats the message by applying stored arguments to the template.
    /// This is the only method that allocates a string.
    /// </summary>
    public string FormatMessage()
    {
        return string.Format(Template, args: _args);
    }
}