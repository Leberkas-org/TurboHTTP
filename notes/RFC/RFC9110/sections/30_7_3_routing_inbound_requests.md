---
title: "7.3.  Routing Inbound Requests"
rfc_number: 9110
rfc_section: "7.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 7.3: Routing Inbound Requests — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, routing_inbound_requests]
---

## 7.3.  Routing Inbound Requests

## 7.3  Routing Inbound Requests

   Once the target URI and its origin are determined, a client decides
   whether a network request is necessary to accomplish the desired
   semantics and, if so, where that request is to be directed.

### 7.3.1  To a Cache

   If the client has a cache [CACHING] and the request can be satisfied
   by it, then the request is usually directed there first.

### 7.3.2  To a Proxy

   If the request is not satisfied by a cache, then a typical client
   will check its configuration to determine whether a proxy is to be
   used to satisfy the request.  Proxy configuration is implementation-
   dependent, but is often based on URI prefix matching, selective
   authority matching, or both, and the proxy itself is usually
   identified by an "http" or "https" URI.

   If an "http" or "https" proxy is applicable, the client connects
   inbound by establishing (or reusing) a connection to that proxy and
   then sending it an HTTP request message containing a request target
   that matches the client's target URI.

### 7.3.3  To the Origin

   If no proxy is applicable, a typical client will invoke a handler
   routine (specific to the target URI's scheme) to obtain access to the
   identified resource.  How that is accomplished is dependent on the
   target URI scheme and defined by its associated specification.

   Section 4.3.2 defines how to obtain access to an "http" resource by
   establishing (or reusing) an inbound connection to the identified
   origin server and then sending it an HTTP request message containing
   a request target that matches the client's target URI.

   Section 4.3.3 defines how to obtain access to an "https" resource by
   establishing (or reusing) an inbound secured connection to an origin
   server that is authoritative for the identified origin and then
   sending it an HTTP request message containing a request target that
   matches the client's target URI.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
