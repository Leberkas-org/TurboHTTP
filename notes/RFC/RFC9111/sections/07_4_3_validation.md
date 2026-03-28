---
title: 4.3.  Validation
rfc_number: 9111
rfc_section: '4.3'
source_url: 'https://www.rfc-editor.org/rfc/rfc9111'
description: 'Section 4.3: Validation — RFC 9111 — HTTP Caching'
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
---

## 4.3.  Validation

## 4.3  Validation

   When a cache has one or more stored responses for a requested URI,
   but cannot serve any of them (e.g., because they are not fresh, or
   one cannot be chosen; see Section 4.1), it can use the conditional
   request mechanism (Section 13 of [HTTP]) in the forwarded request to
   give the next inbound server an opportunity to choose a valid stored
   response to use, updating the stored metadata in the process, or to
   replace the stored response(s) with a new response.  This process is
   known as "validating" or "revalidating" the stored response.

### 4.3.1  Sending a Validation Request

   When generating a conditional request for validation, a cache either
   starts with a request it is attempting to satisfy or -- if it is
   initiating the request independently -- synthesizes a request using a
   stored response by copying the method, target URI, and request header
   fields identified by the Vary header field (Section 4.1).

   It then updates that request with one or more precondition header
   fields.  These contain validator metadata sourced from a stored
   response(s) that has the same URI.  Typically, this will include only
   the stored response(s) that has the same cache key, although a cache
   is allowed to validate a response that it cannot choose with the
   request header fields it is sending (see Section 4.1).

   The precondition header fields are then compared by recipients to
   determine whether any stored response is equivalent to a current
   representation of the resource.

   One such validator is the timestamp given in a Last-Modified header
   field (Section 8.8.2 of [HTTP]), which can be used in an If-Modified-
   Since header field for response validation, or in an If-Unmodified-
   Since or If-Range header field for representation selection (i.e.,
   the client is referring specifically to a previously obtained
   representation with that timestamp).

   Another validator is the entity tag given in an ETag field
   (Section 8.8.3 of [HTTP]).  One or more entity tags, indicating one
   or more stored responses, can be used in an If-None-Match header
   field for response validation, or in an If-Match or If-Range header
   field for representation selection (i.e., the client is referring
   specifically to one or more previously obtained representations with
   the listed entity tags).

   When generating a conditional request for validation, a cache:

> **MUST**: *  MUST send the relevant entity tags (using If-Match, If-None-Match,
      or If-Range) if the entity tags were provided in the stored
      response(s) being validated.

> **SHOULD**: *  SHOULD send the Last-Modified value (using If-Modified-Since) if
      the request is not for a subrange, a single stored response is
      being validated, and that response contains a Last-Modified value.

> **MAY**: *  MAY send the Last-Modified value (using If-Unmodified-Since or If-
      Range) if the request is for a subrange, a single stored response
      is being validated, and that response contains only a Last-
      Modified value (not an entity tag).

   In most cases, both validators are generated in cache validation
   requests, even when entity tags are clearly superior, to allow old
   intermediaries that do not understand entity tag preconditions to
   respond appropriately.

### 4.3.2  Handling a Received Validation Request

   Each client in the request chain may have its own cache, so it is
   common for a cache at an intermediary to receive conditional requests
   from other (outbound) caches.  Likewise, some user agents make use of
   conditional requests to limit data transfers to recently modified
   representations or to complete the transfer of a partially retrieved
   representation.

   If a cache receives a request that can be satisfied by reusing a
   stored 200 (OK) or 206 (Partial Content) response, as per Section 4,
> **SHOULD**: the cache SHOULD evaluate any applicable conditional header field
   preconditions received in that request with respect to the
   corresponding validators contained within the stored response.

> **MUST NOT**: A cache MUST NOT evaluate conditional header fields that only apply
   to an origin server, occur in a request with semantics that cannot be
   satisfied with a cached response, or occur in a request with a target
   resource for which it has no stored responses; such preconditions are
   likely intended for some other (inbound) server.

   The proper evaluation of conditional requests by a cache depends on
   the received precondition header fields and their precedence.  In
   summary, the If-Match and If-Unmodified-Since conditional header
   fields are not applicable to a cache, and If-None-Match takes
   precedence over If-Modified-Since.  See Section 13.2.2 of [HTTP] for
   a complete specification of precondition precedence.

   A request containing an If-None-Match header field (Section 13.1.2 of
   [HTTP]) indicates that the client wants to validate one or more of
   its own stored responses in comparison to the stored response chosen
   by the cache (as per Section 4).

   If an If-None-Match header field is not present, a request containing
   an If-Modified-Since header field (Section 13.1.3 of [HTTP])
   indicates that the client wants to validate one or more of its own
   stored responses by modification date.

   If a request contains an If-Modified-Since header field and the Last-
   Modified header field is not present in a stored response, a cache
