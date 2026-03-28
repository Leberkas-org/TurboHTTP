---
title: 6.1.  Framing and Completeness
rfc_number: 9110
rfc_section: '6.1'
source_url: 'https://www.rfc-editor.org/rfc/rfc9110'
description: 'Section 6.1: Framing and Completeness — RFC 9110 — HTTP Semantics'
tags:
  - RFC9110
  - HTTP-semantics
  - methods
  - status-codes
  - redirects
  - retries
  - content-negotiation
  - conditional-requests
  - framing_and_completeness
---

## 6.1.  Framing and Completeness

6.  Message Abstraction

   Each major version of HTTP defines its own syntax for communicating
   messages.  This section defines an abstract data type for HTTP
   messages based on a generalization of those message characteristics,
   common structure, and capacity for conveying semantics.  This
   abstraction is used to define requirements on senders and recipients
   that are independent of the HTTP version, such that a message in one
   version can be relayed through other versions without changing its
   meaning.

   A "message" consists of the following:

   *  control data to describe and route the message,

   *  a headers lookup table of name/value pairs for extending that
      control data and conveying additional information about the
      sender, message, content, or context,

   *  a potentially unbounded stream of content, and

   *  a trailers lookup table of name/value pairs for communicating
      information obtained while sending the content.

   Framing and control data is sent first, followed by a header section
   containing fields for the headers table.  When a message includes
   content, the content is sent after the header section, potentially
   followed by a trailer section that might contain fields for the
   trailers table.

   Messages are expected to be processed as a stream, wherein the
   purpose of that stream and its continued processing is revealed while
   being read.  Hence, control data describes what the recipient needs
   to know immediately, header fields describe what needs to be known
   before receiving content, the content (when present) presumably
   contains what the recipient wants or needs to fulfill the message
   semantics, and trailer fields provide optional metadata that was
   unknown prior to sending the content.

   Messages are intended to be "self-descriptive": everything a
   recipient needs to know about the message can be determined by
   looking at the message itself, after decoding or reconstituting parts
   that have been compressed or elided in transit, without requiring an
   understanding of the sender's current application state (established
> **MUST**: via prior messages).  However, a client MUST retain knowledge of the
   request when parsing, interpreting, or caching a corresponding
   response.  For example, responses to the HEAD method look just like
   the beginning of a response to GET but cannot be parsed in the same
   manner.

   Note that this message abstraction is a generalization across many
   versions of HTTP, including features that might not be found in some
   versions.  For example, trailers were introduced within the HTTP/1.1
   chunked transfer coding as a trailer section after the content.  An
   equivalent feature is present in HTTP/2 and HTTP/3 within the header
   block that terminates each stream.

## 6.1  Framing and Completeness

   Message framing indicates how each message begins and ends, such that
   each message can be distinguished from other messages or noise on the
   same connection.  Each major version of HTTP defines its own framing
   mechanism.

   HTTP/0.9 and early deployments of HTTP/1.0 used closure of the
   underlying connection to end a response.  For backwards
   compatibility, this implicit framing is also allowed in HTTP/1.1.
   However, implicit framing can fail to distinguish an incomplete
   response if the connection closes early.  For that reason, almost all
   modern implementations use explicit framing in the form of length-
   delimited sequences of message data.

   A message is considered "complete" when all of the octets indicated
   by its framing are available.  Note that, when no explicit framing is
   used, a response message that is ended by the underlying connection's
   close is considered complete even though it might be
   indistinguishable from an incomplete response, unless a transport-
   level error indicates that it is not complete.


---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`Http11ResponseDecoder.cs`** — Detects message completeness via Content-Length or chunked transfer coding; handles connection-close framing for HTTP/1.0
- **`Http2FrameDecoder.cs`** — Uses END_STREAM flag for message completeness in HTTP/2
- **`Http3FrameDecoder.cs`** — Uses FIN bit on QUIC streams for HTTP/3 message completeness
- **`MessageCompleteness.cs`** — Shared abstraction tracking whether headers, content, and trailers are complete

### Test References
- `TurboHttp.Tests/RFC9110/22_FramingCompletenessTests.cs` — Message completeness detection across protocol versions

### Known Gaps
- None
