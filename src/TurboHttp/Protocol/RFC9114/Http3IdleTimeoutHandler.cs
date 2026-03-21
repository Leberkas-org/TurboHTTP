using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Idle Timeout + Reconnection  —  RFC 9114 §5.1
//
// HTTP/3 uses QUIC, which provides built-in idle timeout at the transport
// layer. When no streams are open and no frames have been sent or received
// for the idle timeout duration, the QUIC connection is closed.
//
// Key rules (RFC 9114 §5.1):
//   - Clients SHOULD NOT open an HTTP/3 connection if idle timeout is 0.
//   - The effective idle timeout is the minimum of the two endpoints' values.
//   - To keep a connection alive, send a PING frame (at transport layer)
//     before the idle timeout expires.
//   - Clients should track when the last activity occurred and proactively
//     close connections that have been idle too long.
//
// Reconnection:
//   - After connection loss (idle timeout, transport error, GOAWAY),
//     the client may attempt to reconnect.
//   - Retryable requests (per GOAWAY) should be retried on the new connection.
//   - Exponential backoff should be used for reconnection attempts.

/// <summary>
/// Tracks idle timeout state for an HTTP/3 connection per RFC 9114 §5.1.
/// Determines when a connection should be closed due to inactivity and
/// manages reconnection decisions after connection loss.
/// </summary>
public sealed class Http3IdleTimeoutHandler
{
    private readonly TimeSpan _idleTimeout;
    private DateTime _lastActivity;
    private int _activeStreamCount;

    /// <summary>
    /// Creates a new idle timeout handler with the specified timeout.
    /// </summary>
    /// <param name="idleTimeout">
    /// The idle timeout duration. Use <see cref="TimeSpan.Zero"/> to disable idle timeout.
    /// </param>
    /// <param name="utcNow">
    /// The current UTC time. If null, uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="idleTimeout"/> is negative.
    /// </exception>
    public Http3IdleTimeoutHandler(TimeSpan idleTimeout, DateTime? utcNow = null)
    {
        if (idleTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(idleTimeout), idleTimeout,
                "Idle timeout must be non-negative.");
        }