> **SHOULD**: SHOULD use the stored response's Date field value (or, if no Date
   field is present, the time that the stored response was received) to
   evaluate the conditional.

   A cache that implements partial responses to range requests, as
   defined in Section 14.2 of [HTTP], also needs to evaluate a received
   If-Range header field (Section 13.1.5 of [HTTP]) with respect to the
   cache's chosen response.

   When a cache decides to forward a request to revalidate its own
   stored responses for a request that contains an If-None-Match list of
> **MAY**: entity tags, the cache MAY combine the received list with a list of
   entity tags from its own stored set of responses (fresh or stale) and
   send the union of the two lists as a replacement If-None-Match header
   field value in the forwarded request.  If a stored response contains
> **MUST NOT**: only partial content, the cache MUST NOT include its entity tag in
   the union unless the request is for a range that would be fully
   satisfied by that partial stored response.  If the response to the
   forwarded request is 304 (Not Modified) and has an ETag field value
> **MUST**: with an entity tag that is not in the client's list, the cache MUST
   generate a 200 (OK) response for the client by reusing its
   corresponding stored response, as updated by the 304 response
   metadata (Section 4.3.4).

### 4.3.3  Handling a Validation Response

   Cache handling of a response to a conditional request depends upon
   its status code:

   *  A 304 (Not Modified) response status code indicates that the
      stored response can be updated and reused; see Section 4.3.4.

   *  A full response (i.e., one containing content) indicates that none
      of the stored responses nominated in the conditional request are
> **MUST**: suitable.  Instead, the cache MUST use the full response to
   satisfy the request.  The cache MAY store such a full response,
      subject to its constraints (see Section 3).

   *  However, if a cache receives a 5xx (Server Error) response while
      attempting to validate a response, it can either forward this
      response to the requesting client or act as if the server failed
      to respond.  In the latter case, the cache can send a previously
      stored response, subject to its constraints on doing so (see
      Section 4.2.4), or retry the validation request.

### 4.3.4  Freshening Stored Responses upon Validation

   When a cache receives a 304 (Not Modified) response, it needs to
   identify stored responses that are suitable for updating with the new
   information provided, and then do so.

   The initial set of stored responses to update are those that could
   have been chosen for that request -- i.e., those that meet the
   requirements in Section 4, except the last requirement to be fresh,
   able to be served stale, or just validated.

   Then, that initial set of stored responses is further filtered by the
   first match of:

   *  If the new response contains one or more "strong validators" (see
      Section 8.8.1 of [HTTP]), then each of those strong validators
      identifies a selected representation for update.  All the stored
      responses in the initial set with one of those same strong
      validators are identified for update.  If none of the initial set
      contains at least one of the same strong validators, then the
> **MUST NOT**: cache MUST NOT use the new response to update any stored
      responses.

   *  If the new response contains no strong validators but does contain
      one or more "weak validators", and those validators correspond to
      one of the initial set's stored responses, then the most recent of
      those matching stored responses is identified for update.

   *  If the new response does not include any form of validator (such
      as where a client generates an If-Modified-Since request from a
      source other than the Last-Modified response header field), and
      there is only one stored response in the initial set, and that
      stored response also lacks a validator, then that stored response
      is identified for update.

> **MUST**: For each stored response identified, the cache MUST update its header
   fields with the header fields provided in the 304 (Not Modified)
   response, as per Section 3.2.

### 4.3.5  Freshening Responses with HEAD

   A response to the HEAD method is identical to what an equivalent
   request made with a GET would have been, without sending the content.
   This property of HEAD responses can be used to invalidate or update a
   cached GET response if the more efficient conditional GET request
   mechanism is not available (due to no validators being present in the
   stored response) or if transmission of the content is not desired
   even if it has changed.

   When a cache makes an inbound HEAD request for a target URI and
> **SHOULD**: receives a 200 (OK) response, the cache SHOULD update or invalidate
   each of its stored GET responses that could have been chosen for that
   request (see Section 4.1).

   For each of the stored responses that could have been chosen, if the
   stored response and HEAD response have matching values for any
   received validator fields (ETag and Last-Modified) and, if the HEAD
   response has a Content-Length header field, the value of Content-
> **SHOULD**: Length matches that of the stored response, the cache SHOULD update
   the stored response as described below; otherwise, the cache SHOULD
   consider the stored response to be stale.

   If a cache updates a stored response with the metadata provided in a
> **MUST**: HEAD response, the cache MUST use the header fields provided in the
   HEAD response to update the stored response (see Section 3.2).


---

## TurboHttp Compliance

**Status:** ❌ Missing

**Implementation Notes:**
TurboHttp does not perform cache validation. No conditional request generation (If-None-Match, If-Modified-Since) for cache revalidation exists. The client does not send conditional requests automatically to revalidate stale cached responses, nor does it process 304 (Not Modified) responses for cache update purposes.

**Key Gaps:**
- No conditional request generation for revalidation
- No 304 response handling for cache freshening
- No ETag/Last-Modified based validator comparison
- No HEAD request cache update logic
- No handling of partial 200 responses invalidating partial cache entries

**Affected Components:** None

**Test References:** None
