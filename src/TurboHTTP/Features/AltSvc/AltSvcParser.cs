namespace TurboHTTP.Features.AltSvc;

/// <summary>
/// Parses Alt-Svc header values per RFC 7838 §3.
/// <para>Format: <c>clear</c> or <c>protocol="host:port"; ma=maxAge; persist=1, ...</c></para>
/// <para>Examples:</para>
/// <list type="bullet">
///   <item><c>h3=":443"; ma=86400</c> — HTTP/3 on same host, port 443, 24h TTL</item>
///   <item><c>h3=":443", h2=":443"</c> — multiple alternatives</item>
///   <item><c>h3-29=":443"; ma=3600; persist=1</c> — draft h3 with persist flag</item>
///   <item><c>clear</c> — invalidate all cached alternatives for this origin</item>
/// </list>
/// </summary>
internal static class AltSvcParser
{
    /// <summary>
    /// Parses an Alt-Svc header value into a list of entries.
    /// Returns an empty list for "clear" or unparseable input.
    /// </summary>
    /// <param name="headerValue">The raw Alt-Svc header value.</param>
    /// <param name="isClear">Set to true if the header value is "clear".</param>
    /// <param name="now">Current time for computing expiration. If null, uses DateTimeOffset.UtcNow.</param>
    /// <returns>Parsed Alt-Svc entries.</returns>
    internal static List<AltSvcEntry> Parse(string headerValue, out bool isClear, DateTimeOffset? now = null)
    {
        isClear = false;
        var entries = new List<AltSvcEntry>();

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return entries;
        }

        var trimmed = headerValue.Trim();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            isClear = true;
            return entries;
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;

        // Split by comma to get individual alternatives.
        // Each alternative has format: protocol="host:port" [; param=value]*
        var alternatives = trimmed.Split(',');
        foreach (var alt in alternatives)
        {
            var entry = ParseAlternative(alt.Trim(), currentTime);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static AltSvcEntry? ParseAlternative(string alternative, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(alternative))
        {
            return null;
        }

        // Split into protocol="authority" and parameters by semicolons.
        var parts = alternative.Split(';');
        if (parts.Length == 0)
        {
            return null;
        }

        // Parse protocol="authority" from the first part.
        var protocolAuthority = parts[0].Trim();
        var eqIndex = protocolAuthority.IndexOf('=');
        if (eqIndex <= 0 || eqIndex >= protocolAuthority.Length - 1)
        {
            return null;
        }

        var protocol = protocolAuthority[..eqIndex].Trim();
        var authorityRaw = protocolAuthority[(eqIndex + 1)..].Trim();

        // Strip quotes from authority.
        if (authorityRaw is ['"', _, ..] && authorityRaw[^1] == '"')
        {
            authorityRaw = authorityRaw[1..^1];
        }

        // Parse host:port from authority.
        if (!TryParseAuthority(authorityRaw, out var host, out var port))
        {
            return null;
        }

        // Parse optional parameters: ma= and persist=
        var maxAge = 86400; // RFC 7838 §3.1 default
        var persist = false;

        for (var i = 1; i < parts.Length; i++)
        {
            var param = parts[i].Trim();
            if (param.StartsWith("ma=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(param.AsSpan(3), out var ma) && ma >= 0)
                {
                    maxAge = ma;
                }
            }
            else if (param.StartsWith("persist=", StringComparison.OrdinalIgnoreCase))
            {
                persist = param.AsSpan(8).Trim() is "1";
            }
        }

        var expiresAt = now.AddSeconds(maxAge);

        return new AltSvcEntry(
            Protocol: protocol,
            Host: host,
            Port: port,
            MaxAge: maxAge,
            Persist: persist,
            ExpiresAt: expiresAt);
    }

    /// <summary>
    /// Parses "host:port" or ":port" authority format.
    /// Empty host means same-origin (RFC 7838 §3).
    /// </summary>
    private static bool TryParseAuthority(string authority, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrEmpty(authority))
        {
            return false;
        }

        // Find the last colon to split host and port.
        var colonIndex = authority.LastIndexOf(':');
        if (colonIndex < 0)
        {
            return false;
        }

        host = authority[..colonIndex];
        var portSpan = authority.AsSpan(colonIndex + 1);

        return int.TryParse(portSpan, out port) && port is > 0 and <= 65535;
    }
}