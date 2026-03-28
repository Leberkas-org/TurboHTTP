---
title: "3.  Otherwise, the condition is true."
rfc_number: 9110
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3: Otherwise, the condition is true. — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, otherwise_the_condition_is_true]
---

## 3.  Otherwise, the condition is true.

   3.  Otherwise, the condition is true.

> **MUST NOT**: An origin server that evaluates an If-None-Match condition MUST NOT
   perform the requested method if the condition evaluates to false;
> **MUST**: instead, the origin server MUST respond with either a) the 304 (Not
   Modified) status code if the request method is GET or HEAD or b) the
   412 (Precondition Failed) status code for all other request methods.

   Requirements on cache handling of a received If-None-Match header
   field are defined in Section 4.3.2 of [CACHING].

   Note that an If-None-Match header field with a list value containing
   "*" and other values (including other instances of "*") is
   syntactically invalid (therefore not allowed to be generated) and
   furthermore is unlikely to be interoperable.

### 13.1.3  If-Modified-Since

   The "If-Modified-Since" header field makes a GET or HEAD request
   method conditional on the selected representation's modification date
   being more recent than the date provided in the field value.
   Transfer of the selected representation's data is avoided if that
   data has not changed.


```abnf
     If-Modified-Since = HTTP-date
```


   An example of the field is:

   If-Modified-Since: Sat, 29 Oct 1994 19:43:31 GMT

> **MUST**: A recipient MUST ignore If-Modified-Since if the request contains an
   If-None-Match header field; the condition in If-None-Match is
   considered to be a more accurate replacement for the condition in If-
   Modified-Since, and the two are only combined for the sake of
   interoperating with older intermediaries that might not implement
   If-None-Match.

> **MUST**: A recipient MUST ignore the If-Modified-Since header field if the
   received field value is not a valid HTTP-date, the field value has
   more than one member, or if the request method is neither GET nor
   HEAD.

> **MUST**: A recipient MUST ignore the If-Modified-Since header field if the
   resource does not have a modification date available.

> **MUST**: A recipient MUST interpret an If-Modified-Since field value's
   timestamp in terms of the origin server's clock.

   If-Modified-Since is typically used for two distinct purposes: 1) to
   allow efficient updates of a cached representation that does not have
   an entity tag and 2) to limit the scope of a web traversal to
   resources that have recently changed.

   When used for cache updates, a cache will typically use the value of
   the cached message's Last-Modified header field to generate the field
   value of If-Modified-Since.  This behavior is most interoperable for
   cases where clocks are poorly synchronized or when the server has
   chosen to only honor exact timestamp matches (due to a problem with
   Last-Modified dates that appear to go "back in time" when the origin
   server's clock is corrected or a representation is restored from an
   archived backup).  However, caches occasionally generate the field
   value based on other data, such as the Date header field of the
   cached message or the clock time at which the message was received,
   particularly when the cached message does not contain a Last-Modified
   header field.

   When used for limiting the scope of retrieval to a recent time
   window, a user agent will generate an If-Modified-Since field value
   based on either its own clock or a Date header field received from
   the server in a prior response.  Origin servers that choose an exact
   timestamp match based on the selected representation's Last-Modified
   header field will not be able to help the user agent limit its data
   transfers to only those changed during the specified window.

   When an origin server receives a request that selects a
   representation and that request includes an If-Modified-Since header
> **SHOULD**: field without an If-None-Match header field, the origin server SHOULD
   evaluate the If-Modified-Since condition per Section 13.2 prior to
   performing the method.

   To evaluate a received If-Modified-Since header field:

   1.  If the selected representation's last modification date is
       earlier or equal to the date provided in the field value, the
       condition is false.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
