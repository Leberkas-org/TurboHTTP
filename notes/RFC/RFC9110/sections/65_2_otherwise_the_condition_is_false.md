---
title: "2.  Otherwise, the condition is false."
rfc_number: 9110
rfc_section: "2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 2: Otherwise, the condition is false. — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, otherwise_the_condition_is_false]
---

## 2.  Otherwise, the condition is false.

   2.  Otherwise, the condition is false.

> **MUST**: An origin server that evaluates an If-Unmodified-Since condition MUST
   NOT perform the requested method if the condition evaluates to false.
> **MAY**: Instead, the origin server MAY indicate that the conditional request
   failed by responding with a 412 (Precondition Failed) status code.
   Alternatively, if the request is a state-changing operation that
   appears to have already been applied to the selected representation,
> **MAY**: the origin server MAY respond with a 2xx (Successful) status code
   (i.e., the change requested by the user agent has already succeeded,
   but the user agent might not be aware of it, perhaps because the
   prior response was lost or an equivalent change was made by some
   other user agent).

   Allowing an origin server to send a success response when a change
   request appears to have already been applied is more efficient for
   many authoring use cases, but comes with some risk if multiple user
   agents are making change requests that are very similar but not
   cooperative.  In those cases, an origin server is better off being
   stringent in sending 412 for every failed precondition on an unsafe
   method.

> **MAY**: A client MAY send an If-Unmodified-Since header field in a GET
   request to indicate that it would prefer a 412 (Precondition Failed)
   response if the selected representation has been modified.  However,
   this is only useful in range requests (Section 14) for completing a
   previously received partial representation when there is no desire
   for a new representation.  If-Range (Section 13.1.5) is better suited
   for range requests when the client prefers to receive a new
   representation.

> **MAY**: A cache or intermediary MAY ignore If-Unmodified-Since because its
   interoperability features are only necessary for an origin server.

### 13.1.5  If-Range

   The "If-Range" header field provides a special conditional request
   mechanism that is similar to the If-Match and If-Unmodified-Since
   header fields but that instructs the recipient to ignore the Range
   header field if the validator doesn't match, resulting in transfer of
   the new selected representation instead of a 412 (Precondition
   Failed) response.

   If a client has a partial copy of a representation and wishes to have
   an up-to-date copy of the entire representation, it could use the
   Range header field with a conditional GET (using either or both of
   If-Unmodified-Since and If-Match.)  However, if the precondition
   fails because the representation has been modified, the client would
   then have to make a second request to obtain the entire current
   representation.

   The "If-Range" header field allows a client to "short-circuit" the
   second request.  Informally, its meaning is as follows: if the
   representation is unchanged, send me the part(s) that I am requesting
   in Range; otherwise, send me the entire representation.


```abnf
     If-Range = entity-tag / HTTP-date
```


   A valid entity-tag can be distinguished from a valid HTTP-date by
   examining the first three characters for a DQUOTE.

> **MUST NOT**: A client MUST NOT generate an If-Range header field in a request that
   does not contain a Range header field.  A server MUST ignore an If-
   Range header field received in a request that does not contain a
> **MUST**: Range header field.  An origin server MUST ignore an If-Range header
   field received in a request for a target resource that does not
   support Range requests.

> **MUST NOT**: A client MUST NOT generate an If-Range header field containing an
   entity tag that is marked as weak.  A client MUST NOT generate an If-
   Range header field containing an HTTP-date unless the client has no
   entity tag for the corresponding representation and the date is a
   strong validator in the sense defined by Section 8.8.2.2.

   A server that receives an If-Range header field on a Range request
> **MUST**: MUST evaluate the condition per Section 13.2 prior to performing the
   method.

   To evaluate a received If-Range header field containing an HTTP-date:

   1.  If the HTTP-date validator provided is not a strong validator in
       the sense defined by Section 8.8.2.2, the condition is false.

   2.  If the HTTP-date validator provided exactly matches the
       Last-Modified field value for the selected representation, the
       condition is true.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
