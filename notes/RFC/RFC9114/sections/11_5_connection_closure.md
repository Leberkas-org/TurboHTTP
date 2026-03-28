---
title: "5.  Connection Closure"
rfc_number: 9114
rfc_section: "5"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 5: Connection Closure — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, connection_closure]
---

## 5.  Connection Closure

5.  Connection Closure

   Once established, an HTTP/3 connection can be used for many requests
   and responses over time until the connection is closed.  Connection
   closure can happen in any of several different ways.

## 5.1  Idle Connections

   Each QUIC endpoint declares an idle timeout during the handshake.  If
   the QUIC connection remains idle (no packets received) for longer
   than this duration, the peer will assume that the connection has been
   closed.  HTTP/3 implementations will need to open a new HTTP/3
   connection for new requests if the existing connection has been idle
   for longer than the idle timeout negotiated during the QUIC
> **SHOULD**: handshake, and they SHOULD do so if approaching the idle timeout; see
   Section 10.1 of [QUIC-TRANSPORT].

   HTTP clients are expected to request that the transport keep
   connections open while there are responses outstanding for requests
   or server pushes, as described in Section 10.1.2 of [QUIC-TRANSPORT].
   If the client is not expecting a response from the server, allowing
   an idle connection to time out is preferred over expending effort
> **MAY**: maintaining a connection that might not be needed.  A gateway MAY
   maintain connections in anticipation of need rather than incur the
> **SHOULD**: latency cost of connection establishment to servers.  Servers SHOULD
   NOT actively keep connections open.

## 5.2  Connection Shutdown

   Even when a connection is not idle, either endpoint can decide to
   stop using the connection and initiate a graceful connection close.
   Endpoints initiate the graceful shutdown of an HTTP/3 connection by
   sending a GOAWAY frame.  The GOAWAY frame contains an identifier that
   indicates to the receiver the range of requests or pushes that were
   or might be processed in this connection.  The server sends a client-
   initiated bidirectional stream ID; the client sends a push ID.
   Requests or pushes with the indicated identifier or greater are
   rejected (Section 4.1.1) by the sender of the GOAWAY.  This
> **MAY**: identifier MAY be zero if no requests or pushes were processed.

   The information in the GOAWAY frame enables a client and server to
   agree on which requests or pushes were accepted prior to the shutdown
   of the HTTP/3 connection.  Upon sending a GOAWAY frame, the endpoint
> **SHOULD**: SHOULD explicitly cancel (see Sections 4.1.1 and 7.2.3) any requests
   or pushes that have identifiers greater than or equal to the one
   indicated, in order to clean up transport state for the affected
> **SHOULD**: streams.  The endpoint SHOULD continue to do so as more requests or
   pushes arrive.

> **MUST NOT**: Endpoints MUST NOT initiate new requests or promise new pushes on the
   connection after receipt of a GOAWAY frame from the peer.  Clients
> **MAY**: MAY establish a new connection to send additional requests.

   Some requests or pushes might already be in transit:

   *  Upon receipt of a GOAWAY frame, if the client has already sent
      requests with a stream ID greater than or equal to the identifier
      contained in the GOAWAY frame, those requests will not be
      processed.  Clients can safely retry unprocessed requests on a
      different HTTP connection.  A client that is unable to retry
      requests loses all requests that are in flight when the server
      closes the connection.

      Requests on stream IDs less than the stream ID in a GOAWAY frame
      from the server might have been processed; their status cannot be
      known until a response is received, the stream is reset
      individually, another GOAWAY is received with a lower stream ID
      than that of the request in question, or the connection
      terminates.

> **MAY**: Servers MAY reject individual requests on streams below the
      indicated ID if these requests were not processed.

   *  If a server receives a GOAWAY frame after having promised pushes
      with a push ID greater than or equal to the identifier contained
      in the GOAWAY frame, those pushes will not be accepted.

> **SHOULD**: Servers SHOULD send a GOAWAY frame when the closing of a connection
   is known in advance, even if the advance notice is small, so that the
   remote peer can know whether or not a request has been partially
   processed.  For example, if an HTTP client sends a POST at the same
   time that a server closes a QUIC connection, the client cannot know
   if the server started to process that POST request if the server does
   not send a GOAWAY frame to indicate what streams it might have acted
   on.

> **MAY**: An endpoint MAY send multiple GOAWAY frames indicating different
   identifiers, but the identifier in each frame MUST NOT be greater
   than the identifier in any previous frame, since clients might
   already have retried unprocessed requests on another HTTP connection.
   Receiving a GOAWAY containing a larger identifier than previously
