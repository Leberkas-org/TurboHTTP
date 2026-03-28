---
title: "22.2.  QUIC Versions Registry"
rfc_number: 9000
rfc_section: "22.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 22.2: QUIC Versions Registry — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, quic_versions_registry]
---

# 22.2.  QUIC Versions Registry


   IANA has added a registry for "QUIC Versions" under a "QUIC" heading.

   The "QUIC Versions" registry governs a 32-bit space; see Section 15.
   This registry follows the registration policy from Section 22.1.
   Permanent registrations in this registry are assigned using the
   Specification Required policy (Section 4.6 of [RFC8126]).

   The codepoint of 0x00000001 for the protocol is assigned with
   permanent status to the protocol defined in this document.  The
   codepoint of 0x00000000 is permanently reserved; the note for this
   codepoint indicates that this version is reserved for version
   negotiation.

> **MUST**: All codepoints that follow the pattern 0x?a?a?a?a are reserved, MUST
   NOT be assigned by IANA, and MUST NOT appear in the listing of
   assigned values.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
