using System.Security.Cryptography.X509Certificates;

namespace TurboHttp.Protocol.Http3;

/// <summary>
/// RFC 9114 §3.3 — Validates that a server's TLS certificate covers a given hostname.
/// Used to determine whether an HTTP/3 connection can be coalesced across origins.
///
/// Certificate hostname matching follows RFC 6125 §6.4:
/// - Exact match of Subject Alternative Name (SAN) dNSName entries
/// - Single-level wildcard matching (e.g. *.example.com matches foo.example.com)
/// - Common Name (CN) fallback only when no SAN dNSName entries exist
/// </summary>
public static class Http3CertificateValidator
{
    /// <summary>
    /// Returns true if the certificate covers the given hostname via SAN dNSName
    /// entries or CN fallback, per RFC 6125 §6.4.
    /// </summary>
    /// <param name="certificate">The server's TLS certificate.</param>
    /// <param name="hostname">The target hostname to check.</param>
    /// <returns>True if the certificate covers the hostname.</returns>
    public static bool CoversHostname(X509Certificate2 certificate, string hostname)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        // Try Subject Alternative Names first (preferred per RFC 6125 §6.4.4).
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

        // Fallback: check CN when no SAN dNSName entries exist.
        var cn = GetCommonName(certificate);
        return cn is not null && MatchesHostname(cn, hostname);
    }

    /// <summary>
    /// Extracts dNSName entries from the Subject Alternative Name extension.
    /// OID 2.5.29.17 is the SAN extension.
    /// </summary>
    internal static List<string> GetSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var result = new List<string>();

        foreach (var ext in certificate.Extensions)
        {
            if (ext.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            // Format() returns a human-readable representation that includes
            // "DNS Name=<value>" entries on most platforms.
            // On Linux, multiple SANs may appear comma-separated on a single line:
            // "DNS:a.com, DNS:b.com" — so we split on newlines first, then on ", ".
            var formatted = ext.Format(multiLine: true);
            foreach (var line in formatted.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var token in line.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = token.Trim();

                    // Windows format: "DNS Name=example.com"
                    if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = trimmed["DNS Name=".Length..].Trim();
                        if (value.Length > 0)
                        {
                            result.Add(value);
                        }
                    }
                    // Linux/macOS format: "DNS:example.com"
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

    /// <summary>
    /// Extracts the Common Name (CN) from the certificate's Subject field.
    /// </summary>
    internal static string? GetCommonName(X509Certificate2 certificate)
    {
        var subject = certificate.Subject;
        // Look for CN= in the subject string
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

    /// <summary>
    /// Matches a certificate name (exact or wildcard) against a hostname.
    /// RFC 6125 §6.4.3: wildcard must be the leftmost label only.
    /// </summary>
    internal static bool MatchesHostname(string certName, string hostname)
    {
        // Exact match (case-insensitive per DNS rules).
        if (string.Equals(certName, hostname, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Wildcard matching: *.example.com matches foo.example.com
        // but NOT bar.foo.example.com (single level only).
        if (certName.StartsWith("*.", StringComparison.Ordinal) && certName.Length > 2)
        {
            var wildcardSuffix = certName[1..]; // ".example.com"

            // The hostname must have exactly one label before the wildcard suffix.
            var dotIndex = hostname.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex > 0)
            {
                var hostSuffix = hostname[dotIndex..]; // ".example.com"
                if (string.Equals(hostSuffix, wildcardSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
