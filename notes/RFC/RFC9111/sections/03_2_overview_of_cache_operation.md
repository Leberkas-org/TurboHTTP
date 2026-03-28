---
title: 2.  Overview of Cache Operation
rfc_number: 9111
rfc_section: '2'
source_url: 'https://www.rfc-editor.org/rfc/rfc9111'
description: 'Section 2: Overview of Cache Operation — RFC 9111 — HTTP Caching'
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
  - overview_of_cache_operation
---

## 2.  Overview of Cache Operation

2.  Overview of Cache Operation

   Proper cache operation preserves the semantics of HTTP transfers
   while reducing the transmission of information already held in the
   cache.  See Section 3 of [HTTP] for the general terminology and core
   concepts of HTTP.

   Although caching is an entirely OPTIONAL feature of HTTP, it can be
   assumed that reusing a cached response is desirable and that such
   reuse is the default behavior when no requirement or local
   configuration prevents it.  Therefore, HTTP cache requirements are
   focused on preventing a cache from either storing a non-reusable
   response or reusing a stored response inappropriately, rather than
   mandating that caches always store and reuse particular responses.

   The "cache key" is the information a cache uses to choose a response
   and is composed from, at a minimum, the request method and target URI
   used to retrieve the stored response; the method determines under
   which circumstances that response can be used to satisfy a subsequent
   request.  However, many HTTP caches in common use today only cache
   GET responses and therefore only use the URI as the cache key.

   A cache might store multiple responses for a request target that is
   subject to content negotiation.  Caches differentiate these responses
   by incorporating some of the original request's header fields into
   the cache key as well, using information in the Vary response header
   field, as per Section 4.1.

   Caches might incorporate additional material into the cache key.  For
   example, user agent caches might include the referring site's
   identity, thereby "double keying" the cache to avoid some privacy
   risks (see Section 7.2).

   Most commonly, caches store the successful result of a retrieval
   request: i.e., a 200 (OK) response to a GET request, which contains a
   representation of the target resource (Section 9.3.1 of [HTTP]).
   However, it is also possible to store redirects, negative results
   (e.g., 404 (Not Found)), incomplete results (e.g., 206 (Partial
   Content)), and responses to methods other than GET if the method's
   definition allows such caching and defines something suitable for use
   as a cache key.

   A cache is "disconnected" when it cannot contact the origin server or
   otherwise find a forward path for a request.  A disconnected cache
   can serve stale responses in some circumstances (Section 4.2.4).


---

## TurboHttp Compliance

**Status:** ❌ Missing

**Implementation Notes:**
TurboHttp does not implement an HTTP cache. The client library forwards all requests directly to the origin server without any cache lookup, storage, or revalidation logic. CacheLookupStage is planned as a future pipeline stage but not yet implemented.

**Key Gaps:**
- No cache storage or retrieval mechanism
- No freshness evaluation or expiration logic
- No private vs shared cache distinction
- No understanding of cacheable methods or status codes
- No response reuse logic

**Affected Components:** None (no caching components exist)

**Test References:** None
