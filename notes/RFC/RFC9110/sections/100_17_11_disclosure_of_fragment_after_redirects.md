---
title: "17.11.  Disclosure of Fragment after Redirects"
rfc_number: 9110
rfc_section: "17.11"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.11: Disclosure of Fragment after Redirects — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, disclosure_of_fragment_after_redirects]
---

## 17.11.  Disclosure of Fragment after Redirects

## 17.11  Disclosure of Fragment after Redirects

   Although fragment identifiers used within URI references are not sent
   in requests, implementers ought to be aware that they will be visible
   to the user agent and any extensions or scripts running as a result
   of the response.  In particular, when a redirect occurs and the
   original request's fragment identifier is inherited by the new
   reference in Location (Section 10.2.2), this might have the effect of
   disclosing one site's fragment to another site.  If the first site
   uses personal information in fragments, it ought to ensure that
   redirects to other sites include a (possibly empty) fragment
   component in order to block that inheritance.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
