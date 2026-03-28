---
title: "6.  Implementation Considerations"
rfc_number: 6265
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc6265"
description: "Section 6: Implementation Considerations — RFC 6265 — HTTP State Management (Cookies)"
tags: [RFC6265, cookies, state-management, Set-Cookie, domain-matching, path-matching, SameSite, HttpOnly, implementation_considerations]
---

# 6.  Implementation Considerations


## 6.1.  Limits

   Practical user agent implementations have limits on the number and
> **SHOULD**: size of cookies that they can store.  General-use user agents SHOULD
   provide each of the following minimum capabilities:

   o  At least 4096 bytes per cookie (as measured by the sum of the
      length of the cookie's name, value, and attributes).

   o  At least 50 cookies per domain.

   o  At least 3000 cookies total.

> **SHOULD**: Servers SHOULD use as few and as small cookies as possible to avoid
   reaching these implementation limits and to minimize network
   bandwidth due to the Cookie header being included in every request.

> **SHOULD**: Servers SHOULD gracefully degrade if the user agent fails to return
   one or more cookies in the Cookie header because the user agent might
   evict any cookie at any time on orders from the user.

## 6.2.  Application Programming Interfaces

   One reason the Cookie and Set-Cookie headers use such esoteric syntax
   is that many platforms (both in servers and user agents) provide a
   string-based application programming interface (API) to cookies,
   requiring application-layer programmers to generate and parse the
   syntax used by the Cookie and Set-Cookie headers, which many
   programmers have done incorrectly, resulting in interoperability
   problems.

   Instead of providing string-based APIs to cookies, platforms would be
   well-served by providing more semantic APIs.  It is beyond the scope
   of this document to recommend specific API designs, but there are
   clear benefits to accepting an abstract "Date" object instead of a
   serialized date string.

## 6.3.  IDNA Dependency and Migration

   IDNA2008 [RFC5890] supersedes IDNA2003 [RFC3490].  However, there are
   differences between the two specifications, and thus there can be
   differences in processing (e.g., converting) domain name labels that
   have been registered under one from those registered under the other.
   There will be a transition period of some time during which IDNA2003-
> **SHOULD**: based domain name labels will exist in the wild.  User agents SHOULD
   implement IDNA2008 [RFC5890] and MAY implement [UTS46] or [RFC5895]



   in order to facilitate their IDNA transition.  If a user agent does
> **MUST**: not implement IDNA2008, the user agent MUST implement IDNA2003
   [RFC3490].

---

**Navigation:** [[../RFC6265|RFC6265 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
