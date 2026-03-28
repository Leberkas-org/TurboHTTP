---
title: 4.4.  Invalidating Stored Responses
rfc_number: 9111
rfc_section: '4.4'
source_url: 'https://www.rfc-editor.org/rfc/rfc9111'
description: 'Section 4.4: Invalidating Stored Responses — RFC 9111 — HTTP Caching'
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
  - invalidating_stored_responses
---

## 4.4.  Invalidating Stored Responses

## 4.4  Invalidating Stored Responses

   Because unsafe request methods (Section 9.2.1 of [HTTP]) such as PUT,
   POST, or DELETE have the potential for changing state on the origin
   server, intervening caches are required to invalidate stored
   responses to keep their contents up to date.

> **MUST**: A cache MUST invalidate the target URI (Section 7.1 of [HTTP]) when
   it receives a non-error status code in response to an unsafe request
   method (including methods whose safety is unknown).

> **MAY**: A cache MAY invalidate other URIs when it receives a non-error status
   code in response to an unsafe request method (including methods whose
   safety is unknown).  In particular, the URI(s) in the Location and
   Content-Location response header fields (if present) are candidates
   for invalidation; other URIs might be discovered through mechanisms
> **MUST NOT**: not specified in this document.  However, a cache MUST NOT trigger an
   invalidation under these conditions if the origin (Section 4.3.1 of
   [HTTP]) of the URI to be invalidated differs from that of the target
   URI (Section 7.1 of [HTTP]).  This helps prevent denial-of-service
   attacks.

   "Invalidate" means that the cache will either remove all stored
   responses whose target URI matches the given URI or mark them as
   "invalid" and in need of a mandatory validation before they can be
   sent in response to a subsequent request.

   A "non-error response" is one with a 2xx (Successful) or 3xx
   (Redirection) status code.

   Note that this does not guarantee that all appropriate responses are
   invalidated globally; a state-changing request would only invalidate
   responses in the caches it travels through.


---

## TurboHttp Compliance

**Status:** ❌ Missing

**Implementation Notes:**
TurboHttp does not implement cache invalidation. No logic exists to invalidate stored responses after successful unsafe method requests (POST, PUT, DELETE). Since no cache storage exists, there is nothing to invalidate.

**Key Gaps:**
- No invalidation triggered by unsafe methods
- No invalidation based on Location/Content-Location headers
- No protection against invalidation from non-trustworthy sources

**Affected Components:** None

**Test References:** None

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