> **MUST**: received MUST be treated as a connection error of type H3_ID_ERROR.

   An endpoint that is attempting to gracefully shut down a connection
   can send a GOAWAY frame with a value set to the maximum possible
   value (2^62-4 for servers, 2^62-1 for clients).  This ensures that
   the peer stops creating new requests or pushes.  After allowing time
   for any in-flight requests or pushes to arrive, the endpoint can send
   another GOAWAY frame indicating which requests or pushes it might
   accept before the end of the connection.  This ensures that a
   connection can be cleanly shut down without losing requests.

   A client has more flexibility in the value it chooses for the Push ID
   field in a GOAWAY that it sends.  A value of 2^62-1 indicates that
   the server can continue fulfilling pushes that have already been
   promised.  A smaller value indicates the client will reject pushes
   with push IDs greater than or equal to this value.  Like the server,
> **MAY**: the client MAY send subsequent GOAWAY frames so long as the specified
   push ID is no greater than any previously sent value.

   Even when a GOAWAY indicates that a given request or push will not be
   processed or accepted upon receipt, the underlying transport
   resources still exist.  The endpoint that initiated these requests
   can cancel them to clean up transport state.

   Once all accepted requests and pushes have been processed, the
> **MAY**: endpoint can permit the connection to become idle, or it MAY initiate
   an immediate closure of the connection.  An endpoint that completes a
> **SHOULD**: graceful shutdown SHOULD use the H3_NO_ERROR error code when closing
   the connection.

   If a client has consumed all available bidirectional stream IDs with
   requests, the server need not send a GOAWAY frame, since the client
   is unable to make further requests.

## 5.3  Immediate Application Closure

   An HTTP/3 implementation can immediately close the QUIC connection at
   any time.  This results in sending a QUIC CONNECTION_CLOSE frame to
   the peer indicating that the application layer has terminated the
   connection.  The application error code in this frame indicates to
   the peer why the connection is being closed.  See Section 8 for error
   codes that can be used when closing a connection in HTTP/3.

> **MAY**: Before closing the connection, a GOAWAY frame MAY be sent to allow
   the client to retry some requests.  Including the GOAWAY frame in the
   same packet as the QUIC CONNECTION_CLOSE frame improves the chances
   of the frame being received by clients.

   If there are open streams that have not been explicitly closed, they
   are implicitly closed when the connection is closed; see Section 10.2
   of [QUIC-TRANSPORT].

## 5.4  Transport Closure

   For various reasons, the QUIC transport could indicate to the
   application layer that the connection has terminated.  This might be
   due to an explicit closure by the peer, a transport-level error, or a
   change in network topology that interrupts connectivity.

> **MUST**: If a connection terminates without a GOAWAY frame, clients MUST
   assume that any request that was sent, whether in whole or in part,
   might have been processed.

---

## TurboHttp Compliance

**Status**: ⚠️ Partial

### Implementation Notes

- **`Http3Connection.cs`** — Implements graceful shutdown via GOAWAY frame exchange per §5.2; tracks last accepted stream ID; supports multiple GOAWAY frames with decreasing IDs
- **`Http3ControlStream.cs`** — Sends GOAWAY on control stream before connection closure per §5.2; uses `H3_NO_ERROR` for graceful close per §5.2
- **`Http3IdleTimeoutHandler.cs`** — Monitors QUIC idle timeout and triggers reconnection per §5.1
- **`QuicTransportAdapter.cs`** — Maps QUIC CONNECTION_CLOSE to TurboHttp connection termination per §5.3

### Test References

- `TurboHttp.Tests/RFC9114/15_Http3ConnectionClosureTests.cs` — GOAWAY frame exchange, graceful shutdown sequence
- `TurboHttp.Tests/RFC9114/16_Http3IdleTimeoutTests.cs` — Idle connection management
- `TurboHttp.StreamTests/` — End-to-end connection lifecycle tests

### Known Gaps

- ❌ Two-phase GOAWAY shutdown (§5.2) — does not send initial max-value GOAWAY followed by final GOAWAY; sends single GOAWAY with actual last stream ID
- ⚠️ Client-to-server GOAWAY with push ID (§5.2) — not sent since server push is not implemented
- ⚠️ Transport closure (§5.4) — assumes unfinished requests failed on transport termination, but retry logic does not always distinguish processed vs. unprocessed requests
