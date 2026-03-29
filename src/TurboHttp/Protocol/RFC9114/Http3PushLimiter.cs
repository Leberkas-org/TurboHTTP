namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 DoS Prevention — Push Limiting  —  RFC 9114 §10.5
//
// A client SHOULD limit the number of server push promises it is willing to
// accept.  A malicious server could otherwise flood the client with push
// promises, consuming memory and CPU.  This class enforces a configurable
// ceiling on the total number of push promises accepted on a connection.
//
// When the limit is exceeded the client closes the connection with
// H3_EXCESSIVE_LOAD (0x107), signalling that the server is sending too
// much unsolicited data.
//
// Integration:
//   - Wrap or compose with Http3PushPromiseValidator.
//   - Call RecordPush() for every validated PUSH_PROMISE frame.
//   - The MAX_PUSH_ID value sent by the client should be ≤ MaxPushCount
//     to make the protocol-level and DoS-level limits consistent.

/// <summary>
/// Enforces a configurable ceiling on the number of server push promises
/// accepted per HTTP/3 connection, per RFC 9114 §10.5 (DoS prevention).
/// </summary>
public sealed class Http3PushLimiter
{
    /// <summary>
    /// Default maximum number of push promises a client will accept per connection.
    /// </summary>
    public const int DefaultMaxPushCount = 100;

    /// <summary>
    /// Creates a push limiter with the specified maximum push count.
    /// </summary>
    /// <param name="maxPushCount">
    /// Maximum number of push promises accepted before triggering a connection error.
    /// Must be non-negative. Zero means no pushes are accepted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="maxPushCount"/> is negative.
    /// </exception>
    public Http3PushLimiter(int maxPushCount = DefaultMaxPushCount)
    {
        if (maxPushCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPushCount), maxPushCount,
                "Maximum push count must be non-negative.");
        }

        MaxPushCount = maxPushCount;
    }

    /// <summary>
    /// The configured maximum number of push promises for this connection.
    /// </summary>
    public int MaxPushCount { get; }

    /// <summary>
    /// The number of push promises recorded so far.
    /// </summary>
    public int PushCount { get; private set; }

    /// <summary>
    /// The number of remaining push promises before the limit is reached.
    /// </summary>
    public int Remaining => MaxPushCount - PushCount;

    /// <summary>
    /// Whether the push limit has been reached (no more pushes will be accepted).
    /// </summary>
    public bool IsExhausted => PushCount >= MaxPushCount;

    /// <summary>
    /// Records an incoming push promise and enforces the limit.
    /// Call this for every validated PUSH_PROMISE frame.
    /// </summary>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.ExcessiveLoad"/> if the push count
    /// would exceed the configured maximum (RFC 9114 §10.5).
    /// </exception>
    public void RecordPush()
    {
        if (PushCount >= MaxPushCount)
        {
            throw new Http3Exception(
                Http3ErrorCode.ExcessiveLoad,
                $"Server exceeded push limit of {MaxPushCount} push promises (RFC 9114 §10.5).");
        }

        PushCount++;
    }

    /// <summary>
    /// Returns the recommended MAX_PUSH_ID value to send to the server.
    /// This is <c>MaxPushCount - 1</c> (since push IDs are zero-based),
    /// or -1 if MaxPushCount is 0 (no pushes allowed).
    /// </summary>
    public long RecommendedMaxPushId => MaxPushCount > 0 ? MaxPushCount - 1 : -1;
}
