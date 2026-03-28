---
title: 9.3.  Persistence
rfc_number: 9112
rfc_section: '9.3'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 9.3: Persistence — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - persistence
---

## 9.3.  Persistence

## 9.3  Persistence

   HTTP/1.1 defaults to the use of "persistent connections", allowing
   multiple requests and responses to be carried over a single
> **SHOULD**: connection.  HTTP implementations SHOULD support persistent
   connections.

   A recipient determines whether a connection is persistent or not
   based on the protocol version and Connection header field
   (Section 7.6.1 of [HTTP]) in the most recently received message, if
   any:

   *  If the "close" connection option is present (Section 9.6), the
      connection will not persist after the current response; else,

   *  If the received protocol is HTTP/1.1 (or later), the connection
      will persist after the current response; else,

   *  If the received protocol is HTTP/1.0, the "keep-alive" connection
      option is present, either the recipient is not a proxy or the
      message is a response, and the recipient wishes to honor the
      HTTP/1.0 "keep-alive" mechanism, the connection will persist after
      the current response; otherwise,

   *  The connection will close after the current response.

> **MUST**: A client that does not support persistent connections MUST send the
   "close" connection option in every request message.

> **MUST**: A server that does not support persistent connections MUST send the
   "close" connection option in every response message that does not
   have a 1xx (Informational) status code.

> **MAY**: A client MAY send additional requests on a persistent connection
   until it sends or receives a "close" connection option or receives an
   HTTP/1.0 response without a "keep-alive" connection option.

   In order to remain persistent, all messages on a connection need to
   have a self-defined message length (i.e., one not defined by closure
> **MUST**: of the connection), as described in Section 6.  A server MUST read
   the entire request message body or close the connection after sending
   its response; otherwise, the remaining data on a persistent
   connection would be misinterpreted as the next request.  Likewise, a
> **MUST**: client MUST read the entire response message body if it intends to
   reuse the same connection for a subsequent request.

> **MUST NOT**: A proxy server MUST NOT maintain a persistent connection with an
   HTTP/1.0 client (see Appendix C.2.2 for information and discussion of
   the problems with the Keep-Alive header field implemented by many
   HTTP/1.0 clients).

   See Appendix C.2.2 for more information on backwards compatibility
   with HTTP/1.0 clients.

### 9.3.1  Retrying Requests

   Connections can be closed at any time, with or without intention.
   Implementations ought to anticipate the need to recover from
   asynchronous close events.  The conditions under which a client can
   automatically retry a sequence of outstanding requests are defined in
   Section 9.2.2 of [HTTP].

### 9.3.2  Pipelining

> **MAY**: A client that supports persistent connections MAY "pipeline" its
   requests (i.e., send multiple requests without waiting for each
> **MAY**: response).  A server MAY process a sequence of pipelined requests in
   parallel if they all have safe methods (Section 9.2.1 of [HTTP]), but
> **MUST**: it MUST send the corresponding responses in the same order that the
   requests were received.

> **SHOULD**: A client that pipelines requests SHOULD retry unanswered requests if
   the connection closes before it receives all of the corresponding
   responses.  When retrying pipelined requests after a failed
   connection (a connection not explicitly closed by the server in its
> **MUST NOT**: last complete response), a client MUST NOT pipeline immediately after
   connection establishment, since the first remaining request in the
   prior pipeline might have caused an error response that can be lost
   again if multiple requests are sent on a prematurely closed
   connection (see the TCP reset problem described in Section 9.6).

   Idempotent methods (Section 9.2.2 of [HTTP]) are significant to
   pipelining because they can be automatically retried after a
> **SHOULD NOT**: connection failure.  A user agent SHOULD NOT pipeline requests after
   a non-idempotent method, until the final response status code for
   that method has been received, unless the user agent has a means to
   detect and recover from partial failure conditions involving the
   pipelined sequence.

> **MAY**: An intermediary that receives pipelined requests MAY pipeline those
   requests when forwarding them inbound, since it can rely on the
   outbound user agent(s) to determine what requests can be safely
   pipelined.  If the inbound connection fails before receiving a
> **MAY**: response, the pipelining intermediary MAY attempt to retry a sequence
   of requests that have yet to receive a response if the requests all
   have idempotent methods; otherwise, the pipelining intermediary
> **SHOULD**: SHOULD forward any received responses and then close the
   corresponding outbound connection(s) so that the outbound user
   agent(s) can recover accordingly.


---

## TurboHttp Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHttp fully supports HTTP/1.1 persistent connections. The connection pool maintains keep-alive connections and reuses them for subsequent requests. The `close` connection option is respected — connections are released when the server sends `Connection: close`. HTTP/1.0 keep-alive is also supported. The client reads the entire response body before reusing connections.

**Key Components:**
- `ConnectionPool` — manages persistent connection lifecycle, keep-alive, and reuse
- `Http11ResponseDecoder` — detects `Connection: close` and keep-alive signals
- `RetryStage` — handles connection failures with automatic retry for idempotent methods

**Compliance Details:**
- ✅ Persistent connections by default in HTTP/1.1
- ✅ `Connection: close` option respected
- ✅ HTTP/1.0 keep-alive support
- ✅ Full response body consumed before connection reuse
- ✅ Connection retry for idempotent methods (§9.3.1)
- ⚠️ Pipelining not implemented (§9.3.2) — requests are serialized per connection

**Gaps:**
- HTTP/1.1 pipelining not supported (sequential requests only)

**Test References:** `TurboHttp.Tests.RFC9112`
