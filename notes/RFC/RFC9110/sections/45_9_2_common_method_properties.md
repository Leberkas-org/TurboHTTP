---
title: "9.2.  Common Method Properties"
rfc_number: 9110
rfc_section: "9.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 9.2: Common Method Properties — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, common_method_properties]
---

## 9.2.  Common Method Properties

## 9.2  Common Method Properties

### 9.2.1  Safe Methods

   Request methods are considered "safe" if their defined semantics are
   essentially read-only; i.e., the client does not request, and does
   not expect, any state change on the origin server as a result of
   applying a safe method to a target resource.  Likewise, reasonable
   use of a safe method is not expected to cause any harm, loss of
   property, or unusual burden on the origin server.

   This definition of safe methods does not prevent an implementation
   from including behavior that is potentially harmful, that is not
   entirely read-only, or that causes side effects while invoking a safe
   method.  What is important, however, is that the client did not
   request that additional behavior and cannot be held accountable for
   it.  For example, most servers append request information to access
   log files at the completion of every response, regardless of the
   method, and that is considered safe even though the log storage might
   become full and cause the server to fail.  Likewise, a safe request
   initiated by selecting an advertisement on the Web will often have
   the side effect of charging an advertising account.

   Of the request methods defined by this specification, the GET, HEAD,
   OPTIONS, and TRACE methods are defined to be safe.

   The purpose of distinguishing between safe and unsafe methods is to
   allow automated retrieval processes (spiders) and cache performance
   optimization (pre-fetching) to work without fear of causing harm.  In
   addition, it allows a user agent to apply appropriate constraints on
   the automated use of unsafe methods when processing potentially
   untrusted content.

> **SHOULD**: A user agent SHOULD distinguish between safe and unsafe methods when
   presenting potential actions to a user, such that the user can be
   made aware of an unsafe action before it is requested.

   When a resource is constructed such that parameters within the target
   URI have the effect of selecting an action, it is the resource
   owner's responsibility to ensure that the action is consistent with
   the request method semantics.  For example, it is common for Web-
   based content editing software to use actions within query
   parameters, such as "page?do=delete".  If the purpose of such a
> **MUST**: resource is to perform an unsafe action, then the resource owner MUST
   disable or disallow that action when it is accessed using a safe
   request method.  Failure to do so will result in unfortunate side
   effects when automated processes perform a GET on every URI reference
   for the sake of link maintenance, pre-fetching, building a search
   index, etc.

### 9.2.2  Idempotent Methods

   A request method is considered "idempotent" if the intended effect on
   the server of multiple identical requests with that method is the
   same as the effect for a single such request.  Of the request methods
   defined by this specification, PUT, DELETE, and safe request methods
   are idempotent.

   Like the definition of safe, the idempotent property only applies to
   what has been requested by the user; a server is free to log each
   request separately, retain a revision control history, or implement
   other non-idempotent side effects for each idempotent request.

   Idempotent methods are distinguished because the request can be
   repeated automatically if a communication failure occurs before the
   client is able to read the server's response.  For example, if a
   client sends a PUT request and the underlying connection is closed
   before any response is received, then the client can establish a new
   connection and retry the idempotent request.  It knows that repeating
   the request will have the same intended effect, even if the original
   request succeeded, though the response might differ.

> **SHOULD NOT**: A client SHOULD NOT automatically retry a request with a non-
   idempotent method unless it has some means to know that the request
   semantics are actually idempotent, regardless of the method, or some
   means to detect that the original request was never applied.

   For example, a user agent can repeat a POST request automatically if
   it knows (through design or configuration) that the request is safe
   for that resource.  Likewise, a user agent designed specifically to
   operate on a version control repository might be able to recover from
   partial failure conditions by checking the target resource
   revision(s) after a failed connection, reverting or fixing any
   changes that were partially applied, and then automatically retrying
   the requests that failed.

   Some clients take a riskier approach and attempt to guess when an
   automatic retry is possible.  For example, a client might
   automatically retry a POST request if the underlying transport
   connection closed before any part of a response is received,
   particularly if an idle persistent connection was used.

> **MUST NOT**: A proxy MUST NOT automatically retry non-idempotent requests.  A
   client SHOULD NOT automatically retry a failed automatic retry.

### 9.2.3  Methods and Caching

   For a cache to store and use a response, the associated method needs
   to explicitly allow caching and to detail under what conditions a
   response can be used to satisfy subsequent requests; a method
   definition that does not do so cannot be cached.  For additional
   requirements see [CACHING].

   This specification defines caching semantics for GET, HEAD, and POST,
   although the overwhelming majority of cache implementations only
   support GET and HEAD.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
