---
title: "21.9.  Peer Denial of Service"
rfc_number: 9000
rfc_section: "21.9"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.9: Peer Denial of Service — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, peer_denial_of_service]
---

# 21.9.  Peer Denial of Service


   QUIC and TLS both contain frames or messages that have legitimate
   uses in some contexts, but these frames or messages can be abused to
   cause a peer to expend processing resources without having any
   observable impact on the state of the connection.

   Messages can also be used to change and revert state in small or
   inconsequential ways, such as by sending small increments to flow
   control limits.

   If processing costs are disproportionately large in comparison to
   bandwidth consumption or effect on state, then this could allow a
   malicious peer to exhaust processing capacity.

   While there are legitimate uses for all messages, implementations
> **SHOULD**: SHOULD track cost of processing relative to progress and treat
   excessive quantities of any non-productive packets as indicative of
> **MAY**: an attack.  Endpoints MAY respond to this condition with a connection
   error or by dropping packets.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
