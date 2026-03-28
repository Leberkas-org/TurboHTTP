---
title: "21.3.  Amplification Attack"
rfc_number: 9000
rfc_section: "21.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.3: Amplification Attack — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, amplification_attack]
---

# 21.3.  Amplification Attack


   An attacker might be able to receive an address validation token
   (Section 8) from a server and then release the IP address it used to
   acquire that token.  At a later time, the attacker can initiate a
   0-RTT connection with a server by spoofing this same address, which
   might now address a different (victim) endpoint.  The attacker can
   thus potentially cause the server to send an initial congestion
   window's worth of data towards the victim.

> **SHOULD**: Servers SHOULD provide mitigations for this attack by limiting the
   usage and lifetime of address validation tokens; see Section 8.1.3.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
