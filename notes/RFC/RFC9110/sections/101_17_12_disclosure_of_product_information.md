---
title: "17.12.  Disclosure of Product Information"
rfc_number: 9110
rfc_section: "17.12"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.12: Disclosure of Product Information — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, disclosure_of_product_information]
---

## 17.12.  Disclosure of Product Information

## 17.12  Disclosure of Product Information

   The User-Agent (Section 10.1.5), Via (Section 7.6.3), and Server
   (Section 10.2.4) header fields often reveal information about the
   respective sender's software systems.  In theory, this can make it
   easier for an attacker to exploit known security holes; in practice,
   attackers tend to try all potential holes regardless of the apparent
   software versions being used.

   Proxies that serve as a portal through a network firewall ought to
   take special precautions regarding the transfer of header information
   that might identify hosts behind the firewall.  The Via header field
   allows intermediaries to replace sensitive machine names with
   pseudonyms.

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
