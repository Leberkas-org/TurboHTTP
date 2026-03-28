---
title: "2.  Otherwise, the condition is true."
rfc_number: 9110
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 2: Otherwise, the condition is true. — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, otherwise_the_condition_is_true]
---

## 2.  Otherwise, the condition is true.

   2.  Otherwise, the condition is true.

> **SHOULD**: An origin server that evaluates an If-Modified-Since condition SHOULD
   NOT perform the requested method if the condition evaluates to false;
> **SHOULD**: instead, the origin server SHOULD generate a 304 (Not Modified)
   response, including only those metadata that are useful for
   identifying or updating a previously cached response.

   Requirements on cache handling of a received If-Modified-Since header
   field are defined in Section 4.3.2 of [CACHING].

### 13.1.4  If-Unmodified-Since

   The "If-Unmodified-Since" header field makes the request method
   conditional on the selected representation's last modification date
   being earlier than or equal to the date provided in the field value.
   This field accomplishes the same purpose as If-Match for cases where
   the user agent does not have an entity tag for the representation.


```abnf
     If-Unmodified-Since = HTTP-date
```


   An example of the field is:

   If-Unmodified-Since: Sat, 29 Oct 1994 19:43:31 GMT

> **MUST**: A recipient MUST ignore If-Unmodified-Since if the request contains
   an If-Match header field; the condition in If-Match is considered to
   be a more accurate replacement for the condition in If-Unmodified-
   Since, and the two are only combined for the sake of interoperating
   with older intermediaries that might not implement If-Match.

> **MUST**: A recipient MUST ignore the If-Unmodified-Since header field if the
   received field value is not a valid HTTP-date (including when the
   field value appears to be a list of dates).

> **MUST**: A recipient MUST ignore the If-Unmodified-Since header field if the
   resource does not have a modification date available.

> **MUST**: A recipient MUST interpret an If-Unmodified-Since field value's
   timestamp in terms of the origin server's clock.

   If-Unmodified-Since is most often used with state-changing methods
   (e.g., POST, PUT, DELETE) to prevent accidental overwrites when
   multiple user agents might be acting in parallel on a resource that
   does not supply entity tags with its representations (i.e., to
   prevent the "lost update" problem).  In general, it can be used with
   any method that involves the selection or modification of a
   representation to abort the request if the selected representation's
   last modification date has changed since the date provided in the If-
   Unmodified-Since field value.

   When an origin server receives a request that selects a
   representation and that request includes an If-Unmodified-Since
> **MUST**: header field without an If-Match header field, the origin server MUST
   evaluate the If-Unmodified-Since condition per Section 13.2 prior to
   performing the method.

   To evaluate a received If-Unmodified-Since header field:

   1.  If the selected representation's last modification date is
       earlier than or equal to the date provided in the field value,
       the condition is true.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
