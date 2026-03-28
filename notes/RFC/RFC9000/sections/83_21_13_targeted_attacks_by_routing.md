---
title: "21.13.  Targeted Attacks by Routing"
rfc_number: 9000
rfc_section: "21.13"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.13: Targeted Attacks by Routing — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, targeted_attacks_by_routing]
---

# 21.13.  Targeted Attacks by Routing


   Deployments should limit the ability of an attacker to target a new
   connection to a particular server instance.  Ideally, routing
   decisions are made independently of client-selected values, including
   addresses.  Once an instance is selected, a connection ID can be
   selected so that later packets are routed to the same instance.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
