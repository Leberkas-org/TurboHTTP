namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9113 §5.1 — HTTP/2 stream states.
/// Test-only copy — the production type was removed along with Http2Decoder.
/// </summary>
public enum Http2StreamLifecycleState
{
    Idle,
    Open,
    HalfClosedRemote,
    Closed,
}
