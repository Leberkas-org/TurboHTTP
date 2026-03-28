---
title: "21.14.  Traffic Analysis"
rfc_number: 9000
rfc_section: "21.14"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.14: Traffic Analysis — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, traffic_analysis]
---

# 21.14.  Traffic Analysis


   The length of QUIC packets can reveal information about the length of
   the content of those packets.  The PADDING frame is provided so that
   endpoints have some ability to obscure the length of packet content;
   see Section 19.1.

   Defeating traffic analysis is challenging and the subject of active
   research.  Length is not the only way that information might leak.
   Endpoints might also reveal sensitive information through other side
   channels, such as the timing of packets.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
