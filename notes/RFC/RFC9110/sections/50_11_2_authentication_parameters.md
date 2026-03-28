---
title: "11.2.  Authentication Parameters"
rfc_number: 9110
rfc_section: "11.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 11.2: Authentication Parameters — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, authentication_parameters]
---

## 11.2.  Authentication Parameters

## 11.2  Authentication Parameters

   The authentication scheme is followed by additional information
   necessary for achieving authentication via that scheme as either a
   comma-separated list of parameters or a single sequence of characters
   capable of holding base64-encoded information.


```abnf
     token68        = 1*( ALPHA / DIGIT /
                          "-" / "." / "_" / "~" / "+" / "/" ) *"="
```


   The token68 syntax allows the 66 unreserved URI characters ([URI]),
   plus a few others, so that it can hold a base64, base64url (URL and
   filename safe alphabet), base32, or base16 (hex) encoding, with or
   without padding, but excluding whitespace ([RFC4648]).

   Authentication parameters are name/value pairs, where the name token
> **MUST**: is matched case-insensitively and each parameter name MUST only occur
   once per challenge.


```abnf
     auth-param     = token BWS "=" BWS ( token / quoted-string )
```


   Parameter values can be expressed either as "token" or as "quoted-
   string" (Section 5.6).  Authentication scheme definitions need to
   accept both notations, both for senders and recipients, to allow
   recipients to use generic parsing components regardless of the
   authentication scheme.

   For backwards compatibility, authentication scheme definitions can
   restrict the format for senders to one of the two variants.  This can
   be important when it is known that deployed implementations will fail
   when encountering one of the two formats.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
