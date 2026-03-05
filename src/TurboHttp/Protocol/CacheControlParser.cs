using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §5.2 — Parses the Cache-Control header value into a <see cref="CacheControl"/> record.
/// Unknown directives are silently ignored per RFC 9111 §5.2.
/// </summary>
public static class CacheControlParser
{
    /// <summary>
    /// Parses a Cache-Control header value string into a <see cref="CacheControl"/> record.
    /// Returns null for null or empty input.
    /// Unknown directives are silently ignored (RFC 9111 §5.2).
    /// </summary>
    public static CacheControl? Parse(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        var noCache = false;
        var noStore = false;
        var noTransform = false;
        var mustRevalidate = false;
        var proxyRevalidate = false;
        var isPublic = false;
        var isPrivate = false;
        var immutable = false;
        var onlyIfCached = false;
        TimeSpan? maxAge = null;
        TimeSpan? sMaxAge = null;
        TimeSpan? maxStale = null;
        TimeSpan? minFresh = null;
        List<string>? noCacheFields = null;
        List<string>? privateFields = null;

        var span = headerValue.AsSpan();

        while (!span.IsEmpty)
        {
            // Find the next comma delimiter
            var commaIdx = span.IndexOf(',');
            ReadOnlySpan<char> token;

            if (commaIdx < 0)
            {
                token = span;
                span = ReadOnlySpan<char>.Empty;
            }
            else
            {
                token = span[..commaIdx];
                span = span[(commaIdx + 1)..];
            }

            token = token.Trim();
            if (token.IsEmpty)
            {
                continue;
            }

            // Split directive name from optional =value
            var eqIdx = token.IndexOf('=');
            ReadOnlySpan<char> name;
            ReadOnlySpan<char> value;

            if (eqIdx < 0)
            {
                name = token;
                value = ReadOnlySpan<char>.Empty;
            }
            else
            {
                name = token[..eqIdx].Trim();
                value = token[(eqIdx + 1)..].Trim();
            }

            // Case-insensitive directive matching (RFC 9111 §5.2)
            if (name.Equals("no-cache", StringComparison.OrdinalIgnoreCase))
            {
                noCache = true;
                noCacheFields = ParseFieldList(value);
            }
            else if (name.Equals("no-store", StringComparison.OrdinalIgnoreCase))
            {
                noStore = true;
            }
            else if (name.Equals("no-transform", StringComparison.OrdinalIgnoreCase))
            {
                noTransform = true;
            }
            else if (name.Equals("must-revalidate", StringComparison.OrdinalIgnoreCase))
            {
                mustRevalidate = true;
            }
            else if (name.Equals("proxy-revalidate", StringComparison.OrdinalIgnoreCase))
            {
                proxyRevalidate = true;
            }
            else if (name.Equals("public", StringComparison.OrdinalIgnoreCase))
            {
                isPublic = true;
            }
            else if (name.Equals("private", StringComparison.OrdinalIgnoreCase))
            {
                isPrivate = true;
                privateFields = ParseFieldList(value);
            }
            else if (name.Equals("immutable", StringComparison.OrdinalIgnoreCase))
            {
                immutable = true;
            }
            else if (name.Equals("only-if-cached", StringComparison.OrdinalIgnoreCase))
            {
                onlyIfCached = true;
            }
            else if (name.Equals("max-age", StringComparison.OrdinalIgnoreCase))
            {
                maxAge = ParseSeconds(value);
            }
            else if (name.Equals("s-maxage", StringComparison.OrdinalIgnoreCase))
            {
                sMaxAge = ParseSeconds(value);
            }
            else if (name.Equals("max-stale", StringComparison.OrdinalIgnoreCase))
            {
                // max-stale with no value means "any stale age is acceptable"
                maxStale = value.IsEmpty ? TimeSpan.MaxValue : ParseSeconds(value);
            }
            else if (name.Equals("min-fresh", StringComparison.OrdinalIgnoreCase))
            {
                minFresh = ParseSeconds(value);
            }
            // Unknown directives are silently ignored per RFC 9111 §5.2
        }

        return new CacheControl
        {
            NoCache = noCache,
            NoStore = noStore,
            NoTransform = noTransform,
            MaxAge = maxAge,
            MaxStale = maxStale,
            MinFresh = minFresh,
            OnlyIfCached = onlyIfCached,
            SMaxAge = sMaxAge,
            MustRevalidate = mustRevalidate,
            ProxyRevalidate = proxyRevalidate,
            Public = isPublic,
            Private = isPrivate,
            Immutable = immutable,
            NoCacheFields = noCacheFields,
            PrivateFields = privateFields
        };
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private static TimeSpan? ParseSeconds(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return null;
        }

        // Strip surrounding quotes if present: "3600"
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (int.TryParse(value, out var seconds) && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    /// <summary>
    /// Parses an optional quoted field-list from a directive value.
    /// E.g. no-cache="Authorization, Cookie" → ["Authorization", "Cookie"]
    /// Returns null if value is empty or not a quoted string.
    /// </summary>
    private static List<string>? ParseFieldList(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return null;
        }

        // Strip surrounding quotes
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (value.IsEmpty)
        {
            return null;
        }

        var fields = new List<string>();
        var str = value.ToString();

        foreach (var part in str.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                fields.Add(trimmed);
            }
        }

        return fields.Count > 0 ? fields : null;
    }
}
