---
title: "19.2.  PING Frames"
rfc_number: 9000
rfc_section: "19.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.2: PING Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, ping_frames]
---

# 19.2.  PING Frames


   Endpoints can use PING frames (type=0x01) to verify that their peers
   are still alive or to check reachability to the peer.

   PING frames are formatted as shown in Figure 24, which shows that
   PING frames have no content.

   PING Frame {
     Type (i) = 0x01,
   }

                        Figure 24: PING Frame Format

   The receiver of a PING frame simply needs to acknowledge the packet
   containing this frame.

   The PING frame can be used to keep a connection alive when an
   application or application protocol wishes to prevent the connection
   from timing out; see Section 10.1.2.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
