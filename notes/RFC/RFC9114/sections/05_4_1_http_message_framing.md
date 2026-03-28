---
title: "4.1.  HTTP Message Framing"
rfc_number: 9114
rfc_section: "4.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 4.1: HTTP Message Framing — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, http_message_framing]
---

## 4.1.  HTTP Message Framing

4.  Expressing HTTP Semantics in HTTP/3

## 4.1  HTTP Message Framing

   A client sends an HTTP request on a request stream, which is a
   client-initiated bidirectional QUIC stream; see Section 6.1.  A
> **MUST**: client MUST send only a single request on a given stream.  A server
   sends zero or more interim HTTP responses on the same stream as the
   request, followed by a single final HTTP response, as detailed below.
   See Section 15 of [HTTP] for a description of interim and final HTTP
   responses.

   Pushed responses are sent on a server-initiated unidirectional QUIC
   stream; see Section 6.2.2.  A server sends zero or more interim HTTP
   responses, followed by a single final HTTP response, in the same
   manner as a standard response.  Push is described in more detail in
   Section 4.6.

   On a given stream, receipt of multiple requests or receipt of an
> **MUST**: additional HTTP response following a final HTTP response MUST be
   treated as malformed.

   An HTTP message (request or response) consists of:

   1.  the header section, including message control data, sent as a
       single HEADERS frame,

   2.  optionally, the content, if present, sent as a series of DATA
       frames, and

   3.  optionally, the trailer section, if present, sent as a single
       HEADERS frame.

   Header and trailer sections are described in Sections 6.3 and 6.5 of
   [HTTP]; the content is described in Section 6.4 of [HTTP].

> **MUST**: Receipt of an invalid sequence of frames MUST be treated as a
   connection error of type H3_FRAME_UNEXPECTED.  In particular, a DATA
   frame before any HEADERS frame, or a HEADERS or DATA frame after the
   trailing HEADERS frame, is considered invalid.  Other frame types,
   especially unknown frame types, might be permitted subject to their
   own rules; see Section 9.

> **MAY**: A server MAY send one or more PUSH_PROMISE frames before, after, or
   interleaved with the frames of a response message.  These
   PUSH_PROMISE frames are not part of the response; see Section 4.6 for
   more details.  PUSH_PROMISE frames are not permitted on push streams;
> **MUST**: a pushed response that includes PUSH_PROMISE frames MUST be treated
   as a connection error of type H3_FRAME_UNEXPECTED.

   Frames of unknown types (Section 9), including reserved frames
> **MAY**: (Section 7.2.8) MAY be sent on a request or push stream before,
   after, or interleaved with other frames described in this section.

   The HEADERS and PUSH_PROMISE frames might reference updates to the
   QPACK dynamic table.  While these updates are not directly part of
   the message exchange, they must be received and processed before the
   message can be consumed.  See Section 4.2 for more details.

   Transfer codings (see Section 7 of [HTTP/1.1]) are not defined for
> **MUST NOT**: HTTP/3; the Transfer-Encoding header field MUST NOT be used.

> **MAY**: A response MAY consist of multiple messages when and only when one or
   more interim responses (1xx; see Section 15.2 of [HTTP]) precede a
   final response to the same request.  Interim responses do not contain
   content or trailer sections.

   An HTTP request/response exchange fully consumes a client-initiated
> **MUST**: bidirectional QUIC stream.  After sending a request, a client MUST
   close the stream for sending.  Unless using the CONNECT method (see
> **MUST NOT**: Section 4.4), clients MUST NOT make stream closure dependent on
   receiving a response to their request.  After sending a final
> **MUST**: response, the server MUST close the stream for sending.  At this
   point, the QUIC stream is fully closed.

   When a stream is closed, this indicates the end of the final HTTP
   message.  Because some messages are large or unbounded, endpoints
> **SHOULD**: SHOULD begin processing partial HTTP messages once enough of the
   message has been received to make progress.  If a client-initiated
   stream terminates without enough of the HTTP message to provide a
> **SHOULD**: complete response, the server SHOULD abort its response stream with
   the error code H3_REQUEST_INCOMPLETE.

   A server can send a complete response prior to the client sending an
   entire request if the response does not depend on any portion of the
   request that has not been sent and received.  When the server does
> **MAY**: not need to receive the remainder of the request, it MAY abort
   reading the request stream, send a complete response, and cleanly
   close the sending part of the stream.  The error code H3_NO_ERROR
> **SHOULD**: SHOULD be used when requesting that the client stop sending on the
   request stream.  Clients MUST NOT discard complete responses as a
   result of having their request terminated abruptly, though clients
   can always discard responses at their discretion for other reasons.
   If the server sends a partial or complete response but does not abort
> **SHOULD**: reading the request, clients SHOULD continue sending the content of
   the request and close the stream normally.

### 4.1.1  Request Cancellation and Rejection

> **MAY**: Once a request stream has been opened, the request MAY be cancelled
   by either endpoint.  Clients cancel requests if the response is no
   longer of interest; servers cancel requests if they are unable to or
   choose not to respond.  When possible, it is RECOMMENDED that servers
   send an HTTP response with an appropriate status code rather than
   cancelling a request it has already begun processing.

