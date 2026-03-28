---
title: "7.4.  Rejecting Misdirected Requests"
rfc_number: 9110
rfc_section: "7.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 7.4: Rejecting Misdirected Requests — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, rejecting_misdirected_requests]
---

## 7.4.  Rejecting Misdirected Requests

## 7.4  Rejecting Misdirected Requests

   Once a request is received by a server and parsed sufficiently to
   determine its target URI, the server decides whether to process the
   request itself, forward the request to another server, redirect the
   client to a different resource, respond with an error, or drop the
   connection.  This decision can be influenced by anything about the
   request or connection context, but is specifically directed at
   whether the server has been configured to process requests for that
   target URI and whether the connection context is appropriate for that
   request.

   For example, a request might have been misdirected, deliberately or
   accidentally, such that the information within a received Host header
   field differs from the connection's host or port.  If the connection
   is from a trusted gateway, such inconsistency might be expected;
   otherwise, it might indicate an attempt to bypass security filters,
   trick the server into delivering non-public content, or poison a
   cache.  See Section 17 for security considerations regarding message
   routing.

   Unless the connection is from a trusted gateway, an origin server
> **MUST**: MUST reject a request if any scheme-specific requirements for the
   target URI are not met.  In particular, a request for an "https"
> **MUST**: resource MUST be rejected unless it has been received over a
   connection that has been secured via a certificate valid for that
   target URI's origin, as defined by Section 4.2.2.

   The 421 (Misdirected Request) status code in a response indicates
   that the origin server has rejected the request because it appears to
   have been misdirected (Section 15.5.20).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
