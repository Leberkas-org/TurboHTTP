namespace TurboHTTP.Protocol.Http2;

/// <summary>
/// RFC 9113 §5.4: Distinguishes connection errors (which terminate the entire connection)
/// from stream errors (which reset only the affected stream via RST_STREAM).
/// </summary>
internal enum Http2ErrorScope
{
    /// <summary>
    /// RFC 9113 §5.4.1: A connection error terminates the HTTP/2 connection.
    /// The sender MUST send a GOAWAY frame then close the TCP connection.
    /// </summary>
    Connection,

    /// <summary>
    /// RFC 9113 §5.4.2: A stream error affects only the single stream.
    /// The sender SHOULD send RST_STREAM and continue using the connection.
    /// </summary>
    Stream,
}