> **SHOULD**: Implementations SHOULD cancel requests by abruptly terminating any
   directions of a stream that are still open.  To do so, an
   implementation resets the sending parts of streams and aborts reading
   on the receiving parts of streams; see Section 2.4 of
   [QUIC-TRANSPORT].

   When the server cancels a request without performing any application
> **SHOULD**: processing, the request is considered "rejected".  The server SHOULD
   abort its response stream with the error code H3_REQUEST_REJECTED.
   In this context, "processed" means that some data from the stream was
   passed to some higher layer of software that might have taken some
   action as a result.  The client can treat requests rejected by the
   server as though they had never been sent at all, thereby allowing
   them to be retried later.

> **MUST NOT**: Servers MUST NOT use the H3_REQUEST_REJECTED error code for requests
   that were partially or fully processed.  When a server abandons a
> **SHOULD**: response after partial processing, it SHOULD abort its response
   stream with the error code H3_REQUEST_CANCELLED.

> **SHOULD**: Client SHOULD use the error code H3_REQUEST_CANCELLED to cancel
   requests.  Upon receipt of this error code, a server MAY abruptly
   terminate the response using the error code H3_REQUEST_REJECTED if no
> **MUST NOT**: processing was performed.  Clients MUST NOT use the
   H3_REQUEST_REJECTED error code, except when a server has requested
   closure of the request stream with this error code.

   If a stream is cancelled after receiving a complete response, the
> **MAY**: client MAY ignore the cancellation and use the response.  However, if
   a stream is cancelled after receiving a partial response, the
> **SHOULD NOT**: response SHOULD NOT be used.  Only idempotent actions such as GET,
   PUT, or DELETE can be safely retried; a client SHOULD NOT
   automatically retry a request with a non-idempotent method unless it
   has some means to know that the request semantics are idempotent
   independent of the method or some means to detect that the original
   request was never applied.  See Section 9.2.2 of [HTTP] for more
   details.

### 4.1.2  Malformed Requests and Responses

   A malformed request or response is one that is an otherwise valid
   sequence of frames but is invalid due to:

   *  the presence of prohibited fields or pseudo-header fields,

   *  the absence of mandatory pseudo-header fields,

   *  invalid values for pseudo-header fields,

   *  pseudo-header fields after fields,

   *  an invalid sequence of HTTP messages,

   *  the inclusion of uppercase field names, or

   *  the inclusion of invalid characters in field names or values.

   A request or response that is defined as having content when it
   contains a Content-Length header field (Section 8.6 of [HTTP]) is
   malformed if the value of the Content-Length header field does not
   equal the sum of the DATA frame lengths received.  A response that is
   defined as never having content, even when a Content-Length is
   present, can have a non-zero Content-Length header field even though
   no content is included in DATA frames.

   Intermediaries that process HTTP requests or responses (i.e., any
> **MUST NOT**: intermediary not acting as a tunnel) MUST NOT forward a malformed
   request or response.  Malformed requests or responses that are
> **MUST**: detected MUST be treated as a stream error of type H3_MESSAGE_ERROR.

> **MAY**: For malformed requests, a server MAY send an HTTP response indicating
   the error prior to closing or resetting the stream.  Clients MUST NOT
   accept a malformed response.  Note that these requirements are
   intended to protect against several types of common attacks against
   HTTP; they are deliberately strict because being permissive can
   expose implementations to these vulnerabilities.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes

- **`Http3FrameDecoder.cs`** — Decodes HTTP/3 frame sequences (HEADERS → DATA* → HEADERS?) enforcing the valid message sequence per §4.1; raises `H3_FRAME_UNEXPECTED` for invalid frame ordering
- **`Http3FrameEncoder.cs`** — Encodes request messages as HEADERS + DATA frames with proper stream closure
- **`Http3RequestStream.cs`** — Manages bidirectional request stream lifecycle: sends request, closes send side, reads response per §4.1 requirements
- **`Http3ResponseDecoder.cs`** — Validates response frame sequences including interim (1xx) responses followed by final response; rejects `Transfer-Encoding` header per §4.1

### Test References

- `TurboHttp.Tests/RFC9114/01_Http3FrameDecoderTests.cs` — Frame sequence validation tests
- `TurboHttp.Tests/RFC9114/02_Http3FrameEncoderTests.cs` — Frame encoding tests
- `TurboHttp.Tests/RFC9114/03_Http3MessageFramingTests.cs` — Malformed message detection, Content-Length mismatch tests
- `TurboHttp.StreamTests/` — Stream-level integration tests for full request/response exchanges

### Known Gaps

- ❌ PUSH_PROMISE interleaving (§4.1) — server push not implemented, PUSH_PROMISE frames rejected but not fully parsed
- ⚠️ Partial: `H3_REQUEST_INCOMPLETE` error sent when client stream terminates early, but edge cases around partial Content-Length remain under test
