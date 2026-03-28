---
title: "8.  Error Handling"
rfc_number: 9114
rfc_section: "8"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 8: Error Handling — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, error_handling]
---

## 8.  Error Handling

8.  Error Handling

   When a stream cannot be completed successfully, QUIC allows the
   application to abruptly terminate (reset) that stream and communicate
   a reason; see Section 2.4 of [QUIC-TRANSPORT].  This is referred to
   as a "stream error".  An HTTP/3 implementation can decide to close a
   QUIC stream and communicate the type of error.  Wire encodings of
   error codes are defined in Section 8.1.  Stream errors are distinct
   from HTTP status codes that indicate error conditions.  Stream errors
   indicate that the sender did not transfer or consume the full request
   or response, while HTTP status codes indicate the result of a request
   that was successfully received.

   If an entire connection needs to be terminated, QUIC similarly
   provides mechanisms to communicate a reason; see Section 5.3 of
   [QUIC-TRANSPORT].  This is referred to as a "connection error".
   Similar to stream errors, an HTTP/3 implementation can terminate a
   QUIC connection and communicate the reason using an error code from
   Section 8.1.

   Although the reasons for closing streams and connections are called
   "errors", these actions do not necessarily indicate a problem with
   the connection or either implementation.  For example, a stream can
   be reset if the requested resource is no longer needed.

> **MAY**: An endpoint MAY choose to treat a stream error as a connection error
   under certain circumstances, closing the entire connection in
   response to a condition on a single stream.  Implementations need to
   consider the impact on outstanding requests before making this
   choice.

   Because new error codes can be defined without negotiation (see
   Section 9), use of an error code in an unexpected context or receipt
> **MUST**: of an unknown error code MUST be treated as equivalent to
   H3_NO_ERROR.  However, closing a stream can have other effects
   regardless of the error code; for example, see Section 4.1.

## 8.1  HTTP/3 Error Codes

   The following error codes are defined for use when abruptly
   terminating streams, aborting reading of streams, or immediately
   closing HTTP/3 connections.

   H3_NO_ERROR (0x0100):  No error.  This is used when the connection or
      stream needs to be closed, but there is no error to signal.

   H3_GENERAL_PROTOCOL_ERROR (0x0101):  Peer violated protocol
      requirements in a way that does not match a more specific error
      code or endpoint declines to use the more specific error code.

   H3_INTERNAL_ERROR (0x0102):  An internal error has occurred in the
      HTTP stack.

   H3_STREAM_CREATION_ERROR (0x0103):  The endpoint detected that its
      peer created a stream that it will not accept.

   H3_CLOSED_CRITICAL_STREAM (0x0104):  A stream required by the HTTP/3
      connection was closed or reset.

   H3_FRAME_UNEXPECTED (0x0105):  A frame was received that was not
      permitted in the current state or on the current stream.

   H3_FRAME_ERROR (0x0106):  A frame that fails to satisfy layout
      requirements or with an invalid size was received.

   H3_EXCESSIVE_LOAD (0x0107):  The endpoint detected that its peer is
      exhibiting a behavior that might be generating excessive load.

   H3_ID_ERROR (0x0108):  A stream ID or push ID was used incorrectly,
      such as exceeding a limit, reducing a limit, or being reused.

   H3_SETTINGS_ERROR (0x0109):  An endpoint detected an error in the
      payload of a SETTINGS frame.

   H3_MISSING_SETTINGS (0x010a):  No SETTINGS frame was received at the
      beginning of the control stream.

   H3_REQUEST_REJECTED (0x010b):  A server rejected a request without
      performing any application processing.

   H3_REQUEST_CANCELLED (0x010c):  The request or its response
      (including pushed response) is cancelled.

   H3_REQUEST_INCOMPLETE (0x010d):  The client's stream terminated
      without containing a fully formed request.

   H3_MESSAGE_ERROR (0x010e):  An HTTP message was malformed and cannot
      be processed.

   H3_CONNECT_ERROR (0x010f):  The TCP connection established in
      response to a CONNECT request was reset or abnormally closed.

   H3_VERSION_FALLBACK (0x0110):  The requested operation cannot be
      served over HTTP/3.  The peer should retry over HTTP/1.1.

   Error codes of the format 0x1f * N + 0x21 for non-negative integer
   values of N are reserved to exercise the requirement that unknown
   error codes be treated as equivalent to H3_NO_ERROR (Section 9).
> **SHOULD**: Implementations SHOULD select an error code from this space with some
   probability when they would have sent H3_NO_ERROR.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes

- **`Http3ErrorCodes.cs`** — Defines all 16 error codes from §8.1 with correct hex values: `H3_NO_ERROR` (0x0100) through `H3_VERSION_FALLBACK` (0x0110)
- **`Http3FrameDecoder.cs`** — Maps protocol violations to appropriate error codes; treats unknown error codes as `H3_NO_ERROR` per §8
- **`Http3ControlStream.cs`** — Raises `H3_MISSING_SETTINGS` when control stream first frame is not SETTINGS; raises `H3_CLOSED_CRITICAL_STREAM` when control stream is closed
- **`Http3Connection.cs`** — Distinguishes stream errors from connection errors; escalates stream errors to connection errors when appropriate per §8

### Test References

- `TurboHttp.Tests/RFC9114/09_Http3ErrorCodeTests.cs` — Error code value validation, unknown code handling
- `TurboHttp.Tests/RFC9114/10_Http3ConnectionErrorTests.cs` — Connection-level error propagation tests
- `TurboHttp.Tests/RFC9114/11_Http3StreamErrorTests.cs` — Stream-level error isolation tests

### Known Gaps

- ⚠️ Reserved error codes (0x1f*N+0x21) are not probabilistically sent in place of `H3_NO_ERROR` per §8.1 SHOULD — always sends exact error code
