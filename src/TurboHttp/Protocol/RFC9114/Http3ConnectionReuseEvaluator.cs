using System.Security.Cryptography.X509Certificates;

namespace TurboHttp.Protocol.RFC9114;

/// <summary>
/// RFC 9114 §3.3 — Evaluates whether an HTTP/3 QUIC connection can be reused
/// for a new request to a different origin. HTTP/3 connections are persistent
/// and multiplexed, but reuse across origins requires certificate validation.
/// </summary>
public static class Http3ConnectionReuseEvaluator
{
    /// <summary>
    /// Determines whether an existing HTTP/3 connection can be reused for a request
    /// to the specified target origin.
    /// </summary>
    /// <param name="connectionScheme">The scheme of the existing connection (e.g. "https").</param>
    /// <param name="connectionHost">The hostname the existing connection was established to.</param>
    /// <param name="connectionPort">The port of the existing connection.</param>
    /// <param name="targetScheme">The scheme of the new request.</param>
    /// <param name="targetHost">The hostname of the new request.</param>
    /// <param name="targetPort">The port of the new request.</param>
    /// <param name="serverCertificate">
    /// The TLS certificate presented by the server on the existing connection.
    /// When null, the connection cannot be reused for a different origin.
    /// </param>
    /// <param name="isGoingAway">
    /// True if the server has sent a GOAWAY frame on this connection.
    /// RFC 9114 §5.2: after GOAWAY, no new requests should be sent.
    /// </param>
    /// <returns>A <see cref="Http3ConnectionReuseDecision"/> indicating whether reuse is allowed.</returns>
    public static Http3ConnectionReuseDecision Evaluate(
        string connectionScheme,
        string connectionHost,
        int connectionPort,
        string targetScheme,
        string targetHost,
        int targetPort,
        X509Certificate2? serverCertificate,
        bool isGoingAway = false)
    {
        // RFC 9114 §5.2: After receiving GOAWAY, clients MUST NOT send new requests.
        if (isGoingAway)
        {
            return Http3ConnectionReuseDecision.NewConnection(
                "RFC 9114 §5.2: Server sent GOAWAY; new requests must use a new connection.");
        }

        // RFC 9114 §3.3: Connections are identified by (scheme, host, port).
        // Same origin always allows reuse.
        if (IsSameOrigin(connectionScheme, connectionHost, connectionPort,
                targetScheme, targetHost, targetPort))
        {
            return Http3ConnectionReuseDecision.Reuse(
                "RFC 9114 §3.3: Same origin — connection reuse permitted.");
        }

        // RFC 9114 §3.3: Cross-origin reuse requires that the server's certificate
        // is valid for the target hostname.
        if (serverCertificate is null)
        {
            return Http3ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: No server certificate available; cannot verify target hostname coverage.");
        }

        // RFC 9114 §3.3: Cross-origin coalescing requires same scheme and port.
        // A QUIC connection to port 443 cannot serve requests to port 8443.
        if (!string.Equals(connectionScheme, targetScheme, StringComparison.OrdinalIgnoreCase))
        {
            return Http3ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: Scheme mismatch — connection reuse not permitted.");
        }

        if (connectionPort != targetPort)
        {
            return Http3ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: Port mismatch — connection reuse not permitted.");
        }

        if (!Http3CertificateValidator.CoversHostname(serverCertificate, targetHost))
        {
            return Http3ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: Server certificate does not cover target hostname; new connection required.");
        }

        return Http3ConnectionReuseDecision.Reuse(
            $"RFC 9114 §3.3: Certificate covers '{targetHost}' — cross-origin connection reuse permitted.");
    }

    private static bool IsSameOrigin(
        string scheme1, string host1, int port1,
        string scheme2, string host2, int port2) =>
        string.Equals(scheme1, scheme2, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(host1, host2, StringComparison.OrdinalIgnoreCase) &&
        port1 == port2;
}

/// <summary>
/// Result of evaluating whether an HTTP/3 connection can be reused.
/// </summary>
public sealed record Http3ConnectionReuseDecision
{
    /// <summary>Whether the existing connection can be reused for the new request.</summary>
    public bool CanReuse { get; private init; }

    /// <summary>Human-readable reason for the decision (diagnostics/logging).</summary>
    public string Reason { get; private init; } = string.Empty;

    /// <summary>Creates a reuse-allowed decision.</summary>
    public static Http3ConnectionReuseDecision Reuse(string reason) =>
        new() { CanReuse = true, Reason = reason };

    /// <summary>Creates a new-connection-required decision.</summary>
    public static Http3ConnectionReuseDecision NewConnection(string reason) =>
        new() { CanReuse = false, Reason = reason };
}
