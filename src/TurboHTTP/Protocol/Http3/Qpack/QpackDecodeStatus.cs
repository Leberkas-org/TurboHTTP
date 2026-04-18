namespace TurboHTTP.Protocol.Http3.Qpack;

/// <summary>
/// Decode status for QPACK instruction parsing.
/// </summary>
internal enum QpackDecodeStatus
{
    /// <summary>An instruction was successfully decoded.</summary>
    Success,

    /// <summary>Not enough data to decode a complete instruction.</summary>
    NeedMoreData
}