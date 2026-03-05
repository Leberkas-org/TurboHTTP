using System;
using System.Collections.Generic;
using System.Net.Http;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §3 — A stored response entry in the HTTP cache.
/// Captures all metadata needed to evaluate freshness and perform conditional requests.
/// </summary>
public sealed class CacheEntry
{
    /// <summary>The original HTTP response message (headers, status, version).</summary>
    public required HttpResponseMessage Response { get; init; }

    /// <summary>The fully-buffered response body bytes.</summary>
    public required byte[] Body { get; init; }

    /// <summary>The time at which the request was sent (local clock).</summary>
    public required DateTimeOffset RequestTime { get; init; }

    /// <summary>The time at which the response was received (local clock).</summary>
    public required DateTimeOffset ResponseTime { get; init; }

    /// <summary>Value of the ETag response header, if present.</summary>
    public string? ETag { get; init; }

    /// <summary>Parsed value of the Last-Modified response header, if present.</summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>Parsed value of the Expires response header, if present.</summary>
    public DateTimeOffset? Expires { get; init; }

    /// <summary>Parsed value of the Date response header, if present.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Parsed value of the Age response header (seconds), if present.</summary>
    public int? AgeSeconds { get; init; }

    /// <summary>Parsed Cache-Control directives from the response, if present.</summary>
    public CacheControl? CacheControl { get; init; }

    /// <summary>
    /// The header field names listed in the Vary response header.
    /// RFC 9111 §4.1 — used to select the correct variant for subsequent requests.
    /// </summary>
    public IReadOnlyList<string> VaryHeaderNames { get; init; } = [];

    /// <summary>
    /// The values of each Vary header field captured from the original request.
    /// RFC 9111 §4.1 — a new request must match these values to be a cache hit.
    /// </summary>
    public IReadOnlyDictionary<string, string?> VaryRequestValues { get; init; }
        = new Dictionary<string, string?>();
}
