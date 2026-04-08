namespace TurboHTTP.Diagnostics;

/// <summary>
/// Severity level for TurboTrace events.
/// Numeric values support <c>&gt;=</c> comparison for minimum-level filtering.
/// </summary>
public enum TurboTraceLevel : byte
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
}
