namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// Result of a frame decode attempt.
/// </summary>
internal enum DecodeStatus
{
    /// <summary>A complete frame was decoded.</summary>
    Success,

    /// <summary>Not enough data to decode a complete frame; feed more bytes.</summary>
    NeedMoreData,
}