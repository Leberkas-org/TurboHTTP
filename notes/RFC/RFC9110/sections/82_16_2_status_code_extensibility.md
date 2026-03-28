---
title: "16.2.  Status Code Extensibility"
rfc_number: 9110
rfc_section: "16.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 16.2: Status Code Extensibility — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, status_code_extensibility]
---

## 16.2.  Status Code Extensibility

## 16.2  Status Code Extensibility

### 16.2.1  Status Code Registry

   The "Hypertext Transfer Protocol (HTTP) Status Code Registry",
   maintained by IANA at <https://www.iana.org/assignments/http-status-
   codes>, registers status code numbers.

> **MUST**: A registration MUST include the following fields:

   *  Status Code (3 digits)

   *  Short Description

   *  Pointer to specification text

   Values to be added to the HTTP status code namespace require IETF
   Review (see [RFC8126], Section 4.8).

### 16.2.2  Considerations for New Status Codes

   When it is necessary to express semantics for a response that are not
   defined by current status codes, a new status code can be registered.
   Status codes are generic; they are potentially applicable to any
   resource, not just one particular media type, kind of resource, or
   application of HTTP.  As such, it is preferred that new status codes
   be registered in a document that isn't specific to a single
   application.

   New status codes are required to fall under one of the categories
   defined in Section 15.  To allow existing parsers to process the
   response message, new status codes cannot disallow content, although
   they can mandate a zero-length content.

   Proposals for new status codes that are not yet widely deployed ought
   to avoid allocating a specific number for the code until there is
   clear consensus that it will be registered; instead, early drafts can
   use a notation such as "4NN", or "3N0" .. "3N9", to indicate the
   class of the proposed status code(s) without consuming a number
   prematurely.

   The definition of a new status code ought to explain the request
   conditions that would cause a response containing that status code
   (e.g., combinations of request header fields and/or method(s)) along
   with any dependencies on response header fields (e.g., what fields
   are required, what fields can modify the semantics, and what field
   semantics are further refined when used with the new status code).

   By default, a status code applies only to the request corresponding
   to the response it occurs within.  If a status code applies to a
   larger scope of applicability -- for example, all requests to the
   resource in question or all requests to a server -- this must be
   explicitly specified.  When doing so, it should be noted that not all
   clients can be expected to consistently apply a larger scope because
   they might not understand the new status code.

   The definition of a new final status code ought to specify whether or
   not it is heuristically cacheable.  Note that any response with a
   final status code can be cached if the response has explicit
   freshness information.  A status code defined as heuristically
   cacheable is allowed to be cached without explicit freshness
   information.  Likewise, the definition of a status code can place
   constraints upon cache behavior if the must-understand cache
   directive is used.  See [CACHING] for more information.

   Finally, the definition of a new status code ought to indicate
   whether the content has any implied association with an identified
   resource (Section 6.4.2).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
