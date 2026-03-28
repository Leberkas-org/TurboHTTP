---
title: 3.  Storing Responses in Caches
rfc_number: 9111
rfc_section: '3'
source_url: 'https://www.rfc-editor.org/rfc/rfc9111'
description: 'Section 3: Storing Responses in Caches — RFC 9111 — HTTP Caching'
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
  - storing_responses_in_caches
---

## 3.  Storing Responses in Caches

3.  Storing Responses in Caches

> **MUST NOT**: A cache MUST NOT store a response to a request unless:

   *  the request method is understood by the cache;

   *  the response status code is final (see Section 15 of [HTTP]);

   *  if the response status code is 206 or 304, or the must-understand
      cache directive (see Section 5.2.2.3) is present: the cache
      understands the response status code;

   *  the no-store cache directive is not present in the response (see
      Section 5.2.2.5);

   *  if the cache is shared: the private response directive is either
      not present or allows a shared cache to store a modified response;
      see Section 5.2.2.7);

   *  if the cache is shared: the Authorization header field is not
      present in the request (see Section 11.6.2 of [HTTP]) or a
      response directive is present that explicitly allows shared
      caching (see Section 3.5); and

   *  the response contains at least one of the following:

      -  a public response directive (see Section 5.2.2.9);

      -  a private response directive, if the cache is not shared (see
         Section 5.2.2.7);

      -  an Expires header field (see Section 5.3);

      -  a max-age response directive (see Section 5.2.2.1);

      -  if the cache is shared: an s-maxage response directive (see
         Section 5.2.2.10);

      -  a cache extension that allows it to be cached (see
         Section 5.2.3); or

      -  a status code that is defined as heuristically cacheable (see
         Section 4.2.2).

   Note that a cache extension can override any of the requirements
   listed; see Section 5.2.3.

   In this context, a cache has "understood" a request method or a
   response status code if it recognizes it and implements all specified
   caching-related behavior.

   Note that, in normal operation, some caches will not store a response
   that has neither a cache validator nor an explicit expiration time,
   as such responses are not usually useful to store.  However, caches
   are not prohibited from storing such responses.

## 3.1  Storing Header and Trailer Fields

> **MUST**: Caches MUST include all received response header fields -- including
   unrecognized ones -- when storing a response; this assures that new
   HTTP header fields can be successfully deployed.  However, the
   following exceptions are made:

   *  The Connection header field and fields whose names are listed in
      it are required by Section 7.6.1 of [HTTP] to be removed before
> **MAY**: forwarding the message.  This MAY be implemented by doing so
      before storage.

   *  Likewise, some fields' semantics require them to be removed before
> **MAY**: forwarding the message, and this MAY be implemented by doing so
      before storage; see Section 7.6.1 of [HTTP] for some examples.

   *  The no-cache (Section 5.2.2.4) and private (Section 5.2.2.7) cache
      directives can have arguments that prevent storage of header
      fields by all caches and shared caches, respectively.

   *  Header fields that are specific to the proxy that a cache uses
> **MUST NOT**: when forwarding a request MUST NOT be stored, unless the cache
      incorporates the identity of the proxy into the cache key.
      Effectively, this is limited to Proxy-Authenticate (Section 11.7.1
      of [HTTP]), Proxy-Authentication-Info (Section 11.7.3 of [HTTP]),
      and Proxy-Authorization (Section 11.7.2 of [HTTP]).

> **MAY**: Caches MAY either store trailer fields separate from header fields or
   discard them.  Caches MUST NOT combine trailer fields with header
   fields.

## 3.2  Updating Stored Header Fields

   Caches are required to update a stored response's header fields from
   another (typically newer) response in several situations; for
   example, see Sections 3.4, 4.3.4, and 4.3.5.

> **MUST**: When doing so, the cache MUST add each header field in the provided
   response to the stored response, replacing field values that are
   already present, with the following exceptions:

   *  Header fields excepted from storage in Section 3.1,

   *  Header fields that the cache's stored response depends upon, as
      described below,

   *  Header fields that are automatically processed and removed by the
      recipient, as described below, and

   *  The Content-Length header field.

   In some cases, caches (especially in user agents) store the results
   of processing the received response, rather than the response itself,
   and updating header fields that affect that processing can result in
   inconsistent behavior and security issues.  Caches in this situation
