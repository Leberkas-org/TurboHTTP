namespace TurboHttp.Protocol.RFC9114;

// HTTP/3 Bidirectional Request Stream Lifecycle  —  RFC 9114 §6.1
//
// Each HTTP request-response exchange is mapped to a single QUIC
// bidirectional stream. The client opens the stream, sends the request
// (HEADERS + optional DATA), then half-closes the sending side.
// The server sends the response (HEADERS + optional DATA) and the
// stream is fully closed after the response is received.
//
// Client-initiated bidirectional stream IDs: 0, 4, 8, … (QUIC §2.1: 4n)

/// <summary>
/// Lifecycle states for an HTTP/3 request stream (RFC 9114 §6.1).
/// </summary>
public enum Http3RequestStreamState
{
    /// <summary>Stream opened, request not yet sent.</summary>
    Open,

    /// <summary>Request headers sent on the stream.</summary>
    HeadersSent,

    /// <summary>
    /// Client has finished sending (HEADERS + optional DATA).
    /// The sending side is half-closed (FIN sent).
    /// Waiting for the server's response.
    /// </summary>
    HalfClosedLocal,

    /// <summary>
    /// Response headers received from the server.
    /// Body data may still be arriving.
    /// </summary>
    ResponseHeadersReceived,

    /// <summary>
    /// Response fully received. Stream is closed in both directions.
    /// </summary>
    Closed,

    /// <summary>
    /// Stream was reset due to an error (e.g., H3_REQUEST_CANCELLED).
    /// </summary>
    Reset,
}

/// <summary>
/// Tracks the lifecycle of a single HTTP/3 bidirectional request stream
/// per RFC 9114 §6.1. Each request-response exchange maps to exactly one
/// QUIC bidirectional stream.
/// </summary>
public sealed class Http3RequestStream
{
    /// <summary>
    /// The QUIC stream ID for this request stream.
    /// Client-initiated bidirectional streams use IDs 0, 4, 8, … (4n).
    /// </summary>
    public long StreamId { get; }

    /// <summary>Current lifecycle state of this stream.</summary>
    public Http3RequestStreamState State { get; private set; } = Http3RequestStreamState.Open;

    /// <summary>
    /// Creates a new request stream tracker for the given stream ID.
    /// </summary>
    /// <param name="streamId">
    /// Must be a client-initiated bidirectional stream ID (divisible by 4).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown if <paramref name="streamId"/> is negative or not divisible by 4.
    /// </exception>
    public Http3RequestStream(long streamId)
    {
        if (streamId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamId), streamId,
                "Stream ID must be non-negative.");
        }

        if (streamId % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(streamId), streamId,
                "Client-initiated bidirectional stream IDs must be divisible by 4 (QUIC §2.1).");
        }

        StreamId = streamId;
    }

    /// <summary>
    /// Records that the request HEADERS frame has been sent on this stream.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the stream is not in the <see cref="Http3RequestStreamState.Open"/> state.
    /// </exception>
    public void OnHeadersSent()
    {
        if (State != Http3RequestStreamState.Open)
        {
            throw new InvalidOperationException(
                $"Cannot send headers in state {State}; expected {Http3RequestStreamState.Open}.");
        }

        State = Http3RequestStreamState.HeadersSent;
    }

    /// <summary>
    /// Records that the client has finished sending the request
    /// (half-closes the sending side). This is called after the last
    /// DATA frame (or immediately after HEADERS if there is no body).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the stream is not in <see cref="Http3RequestStreamState.HeadersSent"/>.
    /// </exception>
    public void OnRequestComplete()
    {
        if (State != Http3RequestStreamState.HeadersSent)
        {
            throw new InvalidOperationException(
                $"Cannot complete request in state {State}; expected {Http3RequestStreamState.HeadersSent}.");
        }

        State = Http3RequestStreamState.HalfClosedLocal;
    }

    /// <summary>
    /// Records that the response HEADERS frame has been received from the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the stream is not in <see cref="Http3RequestStreamState.HalfClosedLocal"/>.
    /// </exception>
    public void OnResponseHeadersReceived()
    {
        if (State != Http3RequestStreamState.HalfClosedLocal)
        {
            throw new InvalidOperationException(
                $"Cannot receive response headers in state {State}; expected {Http3RequestStreamState.HalfClosedLocal}.");
        }

        State = Http3RequestStreamState.ResponseHeadersReceived;
    }

    /// <summary>
    /// Records that the response has been fully received (server finished sending).
    /// The stream transitions to <see cref="Http3RequestStreamState.Closed"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the stream is not in <see cref="Http3RequestStreamState.ResponseHeadersReceived"/>.
    /// </exception>
    public void OnResponseComplete()
    {
        if (State != Http3RequestStreamState.ResponseHeadersReceived)
        {
            throw new InvalidOperationException(
                $"Cannot complete response in state {State}; expected {Http3RequestStreamState.ResponseHeadersReceived}.");
        }

        State = Http3RequestStreamState.Closed;
    }

    /// <summary>
    /// Resets the stream with an error. Can be called from any non-closed state.
    /// </summary>
    /// <param name="errorCode">The HTTP/3 error code for the reset.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the stream is already closed or reset.
    /// </exception>
    public void OnReset(Http3ErrorCode errorCode)
    {
        if (State == Http3RequestStreamState.Closed || State == Http3RequestStreamState.Reset)
        {
            throw new InvalidOperationException(
                $"Cannot reset stream in state {State}.");
        }

        State = Http3RequestStreamState.Reset;
    }

    /// <summary>Whether the stream has completed its full lifecycle.</summary>
    public bool IsClosed => State == Http3RequestStreamState.Closed;

    /// <summary>Whether the stream was reset due to an error.</summary>
    public bool IsReset => State == Http3RequestStreamState.Reset;

    /// <summary>Whether the stream is still active (not closed or reset).</summary>
    public bool IsActive => State != Http3RequestStreamState.Closed
                            && State != Http3RequestStreamState.Reset;
}

/// <summary>
/// Allocates client-initiated bidirectional QUIC stream IDs.
/// Per QUIC (RFC 9000 §2.1), client-initiated bidirectional streams
/// use IDs of the form 4n: 0, 4, 8, 12, …
/// </summary>
public sealed class Http3StreamIdAllocator
{
    /// <summary>The next stream ID that will be allocated.</summary>
    public long NextStreamId { get; private set; }

    /// <summary>
    /// Allocates the next client-initiated bidirectional stream ID.
    /// </summary>
    /// <returns>A stream ID divisible by 4.</returns>
    public long Allocate()
    {
        var id = NextStreamId;
        NextStreamId += 4;
        return id;
    }

    /// <summary>
    /// Allocates the next stream ID and returns a new
    /// <see cref="Http3RequestStream"/> tracker for it.
    /// </summary>
    public Http3RequestStream AllocateStream()
    {
        return new Http3RequestStream(Allocate());
    }
}