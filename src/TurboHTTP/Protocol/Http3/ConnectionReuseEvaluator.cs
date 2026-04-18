using System.Security.Cryptography.X509Certificates;

namespace TurboHTTP.Protocol.Http3;

/// <summary>
/// RFC 9114 §3.3 — Evaluates whether an HTTP/3 QUIC connection can be reused
/// for a new request to a different origin. HTTP/3 connections are persistent
/// and multiplexed, but reuse across origins requires certificate validation.
///
/// Certificate hostname matching follows RFC 6125 §6.4:
/// - Exact match of Subject Alternative Name (SAN) dNSName entries
/// - Single-level wildcard matching (e.g. *.example.com matches foo.example.com)
/// - Common Name (CN) fallback only when no SAN dNSName entries exist
/// </summary>
internal static class ConnectionReuseEvaluator
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
    /// <returns>A <see cref="ConnectionReuseDecision"/> indicating whether reuse is allowed.</returns>
    public static ConnectionReuseDecision Evaluate(
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
            return ConnectionReuseDecision.NewConnection(
                "RFC 9114 §5.2: Server sent GOAWAY; new requests must use a new connection.");
        }

        // RFC 9114 §3.3: Connections are identified by (scheme, host, port).
        // Same origin always allows reuse.
        if (IsSameOrigin(connectionScheme, connectionHost, connectionPort,
                targetScheme, targetHost, targetPort))
        {
            return ConnectionReuseDecision.Reuse(
                "RFC 9114 §3.3: Same origin — connection reuse permitted.");
        }

        // RFC 9114 §3.3: Cross-origin reuse requires that the server's certificate
        // is valid for the target hostname.
        if (serverCertificate is null)
        {
            return ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: No server certificate available; cannot verify target hostname coverage.");
        }

        // RFC 9114 §3.3: Cross-origin coalescing requires same scheme and port.
        // A QUIC connection to port 443 cannot serve requests to port 8443.
        if (!string.Equals(connectionScheme, targetScheme, StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: Scheme mismatch — connection reuse not permitted.");
        }

        if (connectionPort != targetPort)
        {
            return ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: Port mismatch — connection reuse not permitted.");
        }

        if (!CoversHostname(serverCertificate, targetHost))
        {
            return ConnectionReuseDecision.NewConnection(
                "RFC 9114 §3.3: Server certificate does not cover target hostname; new connection required.");
        }

        return ConnectionReuseDecision.Reuse(
            $"RFC 9114 §3.3: Certificate covers '{targetHost}' — cross-origin connection reuse permitted.");
    }

    private static bool IsSameOrigin(
        string scheme1, string host1, int port1,
        string scheme2, string host2, int port2) =>
        string.Equals(scheme1, scheme2, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(host1, host2, StringComparison.OrdinalIgnoreCase) &&
        port1 == port2;

    /// <summary>
    /// Returns true if the certificate covers the given hostname via SAN dNSName
    /// entries or CN fallback, per RFC 6125 §6.4.
    /// </summary>
    internal static bool CoversHostname(X509Certificate2 certificate, string hostname)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        var sanNames = GetSubjectAlternativeNames(certificate);
        if (sanNames.Count > 0)
        {
            foreach (var san in sanNames)
            {
                if (MatchesHostname(san, hostname))
                {
                    return true;
                }
            }

            // RFC 6125 §6.4.4: If SANs exist, CN MUST NOT be used.
            return false;
        }

        var cn = GetCommonName(certificate);
        return cn is not null && MatchesHostname(cn, hostname);
    }

    private static List<string> GetSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var result = new List<string>();

        foreach (var ext in certificate.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            var formatted = ext.Format(multiLine: true);
            foreach (var line in formatted.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var token in line.Split([", "], StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = token.Trim();

                    if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = trimmed["DNS Name=".Length..].Trim();
                        if (value.Length > 0)
                        {
                            result.Add(value);
                        }
                    }
                    else if (trimmed.StartsWith("DNS:", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = trimmed["DNS:".Length..].Trim();
                        if (value.Length > 0)
                        {
                            result.Add(value);
                        }
                    }
                }
            }
        }

        return result;
    }

    private static string? GetCommonName(X509Certificate2 certificate)
    {
        var subject = certificate.Subject;
        const string cnPrefix = "CN=";
        var idx = subject.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + cnPrefix.Length;
        var end = subject.IndexOf(',', start);
        var cn = end < 0
            ? subject[start..]
            : subject[start..end];

        return cn.Trim();
    }

    internal static bool MatchesHostname(string certName, string hostname)
    {
        if (string.Equals(certName, hostname, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (certName.StartsWith("*.", StringComparison.Ordinal) && certName.Length > 2)
        {
            var wildcardSuffix = certName[1..];

            var dotIndex = hostname.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex > 0)
            {
                var hostSuffix = hostname[dotIndex..];
                if (string.Equals(hostSuffix, wildcardSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

/// <summary>
/// Result of evaluating whether an HTTP/3 connection can be reused.
/// </summary>
internal sealed record ConnectionReuseDecision
{
    /// <summary>Whether the existing connection can be reused for the new request.</summary>
    public bool CanReuse { get; private init; }

    /// <summary>Human-readable reason for the decision (diagnostics/logging).</summary>
    public string Reason { get; private init; } = string.Empty;

    /// <summary>Creates a reuse-allowed decision.</summary>
    public static ConnectionReuseDecision Reuse(string reason) =>
        new() { CanReuse = true, Reason = reason };

    /// <summary>Creates a new-connection-required decision.</summary>
    public static ConnectionReuseDecision NewConnection(string reason) =>
        new() { CanReuse = false, Reason = reason };
}
