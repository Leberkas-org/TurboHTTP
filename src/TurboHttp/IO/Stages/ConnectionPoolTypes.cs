using System;
using System.Buffers;

namespace TurboHttp.IO.Stages;

public sealed record RoutedTransportItem(string PoolKey, ITransportItem Item);

public sealed record RoutedDataItem(string PoolKey, IMemoryOwner<byte> Memory, int Length);

/// <summary>
/// Strategy for distributing requests across active connections for a host.
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// Selects the connection with the fewest pending requests.
    /// Idle connections are always preferred.
    /// </summary>
    LeastLoaded,

    /// <summary>
    /// Cycles through all active connections in order.
    /// Idle connections are always preferred.
    /// </summary>
    RoundRobin
}

public sealed record PoolConfig(
    int MaxConnectionsPerHost = 10,
    TimeSpan IdleTimeout = default,
    TimeSpan ConnectionTimeout = default,
    LoadBalancingStrategy Strategy = LoadBalancingStrategy.LeastLoaded,
    int MaxReconnectAttempts = 3,
    TimeSpan ReconnectInterval = default)
{
    public TimeSpan IdleTimeout { get; init; } =
        IdleTimeout == TimeSpan.Zero ? TimeSpan.FromMinutes(5) : IdleTimeout;

    public TimeSpan ConnectionTimeout { get; init; } =
        ConnectionTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : ConnectionTimeout;

    public TimeSpan ReconnectInterval { get; init; } =
        ReconnectInterval == TimeSpan.Zero ? TimeSpan.FromSeconds(5) : ReconnectInterval;
}

/// <summary>
/// Thrown when the connection pool exhausts reconnect attempts for a host.
/// </summary>
public sealed class ConnectionPoolException : Exception
{
    public ConnectionPoolException(string message) : base(message) { }
    public ConnectionPoolException(string message, Exception innerException) : base(message, innerException) { }
}
