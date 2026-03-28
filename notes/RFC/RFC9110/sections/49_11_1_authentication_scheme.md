---
title: "11.1.  Authentication Scheme"
rfc_number: 9110
rfc_section: "11.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 11.1: Authentication Scheme — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, authentication_scheme]
---

## 11.1.  Authentication Scheme

11.  HTTP Authentication

## 11.1  Authentication Scheme

   HTTP provides a general framework for access control and
   authentication, via an extensible set of challenge-response
   authentication schemes, which can be used by a server to challenge a
   client request and by a client to provide authentication information.
   It uses a case-insensitive token to identify the authentication
   scheme:


```abnf
     auth-scheme    = token
```


   Aside from the general framework, this document does not specify any
   authentication schemes.  New and existing authentication schemes are
   specified independently and ought to be registered within the
   "Hypertext Transfer Protocol (HTTP) Authentication Scheme Registry".
   For example, the "basic" and "digest" authentication schemes are
   defined by [RFC7617] and [RFC7616], respectively.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
