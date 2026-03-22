using System;
using System.Collections.Generic;

namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 GOAWAY Processing  —  RFC 9114 §5.2, §7.2.6
//
// GOAWAY enables graceful shutdown of a connection. It is sent on the
// control stream and carries a single QUIC variable-length integer:
//   - From server to client: the Stream ID of the last client-initiated
//     bidirectional stream the server will accept (divisible by 4).
//   - From client to server: the Push ID the client will accept.
//
// Key rules:
//   - The ID in GOAWAY MUST NOT increase on subsequent frames.
//   - Requests on streams with ID >= GOAWAY stream ID were NOT processed
//     and can safely be retried on a new connection.
//   - Both endpoints SHOULD send GOAWAY before closing a connection.
//   - A server MAY send multiple GOAWAYs, each with a lower stream ID,
//     narrowing the set of accepted streams.

/// <summary>
/// Tracks GOAWAY state for an HTTP/3 connection per RFC 9114 §5.2.
/// Handles both receiving GOAWAY from the server and sending GOAWAY
/// to the server for graceful client-initiated shutdown.
/// </summary>
public sealed class Http3GoAwayHandler
{
    private long _lastServerGoAwayStreamId = -1;
    private long _lastClientGoAwayPushId = -1;

    /// <summary>
    /// Whether the server has sent a GOAWAY frame on this connection.
    /// </summary>
    public bool IsGoingAway => _lastServerGoAwayStreamId >= 0;

    /// <summary>
    /// The last stream ID from the most recent server GOAWAY, or -1 if none received.
    /// Streams with ID &gt;= this value were NOT processed by the server.
    /// </summary>
    public long LastStreamId => _lastServerGoAwayStreamId;

    /// <summary>
    /// Whether the client has sent a GOAWAY frame on this connection.
    /// </summary>
    public bool ClientGoAwaySent => _lastClientGoAwayPushId >= 0;

    /// <summary>
    /// The push ID from the most recent client GOAWAY, or -1 if none sent.
    /// </summary>
    public long ClientGoAwayPushId => _lastClientGoAwayPushId;

    /// <summary>
    /// Processes a GOAWAY frame received from the server on the control stream.
    /// The frame carries the stream ID of the last request stream the server
    /// will process. All streams with ID &gt;= this value are considered
    /// unprocessed and eligible for retry on a new connection.
    /// </summary>
    /// <param name="frame">The GOAWAY frame received from the server.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="frame"/> is null.</exception>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if the stream ID
    /// increases compared to a previously received GOAWAY (RFC 9114 §5.2).
    /// </exception>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if the stream ID
    /// is not a valid client-initiated bidirectional stream ID (not divisible by 4).
    /// </exception>
    public void OnServerGoAway(Http3GoAwayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var streamId = frame.StreamId;

        // RFC 9114 §5.2: The stream ID MUST be a client-initiated bidirectional
        // stream ID (divisible by 4) when sent from server to client.
        if (streamId % 4 != 0)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"Server GOAWAY stream ID {streamId} is not a valid client-initiated bidirectional stream ID (must be divisible by 4, RFC 9114 §5.2).");
        }

        // RFC 9114 §5.2: An endpoint MAY send multiple GOAWAY frames,
        // but the identifier MUST NOT increase.
        if (_lastServerGoAwayStreamId >= 0 && streamId > _lastServerGoAwayStreamId)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"Server GOAWAY stream ID {streamId} must not increase beyond previous value {_lastServerGoAwayStreamId} (RFC 9114 §5.2).");
        }

        _lastServerGoAwayStreamId = streamId;
    }

    /// <summary>
    /// Determines whether the given stream ID was affected by a server GOAWAY
    /// (i.e., the stream was NOT processed by the server and can be retried).
    /// </summary>
    /// <param name="streamId">The client-initiated bidirectional stream ID to check.</param>
    /// <returns>
    /// <c>true</c> if the stream ID is &gt;= the GOAWAY stream ID
    /// (meaning it was NOT processed and should be retried on a new connection);
    /// <c>false</c> if no GOAWAY was received or the stream was processed.
    /// </returns>
    public bool IsStreamAffected(long streamId)
    {
        if (!IsGoingAway)
        {
            return false;
        }

        return streamId >= _lastServerGoAwayStreamId;
    }

    /// <summary>
    /// Returns the stream IDs from the given active streams that are affected
    /// by the server GOAWAY (stream ID &gt;= last GOAWAY stream ID) and
    /// should be retried on a new connection.
    /// </summary>
    /// <param name="activeStreamIds">The currently active stream IDs.</param>
    /// <returns>Stream IDs that need to be retried.</returns>
    public IReadOnlyList<long> GetRetryableStreamIds(IEnumerable<long> activeStreamIds)
    {
        ArgumentNullException.ThrowIfNull(activeStreamIds);

        if (!IsGoingAway)
        {
            return [];
        }

        var retryable = new List<long>();
        foreach (var id in activeStreamIds)
        {
            if (id >= _lastServerGoAwayStreamId)
            {
                retryable.Add(id);
            }
        }

        return retryable;
    }

    /// <summary>
    /// Determines whether a new request can be sent on this connection.
    /// After receiving a server GOAWAY, new requests MUST NOT be sent
    /// if the next stream ID would be &gt;= the GOAWAY stream ID.
    /// </summary>
    /// <param name="nextStreamId">The stream ID that would be used for the next request.</param>
    /// <returns><c>true</c> if the request can be sent; <c>false</c> if it should use a new connection.</returns>
    public bool CanSendRequest(long nextStreamId)
    {
        if (!IsGoingAway)
        {
            return true;
        }

        return nextStreamId < _lastServerGoAwayStreamId;
    }

    /// <summary>
    /// Creates a GOAWAY frame for the client to send to the server before
    /// closing the connection. The push ID indicates the last push the
    /// client is willing to accept.
    /// </summary>
    /// <param name="pushId">
    /// The maximum push ID the client will accept.
    /// Use 0 to reject all server pushes.
    /// </param>
    /// <returns>A serialized GOAWAY frame ready to send on the control stream.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="pushId"/> is negative.
    /// </exception>
    /// <exception cref="Http3Exception">
    /// Thrown with <see cref="Http3ErrorCode.IdError"/> if the push ID
    /// increases compared to a previously sent client GOAWAY (RFC 9114 §5.2).
    /// </exception>
    public Http3GoAwayFrame CreateClientGoAway(long pushId)
    {
        if (pushId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pushId), pushId, "Push ID must be non-negative.");
        }

        // RFC 9114 §5.2: The identifier MUST NOT increase between GOAWAY frames.
        if (_lastClientGoAwayPushId >= 0 && pushId > _lastClientGoAwayPushId)
        {
            throw new Http3Exception(
                Http3ErrorCode.IdError,
                $"Client GOAWAY push ID {pushId} must not increase beyond previous value {_lastClientGoAwayPushId} (RFC 9114 §5.2).");
        }

        _lastClientGoAwayPushId = pushId;
        return new Http3GoAwayFrame(pushId);
    }
}
