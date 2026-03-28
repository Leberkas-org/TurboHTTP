---
title: "21.10.  Explicit Congestion Notification Attacks"
rfc_number: 9000
rfc_section: "21.10"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.10: Explicit Congestion Notification Attacks — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, explicit_congestion_notification_attacks]
---

# 21.10.  Explicit Congestion Notification Attacks


   An on-path attacker could manipulate the value of ECN fields in the
   IP header to influence the sender's rate.  [RFC3168] discusses
   manipulations and their effects in more detail.

   A limited on-path attacker can duplicate and send packets with
   modified ECN fields to affect the sender's rate.  If duplicate
   packets are discarded by a receiver, an attacker will need to race
   the duplicate packet against the original to be successful in this
   attack.  Therefore, QUIC endpoints ignore the ECN field in an IP
   packet unless at least one QUIC packet in that IP packet is
   successfully processed; see Section 13.4.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
