using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 CANCEL_PUSH Management  —  RFC 9114 §7.2.3
//
// A client sends CANCEL_PUSH on the control stream to indicate that it
// does not wish to receive a promised server push.  The server SHOULD
// stop sending the push stream, but is not required to.
//
// Key rules:
//   - CANCEL_PUSH references a push ID previously promised in a
//     PUSH_PROMISE frame, but receiving a CANCEL_PUSH for an unknown
//     push ID is NOT an error (§7.2.3).
//   - The push ID MUST be within the range allowed by MAX_PUSH_ID;
//     a push ID beyond the limit is a connection error of type
//     H3_ID_ERROR (§7.2.3).
//   - A client MAY send CANCEL_PUSH even if it has already received
//     the corresponding PUSH_PROMISE — it simply means the client is
//     no longer interested.
//   - Cancelling an already-cancelled push ID is idempotent.

/// <summary>
/// Manages CANCEL_PUSH frame creation for an HTTP/3 connection per RFC 9114 §7.2.3.
/// Tracks which push IDs the client has cancelled and validates push IDs against
/// the current MAX_PUSH_ID limit.
/// </summary>
public sealed class Http3CancelPushHandler
{
    private readonly Http3MaxPushIdHandler _maxPushIdHandler;
    private readonly HashSet<long> _cancelledPushIds = new();

    /// <summary>
    /// Creates a new CANCEL_PUSH handler that validates push IDs against the
    /// given <see cref="Http3MaxPushIdHandler"/>.
    /// </summary>
    public Http3CancelPushHandler(Http3MaxPushIdHandler maxPushIdHandler)
    {
        _maxPushIdHandler = maxPushIdHandler ?? throw new ArgumentNullException(nameof(maxPushIdHandler));
    }

    /// <summary>
    /// The number of push IDs that have been cancelled.
    /// </summary>
    public int CancelledCount => _cancelledPushIds.Count;

    /// <summary>
    /// Returns whether the given push ID has been cancelled.
    /// </summary>
    public bool IsCancelled(long pushId) => _cancelledPushIds.Contains(pushId);

    /// <summary>
    /// Creates a CANCEL_PUSH frame to send on the client control stream.
    /// The push ID does not need to correspond to a known PUSH_PROMISE —
    /// cancelling an unknown push ID is explicitly not an error (RFC 9114 §7.2.3).
    /// Cancelling an already-cancelled push ID is idempotent and returns a new frame.
    /// </summary>
    /// <param name="pushId">The push ID to cancel.</param>
    /// <returns>A CANCEL_PUSH frame ready to send on the control stream.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="pushId"/> is negative.
    /// </exception>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if <paramref name="pushId"/>
    /// exceeds the MAX_PUSH_ID limit (RFC 9114 §7.2.3).
    /// </exception>
    public Http3CancelPushFrame CancelPush(long pushId)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        // RFC 9114 §7.2.3: push ID must not exceed MAX_PUSH_ID limit
        if (_maxPushIdHandler.HasSentMaxPushId && pushId > _maxPushIdHandler.CurrentMaxPushId)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"CANCEL_PUSH push ID {pushId} exceeds MAX_PUSH_ID {_maxPushIdHandler.CurrentMaxPushId} (RFC 9114 §7.2.3).");
        }

        _cancelledPushIds.Add(pushId);
        return new Http3CancelPushFrame(pushId);
    }

    /// <summary>
    /// Processes an incoming CANCEL_PUSH frame received from the server.
    /// Per RFC 9114 §7.2.3, a CANCEL_PUSH for an unknown push ID is not an error.
    /// This method simply records the cancellation.
    /// </summary>
    /// <param name="frame">The CANCEL_PUSH frame received from the server.</param>
    public void HandleReceivedCancelPush(Http3CancelPushFrame frame)
    {
        if (frame is null)
        {
            throw new ArgumentNullException(nameof(frame));
        }

        // RFC 9114 §7.2.3: "If the client receives a CANCEL_PUSH frame for a
        // push ID that has not yet been mentioned in a PUSH_PROMISE frame, this
        // MUST NOT be treated as a connection error."
        _cancelledPushIds.Add(frame.PushId);
    }
}
