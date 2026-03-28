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

> **MUST**: A recipient of an If-Range header field MUST ignore the Range header
   field if the If-Range condition evaluates to false.  Otherwise, the
> **SHOULD**: recipient SHOULD process the Range header field as requested.

   Note that the If-Range comparison is by exact match, including when
   the validator is an HTTP-date, and so it differs from the "earlier
   than or equal to" comparison used when evaluating an
   If-Unmodified-Since conditional.

## 13.2  Evaluation of Preconditions

### 13.2.1  When to Evaluate

> **MUST**: Except when excluded below, a recipient cache or origin server MUST
   evaluate received request preconditions after it has successfully
   performed its normal request checks and just before it would process
   the request content (if any) or perform the action associated with
> **MUST**: the request method.  A server MUST ignore all received preconditions
   if its response to the same request without those conditions, prior
   to processing the request content, would have been a status code
   other than a 2xx (Successful) or 412 (Precondition Failed).  In other
   words, redirects and failures that can be detected before significant
   processing occurs take precedence over the evaluation of
   preconditions.

   A server that is not the origin server for the target resource and
> **MUST NOT**: cannot act as a cache for requests on the target resource MUST NOT
   evaluate the conditional request header fields defined by this
> **MUST**: specification, and it MUST forward them if the request is forwarded,
   since the generating client intends that they be evaluated by a
   server that can provide a current representation.  Likewise, a server
> **MUST**: MUST ignore the conditional request header fields defined by this
   specification when received with a request method that does not
   involve the selection or modification of a selected representation,
   such as CONNECT, OPTIONS, or TRACE.

   Note that protocol extensions can modify the conditions under which
   preconditions are evaluated or the consequences of their evaluation.
   For example, the immutable cache directive (defined by [RFC8246])
   instructs caches to forgo forwarding conditional requests when they
   hold a fresh response.

   Although conditional request header fields are defined as being
   usable with the HEAD method (to keep HEAD's semantics consistent with
   those of GET), there is no point in sending a conditional HEAD
   because a successful response is around the same size as a 304 (Not
   Modified) response and more useful than a 412 (Precondition Failed)
   response.

### 13.2.2  Precedence of Preconditions

   When more than one conditional request header field is present in a
   request, the order in which the fields are evaluated becomes
   important.  In practice, the fields defined in this document are
   consistently implemented in a single, logical order, since "lost
   update" preconditions have more strict requirements than cache
   validation, a validated cache is more efficient than a partial
   response, and entity tags are presumed to be more accurate than date
   validators.

> **MUST**: A recipient cache or origin server MUST evaluate the request
   preconditions defined by this specification in the following order:

   1.  When recipient is the origin server and If-Match is present,
       evaluate the If-Match precondition:

       *  if true, continue to step 3

       *  if false, respond 412 (Precondition Failed) unless it can be
          determined that the state-changing request has already
          succeeded (see Section 13.1.1)

   2.  When recipient is the origin server, If-Match is not present, and
       If-Unmodified-Since is present, evaluate the If-Unmodified-Since
       precondition:

       *  if true, continue to step 3

       *  if false, respond 412 (Precondition Failed) unless it can be
          determined that the state-changing request has already
          succeeded (see Section 13.1.4)

   3.  When If-None-Match is present, evaluate the If-None-Match
       precondition:

       *  if true, continue to step 5

       *  if false for GET/HEAD, respond 304 (Not Modified)

       *  if false for other methods, respond 412 (Precondition Failed)

   4.  When the method is GET or HEAD, If-None-Match is not present, and
       If-Modified-Since is present, evaluate the If-Modified-Since
       precondition:

       *  if true, continue to step 5

       *  if false, respond 304 (Not Modified)

   5.  When the method is GET and both Range and If-Range are present,
       evaluate the If-Range precondition:

       *  if true and the Range is applicable to the selected
          representation, respond 206 (Partial Content)

       *  otherwise, ignore the Range header field and respond 200 (OK)

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