        _idleTimeout = idleTimeout;
        _lastActivity = utcNow ?? DateTime.UtcNow;
    }

    /// <summary>
    /// The configured idle timeout duration.
    /// </summary>
    public TimeSpan IdleTimeout => _idleTimeout;

    /// <summary>
    /// The UTC time of the last recorded activity on this connection.
    /// </summary>
    public DateTime LastActivity => _lastActivity;

    /// <summary>
    /// The number of currently active streams on this connection.
    /// </summary>
    public int ActiveStreamCount => _activeStreamCount;

    /// <summary>
    /// Whether idle timeout is disabled (timeout is <see cref="TimeSpan.Zero"/>).
    /// RFC 9114 §5.1: A value of 0 means idle timeout is disabled.
    /// </summary>
    public bool IsTimeoutDisabled => _idleTimeout == TimeSpan.Zero;

    /// <summary>
    /// Records activity on this connection, resetting the idle timer.
    /// Call this when any frame is sent or received, or when a stream is opened/closed.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC time. If null, uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    public void RecordActivity(DateTime? utcNow = null)
    {
        _lastActivity = utcNow ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Records that a new stream has been opened on this connection.
    /// Resets the idle timer.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC time. If null, uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    public void OnStreamOpened(DateTime? utcNow = null)
    {
        _activeStreamCount++;
        RecordActivity(utcNow);
    }

    /// <summary>
    /// Records that a stream has been closed on this connection.
    /// Resets the idle timer.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC time. If null, uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if there are no active streams to close.
    /// </exception>
    public void OnStreamClosed(DateTime? utcNow = null)
    {
        if (_activeStreamCount <= 0)
        {
            throw new InvalidOperationException(
                "Cannot close a stream when no streams are active.");
        }

        _activeStreamCount--;
        RecordActivity(utcNow);
    }

    /// <summary>
    /// Determines whether the connection has been idle for longer than the
    /// configured timeout.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC time. If null, uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the connection has exceeded the idle timeout and should be closed;
    /// <c>false</c> if the connection is still within the timeout or timeout is disabled.
    /// </returns>
    public bool IsIdleTimeoutExpired(DateTime? utcNow = null)
    {
        if (IsTimeoutDisabled)
        {
            return false;
        }

        var now = utcNow ?? DateTime.UtcNow;
        return (now - _lastActivity) >= _idleTimeout;
    }

    /// <summary>
    /// Computes the effective idle timeout as the minimum of the local and
    /// remote idle timeout values (RFC 9114 §5.1). A value of zero on either
    /// side means that side has no idle timeout preference.
    /// </summary>
    /// <param name="localTimeout">The client's idle timeout.</param>
    /// <param name="remoteTimeout">The server's idle timeout (from QUIC transport parameters).</param>
    /// <returns>The effective idle timeout to use for the connection.</returns>
    public static TimeSpan ComputeEffectiveTimeout(TimeSpan localTimeout, TimeSpan remoteTimeout)
    {
        if (localTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(localTimeout), localTimeout, "Timeout must be non-negative.");
        }

        if (remoteTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteTimeout), remoteTimeout, "Timeout must be non-negative.");
        }

        // Zero means "no preference" — use the other side's value
        if (localTimeout == TimeSpan.Zero)
        {
            return remoteTimeout;
        }

        if (remoteTimeout == TimeSpan.Zero)
        {
            return localTimeout;
        }

        return localTimeout < remoteTimeout ? localTimeout : remoteTimeout;
    }

    /// <summary>
    /// Returns the time remaining before the idle timeout expires.
    /// </summary>
    /// <param name="utcNow">
    /// The current UTC time. If null, uses <see cref="DateTime.UtcNow"/>.
    /// </param>
    /// <returns>
    /// The remaining time before timeout, or <see cref="TimeSpan.Zero"/> if already expired.
    /// Returns <see cref="TimeSpan.MaxValue"/> if timeout is disabled.
    /// </returns>
    public TimeSpan TimeUntilExpiry(DateTime? utcNow = null)
    {
        if (IsTimeoutDisabled)
        {
            return TimeSpan.MaxValue;
        }

        var now = utcNow ?? DateTime.UtcNow;
        var elapsed = now - _lastActivity;
        var remaining = _idleTimeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}

