---
title: "17.6.  Attacks Using Shared-Dictionary Compression"
rfc_number: 9110
rfc_section: "17.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 17.6: Attacks Using Shared-Dictionary Compression — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, attacks_using_shared-dictionary_compression]
---

## 17.6.  Attacks Using Shared-Dictionary Compression

## 17.6  Attacks Using Shared-Dictionary Compression

   Some attacks on encrypted protocols use the differences in size
   created by dynamic compression to reveal confidential information;
   for example, [BREACH].  These attacks rely on creating a redundancy
   between attacker-controlled content and the confidential information,
   such that a dynamic compression algorithm using the same dictionary
   for both content will compress more efficiently when the attacker-
   controlled content matches parts of the confidential content.

   HTTP messages can be compressed in a number of ways, including using
   TLS compression, content codings, transfer codings, and other
   extension or version-specific mechanisms.

   The most effective mitigation for this risk is to disable compression
   on sensitive data, or to strictly separate sensitive data from
   attacker-controlled data so that they cannot share the same
   compression dictionary.  With careful design, a compression scheme
   can be designed in a way that is not considered exploitable in
   limited use cases, such as HPACK ([HPACK]).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
