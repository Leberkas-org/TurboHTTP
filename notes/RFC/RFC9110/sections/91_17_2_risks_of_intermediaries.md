---
title: "17.2.  Risks of Intermediaries"
rfc_number: 9110
rfc_section: "17.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.2: Risks of Intermediaries — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, risks_of_intermediaries]
---

## 17.2.  Risks of Intermediaries

## 17.2  Risks of Intermediaries

   HTTP intermediaries are inherently situated for on-path attacks.
   Compromise of the systems on which the intermediaries run can result
   in serious security and privacy problems.  Intermediaries might have
   access to security-related information, personal information about
   individual users and organizations, and proprietary information
   belonging to users and content providers.  A compromised
   intermediary, or an intermediary implemented or configured without
   regard to security and privacy considerations, might be used in the
   commission of a wide range of potential attacks.

   Intermediaries that contain a shared cache are especially vulnerable
   to cache poisoning attacks, as described in Section 7 of [CACHING].

   Implementers need to consider the privacy and security implications
   of their design and coding decisions, and of the configuration
   options they provide to operators (especially the default
   configuration).

   Intermediaries are no more trustworthy than the people and policies
   under which they operate; HTTP cannot solve this problem.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
