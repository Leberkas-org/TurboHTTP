using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9111 §5.2 — Parsed representation of the Cache-Control header.
/// Covers both request and response directives.
/// </summary>
public sealed record CacheControl
{
    // ── Request + Response directives ─────────────────────────────────────────

    /// <summary>RFC 9111 §5.2.1.4 / §5.2.2.4 — no-cache directive.</summary>
    public bool NoCache { get; init; }

    /// <summary>RFC 9111 §5.2.1.5 / §5.2.2.5 — no-store directive.</summary>
    public bool NoStore { get; init; }

    /// <summary>RFC 9111 §5.2.1.6 / §5.2.2.6 — no-transform directive.</summary>
    public bool NoTransform { get; init; }

    /// <summary>RFC 9111 §5.2.1.1 / §5.2.2.1 — max-age value in seconds.</summary>
    public TimeSpan? MaxAge { get; init; }

    // ── Request-only directives ───────────────────────────────────────────────

    /// <summary>RFC 9111 §5.2.1.2 — max-stale value in seconds (request only).</summary>
    public TimeSpan? MaxStale { get; init; }

    /// <summary>RFC 9111 §5.2.1.3 — min-fresh value in seconds (request only).</summary>
    public TimeSpan? MinFresh { get; init; }

    /// <summary>RFC 9111 §5.2.1.7 — only-if-cached directive (request only).</summary>
    public bool OnlyIfCached { get; init; }

    // ── Response-only directives ──────────────────────────────────────────────

    /// <summary>RFC 9111 §5.2.2.10 — s-maxage value in seconds (response, shared cache only).</summary>
    public TimeSpan? SMaxAge { get; init; }

    /// <summary>RFC 9111 §5.2.2.8 — must-revalidate directive (response only).</summary>
    public bool MustRevalidate { get; init; }

    /// <summary>RFC 9111 §5.2.2.9 — proxy-revalidate directive (response only).</summary>
    public bool ProxyRevalidate { get; init; }

    /// <summary>RFC 9111 §5.2.2.5 — public directive (response only).</summary>
    public bool Public { get; init; }

    /// <summary>RFC 9111 §5.2.2.6 — private directive (response only).</summary>
    public bool Private { get; init; }

    /// <summary>RFC 8246 — immutable directive (response only).</summary>
    public bool Immutable { get; init; }

    // ── Field-list variants ───────────────────────────────────────────────────

    /// <summary>
    /// RFC 9111 §5.2.2.4 — field names listed in no-cache="field1, field2".
    /// When non-null, only those fields must be revalidated; others may be served from cache.
    /// </summary>
    public IReadOnlyList<string>? NoCacheFields { get; init; }

    /// <summary>
    /// RFC 9111 §5.2.2.6 — field names listed in private="field1, field2".
    /// When non-null, only those fields are private; the rest may be stored by shared caches.
    /// </summary>
    public IReadOnlyList<string>? PrivateFields { get; init; }
}
