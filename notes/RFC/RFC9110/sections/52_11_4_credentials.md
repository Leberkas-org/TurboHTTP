---
title: "11.4.  Credentials"
rfc_number: 9110
rfc_section: "11.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 11.4: Credentials — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, credentials]
---

## 11.4.  Credentials

## 11.4  Credentials

   Both the Authorization field value and the Proxy-Authorization field
   value contain the client's credentials for the realm of the resource
   being requested, based upon a challenge received in a response
   (possibly at some point in the past).  When creating their values,
   the user agent ought to do so by selecting the challenge with what it
   considers to be the most secure auth-scheme that it understands,
   obtaining credentials from the user as appropriate.  Transmission of
   credentials within header field values implies significant security
   considerations regarding the confidentiality of the underlying
   connection, as described in Section 17.16.1.


```abnf
     credentials = auth-scheme [ 1*SP ( token68 / #auth-param ) ]
```


   Upon receipt of a request for a protected resource that omits
   credentials, contains invalid credentials (e.g., a bad password) or
   partial credentials (e.g., when the authentication scheme requires
> **SHOULD**: more than one round trip), an origin server SHOULD send a 401
   (Unauthorized) response that contains a WWW-Authenticate header field
   with at least one (possibly new) challenge applicable to the
   requested resource.

   Likewise, upon receipt of a request that omits proxy credentials or
   contains invalid or partial proxy credentials, a proxy that requires
> **SHOULD**: authentication SHOULD generate a 407 (Proxy Authentication Required)
   response that contains a Proxy-Authenticate header field with at
   least one (possibly new) challenge applicable to the proxy.

   A server that receives valid credentials that are not adequate to
   gain access ought to respond with the 403 (Forbidden) status code
   (Section 15.5.4).

   HTTP does not restrict applications to this simple challenge-response
   framework for access authentication.  Additional mechanisms can be
   used, such as authentication at the transport level or via message
   encapsulation, and with additional header fields specifying
   authentication information.  However, such additional mechanisms are
   not defined by this specification.

   Note that various custom mechanisms for user authentication use the
   Set-Cookie and Cookie header fields, defined in [COOKIE], for passing
   tokens related to authentication.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
