---
title: "21.12.  Version Downgrade"
rfc_number: 9000
rfc_section: "21.12"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.12: Version Downgrade — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, version_downgrade]
---

# 21.12.  Version Downgrade


   This document defines QUIC Version Negotiation packets (Section 6),
   which can be used to negotiate the QUIC version used between two
   endpoints.  However, this document does not specify how this
   negotiation will be performed between this version and subsequent
   future versions.  In particular, Version Negotiation packets do not
   contain any mechanism to prevent version downgrade attacks.  Future
> **MUST**: versions of QUIC that use Version Negotiation packets MUST define a
   mechanism that is robust against version downgrade attacks.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
