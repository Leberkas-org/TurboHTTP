namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// Result of processing a remote SETTINGS frame.
/// </summary>
internal readonly struct SettingsResult
{
    public int? MaxConcurrentStreamsChange { get; init; }
    public int? InitialWindowSizeChange { get; init; }
    public SettingsFrame? AckFrame { get; init; }
}