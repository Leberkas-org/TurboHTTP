using TurboHTTP.Transport.Connection;

namespace TurboHTTP.Transport.Quic;

/// <summary>
/// Specifies the directionality of a transport stream.
/// Used by <see cref="ClientState"/> to allocate only the channels and pipes
/// needed for the given direction, avoiding deadlocks on unidirectional QUIC streams.
/// </summary>
public enum StreamDirection
{
    /// <summary>Both read and write — standard bidirectional stream (HTTP/1.x, HTTP/2, HTTP/3 request streams).</summary>
    Bidirectional,

    /// <summary>Write-only — outbound unidirectional stream (HTTP/3 control stream, QPACK encoder stream).</summary>
    WriteOnly,

    /// <summary>Read-only — server-initiated inbound unidirectional stream (HTTP/3 server control, QPACK decoder).</summary>
    ReadOnly
}
