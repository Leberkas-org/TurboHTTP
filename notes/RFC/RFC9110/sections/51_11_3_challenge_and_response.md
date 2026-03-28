---
title: "11.3.  Challenge and Response"
rfc_number: 9110
rfc_section: "11.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 11.3: Challenge and Response — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, challenge_and_response]
---

## 11.3.  Challenge and Response

## 11.3  Challenge and Response

   A 401 (Unauthorized) response message is used by an origin server to
   challenge the authorization of a user agent, including a
   WWW-Authenticate header field containing at least one challenge
   applicable to the requested resource.

   A 407 (Proxy Authentication Required) response message is used by a
   proxy to challenge the authorization of a client, including a
   Proxy-Authenticate header field containing at least one challenge
   applicable to the proxy for the requested resource.


```abnf
     challenge   = auth-scheme [ 1*SP ( token68 / #auth-param ) ]
```


      |  *Note:* Many clients fail to parse a challenge that contains an
      |  unknown scheme.  A workaround for this problem is to list well-
      |  supported schemes (such as "basic") first.

   A user agent that wishes to authenticate itself with an origin server
   -- usually, but not necessarily, after receiving a 401 (Unauthorized)
   -- can do so by including an Authorization header field with the
   request.

   A client that wishes to authenticate itself with a proxy -- usually,
   but not necessarily, after receiving a 407 (Proxy Authentication
   Required) -- can do so by including a Proxy-Authorization header
   field with the request.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
