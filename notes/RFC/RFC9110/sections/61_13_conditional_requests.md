---
title: "13.  Conditional Requests"
rfc_number: 9110
rfc_section: "13"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 13: Conditional Requests — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, conditional_requests]
---

## 13.  Conditional Requests

13.  Conditional Requests

   A conditional request is an HTTP request with one or more request
   header fields that indicate a precondition to be tested before
   applying the request method to the target resource.  Section 13.2
   defines when to evaluate preconditions and their order of precedence
   when more than one precondition is present.

   Conditional GET requests are the most efficient mechanism for HTTP
   cache updates [CACHING].  Conditionals can also be applied to state-
   changing methods, such as PUT and DELETE, to prevent the "lost
   update" problem: one client accidentally overwriting the work of
   another client that has been acting in parallel.

## 13.1  Preconditions

   Preconditions are usually defined with respect to a state of the
   target resource as a whole (its current value set) or the state as
   observed in a previously obtained representation (one value in that
   set).  If a resource has multiple current representations, each with
   its own observable state, a precondition will assume that the mapping
   of each request to a selected representation (Section 3.2) is
   consistent over time.  Regardless, if the mapping is inconsistent or
   the server is unable to select an appropriate representation, then no
   harm will result when the precondition evaluates to false.

   Each precondition defined below consists of a comparison between a
   set of validators obtained from prior representations of the target
   resource to the current state of validators for the selected
   representation (Section 8.8).  Hence, these preconditions evaluate
   whether the state of the target resource has changed since a given
   state known by the client.  The effect of such an evaluation depends
   on the method semantics and choice of conditional, as defined in
   Section 13.2.

   Other preconditions, defined by other specifications as extension
   fields, might place conditions on all recipients, on the state of the
   target resource in general, or on a group of resources.  For
   instance, the "If" header field in WebDAV can make a request
   conditional on various aspects of multiple resources, such as locks,
   if the recipient understands and implements that field ([WEBDAV],
   Section 10.4).

   Extensibility of preconditions is only possible when the precondition
   can be safely ignored if unknown (like If-Modified-Since), when
   deployment can be assumed for a given use case, or when
   implementation is signaled by some other property of the target
   resource.  This encourages a focus on mutually agreed deployment of
   common standards.

### 13.1.1  If-Match

   The "If-Match" header field makes the request method conditional on
   the recipient origin server either having at least one current
   representation of the target resource, when the field value is "*",
   or having a current representation of the target resource that has an
   entity tag matching a member of the list of entity tags provided in
   the field value.

> **MUST**: An origin server MUST use the strong comparison function when
   comparing entity tags for If-Match (Section 8.8.3.2), since the
   client intends this precondition to prevent the method from being
   applied if there have been any changes to the representation data.


```abnf
     If-Match = "*" / #entity-tag
```


   Examples:

   If-Match: "xyzzy"
   If-Match: "xyzzy", "r2d2xxxx", "c3piozzzz"
   If-Match: *

   If-Match is most often used with state-changing methods (e.g., POST,
   PUT, DELETE) to prevent accidental overwrites when multiple user
   agents might be acting in parallel on the same resource (i.e., to
   prevent the "lost update" problem).  In general, it can be used with
   any method that involves the selection or modification of a
   representation to abort the request if the selected representation's
   current entity tag is not a member within the If-Match field value.

   When an origin server receives a request that selects a
   representation and that request includes an If-Match header field,
> **MUST**: the origin server MUST evaluate the If-Match condition per
   Section 13.2 prior to performing the method.

   To evaluate a received If-Match header field:

   1.  If the field value is "*", the condition is true if the origin
       server has a current representation for the target resource.

   2.  If the field value is a list of entity tags, the condition is
       true if any of the listed tags match the entity tag of the
       selected representation.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
