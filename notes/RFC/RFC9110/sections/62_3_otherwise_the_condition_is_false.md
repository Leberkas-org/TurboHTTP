---
title: "3.  Otherwise, the condition is false."
rfc_number: 9110
rfc_section: "3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 3: Otherwise, the condition is false. — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, otherwise_the_condition_is_false]
---

## 3.  Otherwise, the condition is false.

   3.  Otherwise, the condition is false.

> **MUST NOT**: An origin server that evaluates an If-Match condition MUST NOT
   perform the requested method if the condition evaluates to false.
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
   cooperative.  For example, multiple user agents writing to a common
   resource as a semaphore (e.g., a nonatomic increment) are likely to
   collide and potentially lose important state transitions.  For those
   kinds of resources, an origin server is better off being stringent in
   sending 412 for every failed precondition on an unsafe method.  In
   other cases, excluding the ETag field from a success response might
   encourage the user agent to perform a GET as its next request to
   eliminate confusion about the resource's current state.

> **MAY**: A client MAY send an If-Match header field in a GET request to
   indicate that it would prefer a 412 (Precondition Failed) response if
   the selected representation does not match.  However, this is only
   useful in range requests (Section 14) for completing a previously
   received partial representation when there is no desire for a new
   representation.  If-Range (Section 13.1.5) is better suited for range
   requests when the client prefers to receive a new representation.

> **MAY**: A cache or intermediary MAY ignore If-Match because its
   interoperability features are only necessary for an origin server.

   Note that an If-Match header field with a list value containing "*"
   and other values (including other instances of "*") is syntactically
   invalid (therefore not allowed to be generated) and furthermore is
   unlikely to be interoperable.

### 13.1.2  If-None-Match

   The "If-None-Match" header field makes the request method conditional
   on a recipient cache or origin server either not having any current
   representation of the target resource, when the field value is "*",
   or having a selected representation with an entity tag that does not
   match any of those listed in the field value.

> **MUST**: A recipient MUST use the weak comparison function when comparing
   entity tags for If-None-Match (Section 8.8.3.2), since weak entity
   tags can be used for cache validation even if there have been changes
   to the representation data.


```abnf
     If-None-Match = "*" / #entity-tag
```


   Examples:

   If-None-Match: "xyzzy"
   If-None-Match: W/"xyzzy"
   If-None-Match: "xyzzy", "r2d2xxxx", "c3piozzzz"
   If-None-Match: W/"xyzzy", W/"r2d2xxxx", W/"c3piozzzz"
   If-None-Match: *

   If-None-Match is primarily used in conditional GET requests to enable
   efficient updates of cached information with a minimum amount of
   transaction overhead.  When a client desires to update one or more
> **SHOULD**: stored responses that have entity tags, the client SHOULD generate an
   If-None-Match header field containing a list of those entity tags
   when making a GET request; this allows recipient servers to send a
   304 (Not Modified) response to indicate when one of those stored
   responses matches the selected representation.

   If-None-Match can also be used with a value of "*" to prevent an
   unsafe request method (e.g., PUT) from inadvertently modifying an
   existing representation of the target resource when the client
   believes that the resource does not have a current representation
   (Section 9.2.1).  This is a variation on the "lost update" problem
   that might arise if more than one client attempts to create an
   initial representation for the target resource.

   When an origin server receives a request that selects a
   representation and that request includes an If-None-Match header
> **MUST**: field, the origin server MUST evaluate the If-None-Match condition
   per Section 13.2 prior to performing the method.

   To evaluate a received If-None-Match header field:

   1.  If the field value is "*", the condition is false if the origin
       server has a current representation for the target resource.

   2.  If the field value is a list of entity tags, the condition is
       false if one of the listed tags matches the entity tag of the
       selected representation.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
