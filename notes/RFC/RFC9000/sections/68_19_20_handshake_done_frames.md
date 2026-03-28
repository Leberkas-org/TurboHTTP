---
title: "19.20.  HANDSHAKE_DONE Frames"
rfc_number: 9000
rfc_section: "19.20"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.20: HANDSHAKE_DONE Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, handshake_done_frames]
---

# 19.20.  HANDSHAKE_DONE Frames


   The server uses a HANDSHAKE_DONE frame (type=0x1e) to signal
   confirmation of the handshake to the client.

   HANDSHAKE_DONE frames are formatted as shown in Figure 44, which
   shows that HANDSHAKE_DONE frames have no content.

   HANDSHAKE_DONE Frame {
     Type (i) = 0x1e,
   }

                   Figure 44: HANDSHAKE_DONE Frame Format

> **MUST**: A HANDSHAKE_DONE frame can only be sent by the server.  Servers MUST
   NOT send a HANDSHAKE_DONE frame before completing the handshake.  A
> **MUST**: server MUST treat receipt of a HANDSHAKE_DONE frame as a connection
   error of type PROTOCOL_VIOLATION.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
