---
title: 4.1.  Calculating Cache Keys with the Vary Header Field
rfc_number: 9111
rfc_section: '4.1'
source_url: 'https://www.rfc-editor.org/rfc/rfc9111'
description: >-
  Section 4.1: Calculating Cache Keys with the Vary Header Field — RFC 9111 —
  HTTP Caching
tags:
  - RFC9111
  - HTTP-caching
  - freshness
  - validation
  - Cache-Control
  - max-age
  - Expires
  - conditional-requests
  - Vary
  - calculating_cache_keys_with_the_vary_header_field
---

## 4.1.  Calculating Cache Keys with the Vary Header Field

4.  Constructing Responses from Caches

> **MUST NOT**: When presented with a request, a cache MUST NOT reuse a stored
   response unless:

   *  the presented target URI (Section 7.1 of [HTTP]) and that of the
      stored response match, and

   *  the request method associated with the stored response allows it
      to be used for the presented request, and

   *  request header fields nominated by the stored response (if any)
      match those presented (see Section 4.1), and

   *  the stored response does not contain the no-cache directive
      (Section 5.2.2.4), unless it is successfully validated
      (Section 4.3), and

   *  the stored response is one of the following:

      -  fresh (see Section 4.2), or

      -  allowed to be served stale (see Section 4.2.4), or

      -  successfully validated (see Section 4.3).

   Note that a cache extension can override any of the requirements
   listed; see Section 5.2.3.

   When a stored response is used to satisfy a request without
> **MUST**: validation, a cache MUST generate an Age header field (Section 5.1),
   replacing any present in the response with a value equal to the
   stored response's current_age; see Section 4.2.3.

> **MUST**: A cache MUST write through requests with methods that are unsafe
   (Section 9.2.1 of [HTTP]) to the origin server; i.e., a cache is not
   allowed to generate a reply to such a request before having forwarded
   the request and having received a corresponding response.

   Also, note that unsafe requests might invalidate already-stored
   responses; see Section 4.4.

   A cache can use a response that is stored or storable to satisfy
   multiple requests, provided that it is allowed to reuse that response
   for the requests in question.  This enables a cache to "collapse
   requests" -- or combine multiple incoming requests into a single
   forward request upon a cache miss -- thereby reducing load on the
   origin server and network.  Note, however, that if the cache cannot
   use the returned response for some or all of the collapsed requests,
   it will need to forward the requests in order to satisfy them,
   potentially introducing additional latency.

> **MUST**: When more than one suitable response is stored, a cache MUST use the
   most recent one (as determined by the Date header field).  It can
   also forward the request with "Cache-Control: max-age=0" or "Cache-
   Control: no-cache" to disambiguate which response to use.

> **MUST**: A cache without a clock (Section 5.6.7 of [HTTP]) MUST revalidate
   stored responses upon every use.

## 4.1  Calculating Cache Keys with the Vary Header Field

   When a cache receives a request that can be satisfied by a stored
   response and that stored response contains a Vary header field
> **MUST NOT**: (Section 12.5.5 of [HTTP]), the cache MUST NOT use that stored
   response without revalidation unless all the presented request header
   fields nominated by that Vary field value match those fields in the
   original request (i.e., the request that caused the cached response
   to be stored).

   The header fields from two requests are defined to match if and only
   if those in the first request can be transformed to those in the
   second request by applying any of the following:

   *  adding or removing whitespace, where allowed in the header field's
      syntax

   *  combining multiple header field lines with the same field name
      (see Section 5.2 of [HTTP])

   *  normalizing both header field values in a way that is known to
      have identical semantics, according to the header field's
      specification (e.g., reordering field values when order is not
      significant; case-normalization, where values are defined to be
      case-insensitive)

   If (after any normalization that might take place) a header field is
   absent from a request, it can only match another request if it is
   also absent there.

   A stored response with a Vary header field value containing a member
   "*" always fails to match.

   If multiple stored responses match, the cache will need to choose one
   to use.  When a nominated request header field has a known mechanism
   for ranking preference (e.g., qvalues on Accept and similar request
> **MAY**: header fields), that mechanism MAY be used to choose a preferred
   response.  If such a mechanism is not available, or leads to equally
   preferred responses, the most recent response (as determined by the
   Date header field) is chosen, as per Section 4.

   Some resources mistakenly omit the Vary header field from their
   default response (i.e., the one sent when the request does not
   express any preferences), with the effect of choosing it for
   subsequent requests to that resource even when more preferable
   responses are available.  When a cache has multiple stored responses
   for a target URI and one or more omits the Vary header field, the
> **SHOULD**: cache SHOULD choose the most recent (see Section 4.2.3) stored
   response with a valid Vary field value.

   If no stored response matches, the cache cannot satisfy the presented
   request.  Typically, the request is forwarded to the origin server,
   potentially with preconditions added to describe what responses the
   cache has already stored (Section 4.3).


---

## TurboHttp Compliance

**Status:** ❌ Missing

**Implementation Notes:**
TurboHttp does not compute cache keys or process the Vary header field for cache selection purposes. The Vary header is passed through in responses but not used for any storage or retrieval logic.

**Key Gaps:**
- No cache key computation from effective request URI
- No Vary-based secondary key selection
- No `Vary: *` handling
- No stored response matching against request header fields

**Affected Components:** None

**Test References:** None
