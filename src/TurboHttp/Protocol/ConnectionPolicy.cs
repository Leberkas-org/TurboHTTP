namespace TurboHttp.Protocol;

/// <summary>
/// Configuration for HTTP connection management behavior.
/// RFC 9112 §9 — Persistent Connections.
/// </summary>
public sealed record ConnectionPolicy
{
    /// <summary>Default policy: max 6 connections per host, HTTP/2 multiplexing enabled.</summary>
    public static readonly ConnectionPolicy Default = new();

    /// <summary>
    /// Maximum number of simultaneous connections per host (applies to HTTP/1.x only).
    /// HTTP/2 connections are multiplexed and not subject to this limit.
    /// Default is 6 (aligned with browser and RFC conventions).
    /// </summary>
    public int MaxConnectionsPerHost { get; init; } = 6;

    /// <summary>
    /// If true, HTTP/2 connections are treated as multiplexed streams and
    /// the per-host connection limiter is not applied to HTTP/2 connections.
    /// Default is true.
    /// </summary>
    public bool AllowHttp2Multiplexing { get; init; } = true;
}
