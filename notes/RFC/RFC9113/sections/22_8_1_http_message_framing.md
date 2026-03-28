---
title: "8.1.  HTTP Message Framing"
rfc_number: 9113
rfc_section: "8.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.1: HTTP Message Framing — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, http_message_framing]
---

## 8.1.  HTTP Message Framing

8.  Expressing HTTP Semantics in HTTP/2

   HTTP/2 is an instantiation of the HTTP message abstraction (Section 6
   of [HTTP]).

## 8.1  HTTP Message Framing

   A client sends an HTTP request on a new stream, using a previously
   unused stream identifier (Section 5.1.1).  A server sends an HTTP
   response on the same stream as the request.

   An HTTP message (request or response) consists of:

   1.  one HEADERS frame (followed by zero or more CONTINUATION frames)
       containing the header section (see Section 6.3 of [HTTP]),

   2.  zero or more DATA frames containing the message content (see
       Section 6.4 of [HTTP]), and

   3.  optionally, one HEADERS frame (followed by zero or more
       CONTINUATION frames) containing the trailer section, if present
       (see Section 6.5 of [HTTP]).

> **MAY**: For a response only, a server MAY send any number of interim
   responses before the HEADERS frame containing a final response.  An
   interim response consists of a HEADERS frame (which might be followed
   by zero or more CONTINUATION frames) containing the control data and
   header section of an interim (1xx) HTTP response (see Section 15 of
   [HTTP]).  A HEADERS frame with the END_STREAM flag set that carries
   an informational status code is malformed (Section 8.1.1).

   The last frame in the sequence bears an END_STREAM flag, noting that
   a HEADERS frame with the END_STREAM flag set can be followed by
   CONTINUATION frames that carry any remaining fragments of the field
   block.

> **MUST NOT**: Other frames (from any stream) MUST NOT occur between the HEADERS
   frame and any CONTINUATION frames that might follow.

   HTTP/2 uses DATA frames to carry message content.  The chunked
   transfer encoding defined in Section 7.1 of [HTTP/1.1] cannot be used
   in HTTP/2; see Section 8.2.2.

   Trailer fields are carried in a field block that also terminates the
   stream.  That is, trailer fields comprise a sequence starting with a
   HEADERS frame, followed by zero or more CONTINUATION frames, where
> **MUST NOT**: the HEADERS frame bears an END_STREAM flag.  Trailers MUST NOT
   include pseudo-header fields (Section 8.3).  An endpoint that
> **MUST**: receives pseudo-header fields in trailers MUST treat the request or
   response as malformed (Section 8.1.1).

   An endpoint that receives a HEADERS frame without the END_STREAM flag
   set after receiving the HEADERS frame that opens a request or after
> **MUST**: receiving a final (non-informational) status code MUST treat the
   corresponding request or response as malformed (Section 8.1.1).

   An HTTP request/response exchange fully consumes a single stream.  A
   request starts with the HEADERS frame that puts the stream into the
   "open" state.  The request ends with a frame with the END_STREAM flag
   set, which causes the stream to become "half-closed (local)" for the
   client and "half-closed (remote)" for the server.  A response stream
   starts with zero or more interim responses in HEADERS frames,
   followed by a HEADERS frame containing a final status code.

   An HTTP response is complete after the server sends -- or the client
   receives -- a frame with the END_STREAM flag set (including any
   CONTINUATION frames needed to complete a field block).  A server can
   send a complete response prior to the client sending an entire
   request if the response does not depend on any portion of the request
> **MAY**: that has not been sent and received.  When this is true, a server MAY
   request that the client abort transmission of a request without error
   by sending a RST_STREAM with an error code of NO_ERROR after sending
   a complete response (i.e., a frame with the END_STREAM flag set).
> **MUST NOT**: Clients MUST NOT discard responses as a result of receiving such a
   RST_STREAM, though clients can always discard responses at their
   discretion for other reasons.

### 8.1.1  Malformed Messages

   A malformed request or response is one that is an otherwise valid
   sequence of HTTP/2 frames but is invalid due to the presence of
   extraneous frames, prohibited fields or pseudo-header fields, the
   absence of mandatory pseudo-header fields, the inclusion of uppercase
   field names, or invalid field names and/or values (in certain
   circumstances; see Section 8.2).

   A request or response that includes message content can include a
   content-length header field.  A request or response is also malformed
   if the value of a content-length header field does not equal the sum
   of the DATA frame payload lengths that form the content, unless the
   message is defined as having no content.  For example, 204 or 304
   responses contain no content, as does the response to a HEAD request.
   A response that is defined to have no content, as described in
> **MAY**: Section 6.4.1 of [HTTP], MAY have a non-zero content-length header
   field, even though no content is included in DATA frames.

   Intermediaries that process HTTP requests or responses (i.e., any
> **MUST NOT**: intermediary not acting as a tunnel) MUST NOT forward a malformed
   request or response.  Malformed requests or responses that are
> **MUST**: detected MUST be treated as a stream error (Section 5.4.2) of type
   PROTOCOL_ERROR.

> **MAY**: For malformed requests, a server MAY send an HTTP response prior to
   closing or resetting the stream.  Clients MUST NOT accept a malformed
   response.

   Endpoints that progressively process messages might have performed
   some processing before identifying a request or response as
   malformed.  For instance, it might be possible to generate an
   informational or 404 status code without having received a complete
   request.  Similarly, intermediaries might forward incomplete messages
> **MAY**: before detecting errors.  A server MAY generate a final response
   before receiving an entire request when the response does not depend
   on the remainder of the request being correct.

   These requirements are intended to protect against several types of
   common attacks against HTTP; they are deliberately strict because
   being permissive can expose implementations to these vulnerabilities.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`Http2FrameDecoder.cs`** — Validates message framing: HEADERS→CONTINUATION sequences, END_STREAM/END_HEADERS flag handling, content-length vs DATA payload length checks
- **`Http2FrameEncoder.cs`** — Produces correct HEADERS/DATA/CONTINUATION sequences with proper flag management
- **`Http2StreamState.cs`** — Tracks stream lifecycle (open → half-closed → closed) per §8.1 framing rules
- **`Http2ConnectionStage.cs`** — Detects and rejects malformed messages per §8.1.1; sends PROTOCOL_ERROR stream errors for violations

### Test References
- `TurboHttp.Tests/RFC9113/22_Http2MessageFramingTests.cs` — Message structure, END_STREAM handling, malformed message detection

### Known Gaps
- ⚠️ Trailer field pseudo-header rejection — Trailers with pseudo-headers detected but error response generation is basic
- ❌ Intermediary forwarding rules — TurboHttp is a client library, not an intermediary; forwarding checks not applicable

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
