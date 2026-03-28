---
title: 6.4.  Content
rfc_number: 9110
rfc_section: '6.4'
source_url: 'https://www.rfc-editor.org/rfc/rfc9110'
description: 'Section 6.4: Content — RFC 9110 — HTTP Semantics'
tags:
  - RFC9110
  - HTTP-semantics
  - methods
  - status-codes
  - redirects
  - retries
  - content-negotiation
  - conditional-requests
---

## 6.4.  Content

## 6.4  Content

   HTTP messages often transfer a complete or partial representation as
   the message "content": a stream of octets sent after the header
   section, as delineated by the message framing.

   This abstract definition of content reflects the data after it has
   been extracted from the message framing.  For example, an HTTP/1.1
   message body (Section 6 of [HTTP/1.1]) might consist of a stream of
   data encoded with the chunked transfer coding -- a sequence of data
   chunks, one zero-length chunk, and a trailer section -- whereas the
   content of that same message includes only the data stream after the
   transfer coding has been decoded; it does not include the chunk
   lengths, chunked framing syntax, nor the trailer fields
   (Section 6.5).

      |  *Note:* Some field names have a "Content-" prefix.  This is an
      |  informal convention; while some of these fields refer to the
      |  content of the message, as defined above, others are scoped to
      |  the selected representation (Section 3.2).  See the individual
      |  field's definition to disambiguate.

### 6.4.1  Content Semantics

   The purpose of content in a request is defined by the method
   semantics (Section 9).

   For example, a representation in the content of a PUT request
   (Section 9.3.4) represents the desired state of the target resource
   after the request is successfully applied, whereas a representation
   in the content of a POST request (Section 9.3.3) represents
   information to be processed by the target resource.

   In a response, the content's purpose is defined by the request
   method, response status code (Section 15), and response fields
   describing that content.  For example, the content of a 200 (OK)
   response to GET (Section 9.3.1) represents the current state of the
   target resource, as observed at the time of the message origination
   date (Section 6.6.1), whereas the content of the same status code in
   a response to POST might represent either the processing result or
   the new state of the target resource after applying the processing.

   The content of a 206 (Partial Content) response to GET contains
   either a single part of the selected representation or a multipart
   message body containing multiple parts of that representation, as
   described in Section 15.3.7.

   Response messages with an error status code usually contain content
   that represents the error condition, such that the content describes
   the error state and what steps are suggested for resolving it.

   Responses to the HEAD request method (Section 9.3.2) never include
   content; the associated response header fields indicate only what
   their values would have been if the request method had been GET
   (Section 9.3.1).

   2xx (Successful) responses to a CONNECT request method
   (Section 9.3.6) switch the connection to tunnel mode instead of
   having content.

   All 1xx (Informational), 204 (No Content), and 304 (Not Modified)
   responses do not include content.

   All other responses do include content, although that content might
   be of zero length.

### 6.4.2  Identifying Content

   When a complete or partial representation is transferred as message
   content, it is often desirable for the sender to supply, or the
   recipient to determine, an identifier for a resource corresponding to
   that specific representation.  For example, a client making a GET
   request on a resource for "the current weather report" might want an
   identifier specific to the content returned (e.g., "weather report
   for Laguna Beach at 20210720T1711").  This can be useful for sharing
   or bookmarking content from resources that are expected to have
   changing representations over time.

   For a request message:

   *  If the request has a Content-Location header field, then the
      sender asserts that the content is a representation of the
      resource identified by the Content-Location field value.  However,
      such an assertion cannot be trusted unless it can be verified by
      other means (not defined by this specification).  The information
      might still be useful for revision history links.

   *  Otherwise, the content is unidentified by HTTP, but a more
      specific identifier might be supplied within the content itself.

   For a response message, the following rules are applied in order
   until a match is found:

   1.  If the request method is HEAD or the response status code is 204
       (No Content) or 304 (Not Modified), there is no content in the
       response.

   2.  If the request method is GET and the response status code is 200
       (OK), the content is a representation of the target resource
       (Section 7.1).

   3.  If the request method is GET and the response status code is 203
       (Non-Authoritative Information), the content is a potentially
       modified or enhanced representation of the target resource as
       provided by an intermediary.

   4.  If the request method is GET and the response status code is 206
       (Partial Content), the content is one or more parts of a
       representation of the target resource.

   5.  If the response has a Content-Location header field and its field
       value is a reference to the same URI as the target URI, the
       content is a representation of the target resource.

   6.  If the response has a Content-Location header field and its field
       value is a reference to a URI different from the target URI, then
       the sender asserts that the content is a representation of the
       resource identified by the Content-Location field value.
       However, such an assertion cannot be trusted unless it can be
       verified by other means (not defined by this specification).

   7.  Otherwise, the content is unidentified by HTTP, but a more
       specific identifier might be supplied within the content itself.


---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`HttpResponseDecoder.cs`** — Extracts content from message framing; handles zero-length content for 204/304 responses per §6.4.1
- **`ContentDecodingStage.cs`** — Decodes content after extracting from framing layer; supports streaming content delivery
- **`Http11ResponseDecoder.cs`** — Handles chunked transfer coding extraction to produce raw content stream
- **`ContentIdentification.cs`** — Applies §6.4.2 rules for identifying content via Content-Location and request method

### Test References
- `TurboHttp.Tests/RFC9110/25_ContentTests.cs` — Content semantics, zero-length bodies, HEAD response handling

### Known Gaps
- None

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
