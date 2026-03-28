---
title: "15.1.  Overview of Status Codes"
rfc_number: 9110
rfc_section: "15.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 15.1: Overview of Status Codes — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, overview_of_status_codes]
---

## 15.1.  Overview of Status Codes

15.  Status Codes

   The status code of a response is a three-digit integer code that
   describes the result of the request and the semantics of the
   response, including whether the request was successful and what
   content is enclosed (if any).  All valid status codes are within the
   range of 100 to 599, inclusive.

   The first digit of the status code defines the class of response.
   The last two digits do not have any categorization role.  There are
   five values for the first digit:

   *  1xx (Informational): The request was received, continuing process

   *  2xx (Successful): The request was successfully received,
      understood, and accepted

   *  3xx (Redirection): Further action needs to be taken in order to
      complete the request

   *  4xx (Client Error): The request contains bad syntax or cannot be
      fulfilled

   *  5xx (Server Error): The server failed to fulfill an apparently
      valid request

   HTTP status codes are extensible.  A client is not required to
   understand the meaning of all registered status codes, though such
> **MUST**: understanding is obviously desirable.  However, a client MUST
   understand the class of any status code, as indicated by the first
   digit, and treat an unrecognized status code as being equivalent to
   the x00 status code of that class.

   For example, if a client receives an unrecognized status code of 471,
   it can see from the first digit that there was something wrong with
   its request and treat the response as if it had received a 400 (Bad
   Request) status code.  The response message will usually contain a
   representation that explains the status.

   Values outside the range 100..599 are invalid.  Implementations often
   use three-digit integer values outside of that range (i.e., 600..999)
   for internal communication of non-HTTP status (e.g., library errors).
> **SHOULD**: A client that receives a response with an invalid status code SHOULD
   process the response as if it had a 5xx (Server Error) status code.

   A single request can have multiple associated responses: zero or more
   "interim" (non-final) responses with status codes in the
   "informational" (1xx) range, followed by exactly one "final" response
   with a status code in one of the other ranges.

## 15.1  Overview of Status Codes

   The status codes listed below are defined in this specification.  The
   reason phrases listed here are only recommendations -- they can be
   replaced by local equivalents or left out altogether without
   affecting the protocol.

   Responses with status codes that are defined as heuristically
   cacheable (e.g., 200, 203, 204, 206, 300, 301, 308, 404, 405, 410,
   414, and 501 in this specification) can be reused by a cache with
   heuristic expiration unless otherwise indicated by the method
   definition or explicit cache controls [CACHING]; all other status
   codes are not heuristically cacheable.

   Additional status codes, outside the scope of this specification,
   have been specified for use in HTTP.  All such status codes ought to
   be registered within the "Hypertext Transfer Protocol (HTTP) Status
   Code Registry", as described in Section 16.2.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`HttpStatusCode.cs`** — Enum covering all standard status codes (100–599); unrecognized codes treated as x00 equivalent per §15.1 MUST requirement
- **`HttpResponseDecoder.cs`** — Parses three-digit status codes; rejects values outside 100–599 range
- **`StatusCodeClassification.cs`** — Classifies by first digit: informational, successful, redirection, client error, server error; handles interim (1xx) vs final responses

### Test References
- `TurboHttp.Tests/RFC9110/75_StatusCodeTests.cs` — Status code parsing, class-based fallback, invalid code handling

### Known Gaps
- None
