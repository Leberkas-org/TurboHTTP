namespace TurboHTTP.Protocol.Http2;

internal sealed class Http2Exception(
    string message,
    Http2ErrorCode errorCode = Http2ErrorCode.ProtocolError,
    Http2ErrorScope scope = Http2ErrorScope.Connection,
    int streamId = 0)
    : TurboProtocolException(message)
{
    public Http2ErrorCode ErrorCode { get; } = errorCode;

    /// <summary>
    /// Whether this error terminates the connection (Connection) or only resets a stream (Stream).
    /// Defaults to Connection — the safe conservative choice per RFC 9113 §5.4.
    /// </summary>
    public Http2ErrorScope Scope { get; } = scope;

    /// <summary>
    /// For stream errors, the ID of the affected stream. Zero for connection errors.
    /// </summary>
    public int StreamId { get; } = streamId;

    /// <summary>True when this error terminates the entire HTTP/2 connection.</summary>
    public bool IsConnectionError => Scope == Http2ErrorScope.Connection;
}
