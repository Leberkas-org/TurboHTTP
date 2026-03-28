---
title: "19.1.  PADDING Frames"
rfc_number: 9000
rfc_section: "19.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.1: PADDING Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, padding_frames]
---

# 19.1.  PADDING Frames



   As described in Section 12.4, packets contain one or more frames.
   This section describes the format and semantics of the core QUIC
   frame types.

## 19.1.  PADDING Frames

   A PADDING frame (type=0x00) has no semantic value.  PADDING frames
   can be used to increase the size of a packet.  Padding can be used to
   increase an Initial packet to the minimum required size or to provide
   protection against traffic analysis for protected packets.

   PADDING frames are formatted as shown in Figure 23, which shows that
   PADDING frames have no content.  That is, a PADDING frame consists of
   the single byte that identifies the frame as a PADDING frame.

   PADDING Frame {
     Type (i) = 0x00,
   }

                      Figure 23: PADDING Frame Format

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
