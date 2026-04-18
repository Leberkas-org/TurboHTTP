namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Result of processing an inbound DATA frame through flow control.
/// </summary>
internal readonly struct FlowControlResult
{
    public bool Success { get; init; }
    public bool IsConnectionViolation { get; init; }
    public bool IsStreamViolation { get; init; }
    public int ViolationStreamId { get; init; }
    public WindowUpdateFrame? ConnectionWindowUpdate { get; init; }
    public WindowUpdateFrame? StreamWindowUpdate { get; init; }
}