> **MAY**: MAY omit these header fields from updating stored responses on an
   exceptional basis but SHOULD limit such omission to those fields
   necessary to assure integrity of the stored response.

   For example, a browser might decode the content coding of a response
   while it is being received, creating a disconnect between the data it
   has stored and the response's original metadata.  Updating that
   stored metadata with a different Content-Encoding header field would
   be problematic.  Likewise, a browser might store a post-parse HTML
   tree rather than the content received in the response; updating the
   Content-Type header field would not be workable in this case because
   any assumptions about the format made in parsing would now be
   invalid.

   Furthermore, some fields are automatically processed and removed by
   the HTTP implementation, such as the Content-Range header field.
> **MAY**: Implementations MAY automatically omit such header fields from
   updates, even when the processing does not actually occur.

   Note that the Content-* prefix is not a signal that a header field is
   omitted from update; it is a convention for MIME header fields, not
   HTTP.

## 3.3  Storing Incomplete Responses

   If the request method is GET, the response status code is 200 (OK),
> **MAY**: and the entire response header section has been received, a cache MAY
   store a response that is not complete (Section 6.1 of [HTTP])
   provided that the stored response is recorded as being incomplete.
> **MAY**: Likewise, a 206 (Partial Content) response MAY be stored as if it
   were an incomplete 200 (OK) response.  However, a cache MUST NOT
   store incomplete or partial-content responses if it does not support
   the Range and Content-Range header fields or if it does not
   understand the range units used in those fields.

> **MAY**: A cache MAY complete a stored incomplete response by making a
   subsequent range request (Section 14.2 of [HTTP]) and combining the
   successful response with the stored response, as defined in
> **MUST NOT**: Section 3.4.  A cache MUST NOT use an incomplete response to answer
   requests unless the response has been made complete, or the request
   is partial and specifies a range wholly within the incomplete
> **MUST NOT**: response.  A cache MUST NOT send a partial response to a client
   without explicitly marking it using the 206 (Partial Content) status
   code.

## 3.4  Combining Partial Content

   A response might transfer only a partial representation if the
   connection closed prematurely or if the request used one or more
   Range specifiers (Section 14.2 of [HTTP]).  After several such
   transfers, a cache might have received several ranges of the same
> **MAY**: representation.  A cache MAY combine these ranges into a single
   stored response, and reuse that response to satisfy later requests,
   if they all share the same strong validator and the cache complies
   with the client requirements in Section 15.3.7.3 of [HTTP].

   When combining the new response with one or more stored responses, a
> **MUST**: cache MUST update the stored response header fields using the header
   fields provided in the new response, as per Section 3.2.

## 3.5  Storing Responses to Authenticated Requests

> **MUST NOT**: A shared cache MUST NOT use a cached response to a request with an
   Authorization header field (Section 11.6.2 of [HTTP]) to satisfy any
   subsequent request unless the response contains a Cache-Control field
   with a response directive (Section 5.2.2) that allows it to be stored
   by a shared cache, and the cache conforms to the requirements of that
   directive for that response.

   In this specification, the following response directives have such an
   effect: must-revalidate (Section 5.2.2.2), public (Section 5.2.2.9),
   and s-maxage (Section 5.2.2.10).


---

## TurboHttp Compliance

**Status:** ❌ Missing

**Implementation Notes:**
TurboHttp does not store responses in any cache. No logic exists to evaluate whether a response is cacheable based on request method, status code, or Cache-Control directives. All responses are passed directly to the caller without storage consideration.

**Key Gaps:**
- No response storage mechanism
- No evaluation of `no-store`, `private`, or `Authorization` constraints
- No incomplete response handling for caching purposes
- No `s-maxage` or shared cache directive processing

**Affected Components:** None

**Test References:** None

---

**Navigation:** [[../RFC9111|RFC9111 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
