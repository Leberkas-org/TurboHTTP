using System;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 7540 §5.4: Distinguishes connection errors (which terminate the entire connection)
/// from stream errors (which reset only the affected stream via RST_STREAM).
/// </summary>
public enum Http2ErrorScope
{
    /// <summary>
    /// RFC 7540 §5.4.1: A connection error terminates the HTTP/2 connection.
    /// The sender MUST send a GOAWAY frame then close the TCP connection.
    /// </summary>
    Connection,

    /// <summary>
    /// RFC 7540 §5.4.2: A stream error affects only the single stream.
    /// The sender SHOULD send RST_STREAM and continue using the connection.
    /// </summary>
    Stream,
}

public sealed class Http2Exception(
    string message,
    Http2ErrorCode errorCode = Http2ErrorCode.ProtocolError,
    Http2ErrorScope scope = Http2ErrorScope.Connection,
    int streamId = 0)
    : Exception(message)
{
    public Http2ErrorCode ErrorCode { get; } = errorCode;

    /// <summary>
    /// Whether this error terminates the connection (Connection) or only resets a stream (Stream).
    /// Defaults to Connection — the safe conservative choice per RFC 7540 §5.4.
    /// </summary>
    public Http2ErrorScope Scope { get; } = scope;

    /// <summary>
    /// For stream errors, the ID of the affected stream. Zero for connection errors.
    /// </summary>
    public int StreamId { get; } = streamId;

    /// <summary>True when this error terminates the entire HTTP/2 connection.</summary>
    public bool IsConnectionError => Scope == Http2ErrorScope.Connection;
}
