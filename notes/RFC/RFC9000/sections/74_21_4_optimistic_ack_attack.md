---
title: "21.4.  Optimistic ACK Attack"
rfc_number: 9000
rfc_section: "21.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.4: Optimistic ACK Attack — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, optimistic_ack_attack]
---

# 21.4.  Optimistic ACK Attack


   An endpoint that acknowledges packets it has not received might cause
   a congestion controller to permit sending at rates beyond what the
> **MAY**: network supports.  An endpoint MAY skip packet numbers when sending
   packets to detect this behavior.  An endpoint can then immediately
   close the connection with a connection error of type
   PROTOCOL_VIOLATION; see Section 10.2.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
