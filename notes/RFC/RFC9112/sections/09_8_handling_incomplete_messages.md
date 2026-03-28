---
title: 8.  Handling Incomplete Messages
rfc_number: 9112
rfc_section: '8'
source_url: 'https://www.rfc-editor.org/rfc/rfc9112'
description: 'Section 8: Handling Incomplete Messages — RFC 9112 — HTTP/1.1'
tags:
  - RFC9112
  - HTTP/1.1
  - message-framing
  - chunked-encoding
  - connection-management
  - keep-alive
  - Host-header
  - pipelining
  - handling_incomplete_messages
---

## 8.  Handling Incomplete Messages

8.  Handling Incomplete Messages

   A server that receives an incomplete request message, usually due to
> **MAY**: a canceled request or a triggered timeout exception, MAY send an
   error response prior to closing the connection.

   A client that receives an incomplete response message, which can
   occur when a connection is closed prematurely or when decoding a
> **MUST**: supposedly chunked transfer coding fails, MUST record the message as
   incomplete.  Cache requirements for incomplete responses are defined
   in Section 3.3 of [CACHING].

   If a response terminates in the middle of the header section (before
   the empty line is received) and the status code might rely on header
   fields to convey the full meaning of the response, then the client
   cannot assume that meaning has been conveyed; the client might need
   to repeat the request in order to determine what action to take next.

   A message body that uses the chunked transfer coding is incomplete if
   the zero-sized chunk that terminates the encoding has not been
   received.  A message that uses a valid Content-Length is incomplete
   if the size of the message body received (in octets) is less than the
   value given by Content-Length.  A response that has neither chunked
   transfer coding nor Content-Length is terminated by closure of the
   connection and, if the header section was received intact, is
   considered complete unless an error was indicated by the underlying
   connection (e.g., an "incomplete close" in TLS would leave the
   response incomplete, as described in Section 9.8).


---

## TurboHttp Compliance

**Status:** ✅ Compliant

**Implementation Notes:**
TurboHttp correctly detects and records incomplete response messages. When a connection closes prematurely (before Content-Length bytes received or before chunked zero-chunk), the response is marked as incomplete. The decoder distinguishes between connection-close terminated responses (complete if headers intact) and prematurely truncated responses.

**Key Components:**
- `Http11ResponseDecoder` — incomplete message detection
- `MessageCompleteness` — tracks whether full body was received
- `ConnectionPool` — handles connection failures and retries

**Compliance Details:**
- ✅ Incomplete chunked messages detected (no zero-chunk received)
- ✅ Content-Length mismatch detected (fewer bytes than declared)
- ✅ Connection-close responses considered complete if headers intact
- ✅ TLS incomplete close detection

**Gaps:** None identified

**Test References:** `TurboHttp.Tests.RFC9112`
