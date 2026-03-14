using System;

namespace TurboHttp.IO.Stages;

public sealed record PoolConfig(
    int MaxConnectionsPerHost = 10,
    TimeSpan IdleTimeout = default,
    TimeSpan ConnectionTimeout = default,
    int MaxReconnectAttempts = 3,
    TimeSpan ReconnectInterval = default,
    TimeSpan IdleCheckInterval = default,
    int PerHostQueueSize = 100,
    int MaxRequestsPerConnection = 1)
{
    public TimeSpan IdleTimeout { get; init; } =
        IdleTimeout == TimeSpan.Zero ? TimeSpan.FromMinutes(5) : IdleTimeout;

    public TimeSpan ConnectionTimeout { get; init; } =
        ConnectionTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : ConnectionTimeout;

    public TimeSpan ReconnectInterval { get; init; } =
        ReconnectInterval == TimeSpan.Zero ? TimeSpan.FromSeconds(5) : ReconnectInterval;

    /// <summary>
    /// How often the idle eviction timer fires to check for stale connections.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan IdleCheckInterval { get; init; } =
        IdleCheckInterval == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : IdleCheckInterval;
}