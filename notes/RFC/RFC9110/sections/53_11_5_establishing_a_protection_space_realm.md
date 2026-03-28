---
title: "11.5.  Establishing a Protection Space (Realm)"
rfc_number: 9110
rfc_section: "11.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 11.5: Establishing a Protection Space (Realm) — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, establishing_a_protection_space_realm]
---

## 11.5.  Establishing a Protection Space (Realm)

## 11.5  Establishing a Protection Space (Realm)

   The "realm" authentication parameter is reserved for use by
   authentication schemes that wish to indicate a scope of protection.

   A "protection space" is defined by the origin (see Section 4.3.1) of
   the server being accessed, in combination with the realm value if
   present.  These realms allow the protected resources on a server to
   be partitioned into a set of protection spaces, each with its own
   authentication scheme and/or authorization database.  The realm value
   is a string, generally assigned by the origin server, that can have
   additional semantics specific to the authentication scheme.  Note
   that a response can have multiple challenges with the same auth-
   scheme but with different realms.

   The protection space determines the domain over which credentials can
   be automatically applied.  If a prior request has been authorized,
> **MAY**: the user agent MAY reuse the same credentials for all other requests
   within that protection space for a period of time determined by the
   authentication scheme, parameters, and/or user preferences (such as a
   configurable inactivity timeout).

   The extent of a protection space, and therefore the requests to which
   credentials might be automatically applied, is not necessarily known
   to clients without additional information.  An authentication scheme
   might define parameters that describe the extent of a protection
   space.  Unless specifically allowed by the authentication scheme, a
   single protection space cannot extend outside the scope of its
   server.

> **MUST**: For historical reasons, a sender MUST only generate the quoted-string
   syntax.  Recipients might have to support both token and quoted-
   string syntax for maximum interoperability with existing clients that
   have been accepting both notations for a long time.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
