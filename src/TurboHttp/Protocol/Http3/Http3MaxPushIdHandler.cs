namespace TurboHttp.Protocol.Http3;

// HTTP/3 MAX_PUSH_ID Management  —  RFC 9114 §7.2.7
//
// A client sends MAX_PUSH_ID on the control stream to indicate the maximum
// push ID the server is permitted to use.  Until a MAX_PUSH_ID frame is
// received, the server MUST NOT use server push (the limit is effectively -1).
//
// Key rules:
//   - MAX_PUSH_ID is always sent by the client, never the server.
//   - The value MUST NOT decrease across frames; a decrease is a
//     connection error of type H3_ID_ERROR (RFC 9114 §7.2.7).
//   - The server MUST NOT use a push ID greater than the current limit;
//     doing so is a connection error of type H3_ID_ERROR.
//   - A MAX_PUSH_ID frame received on any stream other than the control
//     stream is a connection error of type H3_FRAME_UNEXPECTED.

/// <summary>
/// Tracks the MAX_PUSH_ID state for an HTTP/3 connection per RFC 9114 §7.2.7.
/// Handles sending MAX_PUSH_ID from the client and validating incoming push IDs
/// from the server against the current limit.
/// </summary>
public sealed class Http3MaxPushIdHandler
{
    /// <summary>
    /// The current MAX_PUSH_ID value sent by the client, or -1 if none has been sent.
    /// The server MUST NOT use any push ID greater than this value.
    /// </summary>
    public long CurrentMaxPushId { get; private set; } = -1;

    /// <summary>
    /// Whether a MAX_PUSH_ID frame has been sent by the client.
    /// Until this is true, the server MUST NOT use server push.
    /// </summary>
    public bool HasSentMaxPushId => CurrentMaxPushId >= 0;

    /// <summary>
    /// Creates a MAX_PUSH_ID frame to send on the client control stream.
    /// The value MUST NOT decrease compared to a previously sent MAX_PUSH_ID.
    /// </summary>
    /// <param name="pushId">The maximum push ID the server is permitted to use.</param>
    /// <returns>A MAX_PUSH_ID frame ready to send on the control stream.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="pushId"/> is negative.
    /// </exception>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if <paramref name="pushId"/>
    /// is less than the previously sent value (RFC 9114 §7.2.7).
    /// </exception>
    public Http3MaxPushIdFrame CreateMaxPushId(long pushId)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        // RFC 9114 §7.2.7: The maximum push ID MUST NOT be reduced.
        if (CurrentMaxPushId >= 0 && pushId < CurrentMaxPushId)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"MAX_PUSH_ID {pushId} must not decrease below previous value {CurrentMaxPushId} (RFC 9114 §7.2.7).");
        }

        CurrentMaxPushId = pushId;
        return new Http3MaxPushIdFrame(pushId);
    }

    /// <summary>
    /// Validates that a push ID from the server (e.g. in a PUSH_PROMISE frame)
    /// does not exceed the current MAX_PUSH_ID limit.
    /// </summary>
    /// <param name="pushId">The push ID used by the server.</param>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if no MAX_PUSH_ID has
    /// been sent (server push is not permitted) or if <paramref name="pushId"/>
    /// exceeds the current limit (RFC 9114 §7.2.7).
    /// </exception>
    public void ValidatePushId(long pushId)
    {
        if (!HasSentMaxPushId)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"Server used push ID {pushId} but no MAX_PUSH_ID has been sent; server push is not permitted (RFC 9114 §7.2.7).");
        }

        if (pushId > CurrentMaxPushId)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"Server push ID {pushId} exceeds MAX_PUSH_ID {CurrentMaxPushId} (RFC 9114 §7.2.7).");
        }
    }

    /// <summary>
    /// Returns whether the given push ID is within the current limit.
    /// Returns false if no MAX_PUSH_ID has been sent or if the push ID exceeds the limit.
    /// </summary>
    /// <param name="pushId">The push ID to check.</param>
    /// <returns><c>true</c> if the push ID is permitted; <c>false</c> otherwise.</returns>
    public bool IsPushIdAllowed(long pushId)
    {
        if (!HasSentMaxPushId)
        {
            return false;
        }

        return pushId >= 0 && pushId <= CurrentMaxPushId;
    }
}
