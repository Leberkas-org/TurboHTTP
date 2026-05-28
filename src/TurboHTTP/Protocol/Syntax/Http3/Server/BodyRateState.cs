namespace TurboHTTP.Protocol.Syntax.Http3.Server;

/// <summary>
/// Tracks request body data rate for a single stream.
/// Used to enforce minimum data rate with grace period, compatible with Kestrel's timeout model.
/// </summary>
internal sealed class BodyRateState
{
    /// <summary>
    /// Total bytes received on this stream.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Bytes recorded at last check time (used to calculate rate).
    /// </summary>
    public long LastCheckBytes { get; set; }

    /// <summary>
    /// Timestamp (in milliseconds from Environment.TickCount64) of last rate check.
    /// </summary>
    public long LastCheckTimestamp { get; set; } = Environment.TickCount64;

    /// <summary>
    /// Timestamp (in milliseconds from Environment.TickCount64) when grace period started.
    /// </summary>
    public long GracePeriodStartTimestamp { get; set; } = Environment.TickCount64;

    /// <summary>
    /// Whether the stream is currently in its grace period (allowed to have slow data rate).
    /// </summary>
    public bool InGracePeriod { get; set; }
}