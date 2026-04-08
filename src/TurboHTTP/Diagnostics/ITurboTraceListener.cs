namespace TurboHTTP.Diagnostics;

/// <summary>
/// Receives trace events from the TurboTrace system.
/// Implementations must be thread-safe — <see cref="Write"/> may be called concurrently.
/// </summary>
public interface ITurboTraceListener
{
    /// <summary>
    /// Writes a trace event. The event is passed by reference to avoid copying.
    /// </summary>
    void Write(in TraceEvent evt);

    /// <summary>
    /// Returns whether this listener accepts events at the given level and category.
    /// </summary>
    bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category);
}