/// <summary>
/// Manages reconnection decisions and exponential backoff after HTTP/3
/// connection loss per RFC 9114 §5.1 and general transport best practices.
/// </summary>
public sealed class Http3ReconnectionPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private int _attempt;

    /// <summary>
    /// Creates a reconnection policy with configurable backoff parameters.
    /// </summary>
    /// <param name="maxAttempts">Maximum number of reconnection attempts before giving up.</param>
    /// <param name="baseDelay">The initial delay between reconnection attempts.</param>
    /// <param name="maxDelay">The maximum delay between reconnection attempts.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="maxAttempts"/> is negative, or delays are negative.
    /// </exception>
    public Http3ReconnectionPolicy(
        int maxAttempts = 5,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null)
    {
        if (maxAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts), maxAttempts,
                "Maximum attempts must be non-negative.");
        }

        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);

        if (_baseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseDelay), baseDelay,
                "Base delay must be non-negative.");
        }

        if (_maxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDelay), maxDelay,
                "Max delay must be non-negative.");
        }

        _maxAttempts = maxAttempts;
    }

    /// <summary>The maximum number of reconnection attempts.</summary>
    public int MaxAttempts => _maxAttempts;

    /// <summary>The current reconnection attempt number (0-based).</summary>
    public int CurrentAttempt => _attempt;

    /// <summary>The base delay between reconnection attempts.</summary>
    public TimeSpan BaseDelay => _baseDelay;

    /// <summary>The maximum delay between reconnection attempts.</summary>
    public TimeSpan MaxDelay => _maxDelay;

    /// <summary>
    /// Whether the maximum number of reconnection attempts has been exhausted.
    /// </summary>
    public bool IsExhausted => _attempt >= _maxAttempts;

    /// <summary>
    /// Evaluates whether a reconnection should be attempted and returns
    /// the result with the appropriate delay.
    /// </summary>
    /// <param name="reason">The reason for the connection loss.</param>
    /// <returns>
    /// A <see cref="Http3ReconnectionDecision"/> indicating whether to reconnect
    /// and, if so, the delay before attempting.
    /// </returns>
    public Http3ReconnectionDecision ShouldReconnect(Http3ConnectionLossReason reason)
    {
        // Fatal errors should not be retried
        if (reason == Http3ConnectionLossReason.ProtocolError)
        {
            return Http3ReconnectionDecision.DoNotReconnect(
                "Protocol error — reconnection would likely fail with the same error.");
        }

        if (IsExhausted)
        {
            return Http3ReconnectionDecision.DoNotReconnect(
                $"Maximum reconnection attempts ({_maxAttempts}) exhausted.");
        }

        var delay = ComputeDelay(_attempt);
        _attempt++;

        return Http3ReconnectionDecision.Reconnect(delay,
            $"Reconnection attempt {_attempt}/{_maxAttempts} after {reason}, delay {delay.TotalMilliseconds:F0}ms.");
    }

    /// <summary>
    /// Resets the reconnection attempt counter. Call this after a successful
    /// connection is established.
    /// </summary>
    public void Reset()
    {
        _attempt = 0;
    }

    /// <summary>
    /// Computes the backoff delay for the given attempt using exponential backoff
    /// with a cap at <see cref="MaxDelay"/>.
    /// </summary>
    internal TimeSpan ComputeDelay(int attempt)
    {
        // 2^attempt * baseDelay, capped at maxDelay
        var multiplier = Math.Pow(2, attempt);
        var delayMs = _baseDelay.TotalMilliseconds * multiplier;
        var cappedMs = Math.Min(delayMs, _maxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(cappedMs);
    }
}

/// <summary>
/// Reasons for HTTP/3 connection loss.
/// </summary>
public enum Http3ConnectionLossReason
{
    /// <summary>Connection was idle for longer than the configured timeout.</summary>
    IdleTimeout,

    /// <summary>The server sent a GOAWAY frame requesting graceful shutdown.</summary>
    GoAway,

    /// <summary>The underlying QUIC transport encountered an error.</summary>
    TransportError,

    /// <summary>An HTTP/3 protocol error occurred (e.g., invalid frame, stream error).</summary>
    ProtocolError,

    /// <summary>The connection was closed by the server without GOAWAY.</summary>
    ServerClosed,
}

/// <summary>
/// Result of evaluating whether to attempt reconnection.
/// </summary>
public sealed record Http3ReconnectionDecision
{
    /// <summary>Whether reconnection should be attempted.</summary>
    public bool ShouldAttempt { get; private init; }

    /// <summary>The delay before attempting reconnection.</summary>
    public TimeSpan Delay { get; private init; }

    /// <summary>Human-readable reason for the decision.</summary>
    public string Reason { get; private init; } = string.Empty;

    /// <summary>Creates a reconnect decision with the specified delay.</summary>
    public static Http3ReconnectionDecision Reconnect(TimeSpan delay, string reason) =>
        new() { ShouldAttempt = true, Delay = delay, Reason = reason };

    /// <summary>Creates a do-not-reconnect decision.</summary>
    public static Http3ReconnectionDecision DoNotReconnect(string reason) =>
        new() { ShouldAttempt = false, Delay = TimeSpan.Zero, Reason = reason };
}
