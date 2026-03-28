---
title: "19.17.  PATH_CHALLENGE Frames"
rfc_number: 9000
rfc_section: "19.17"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.17: PATH_CHALLENGE Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, path_challenge_frames]
---

# 19.17.  PATH_CHALLENGE Frames


   Endpoints can use PATH_CHALLENGE frames (type=0x1a) to check
   reachability to the peer and for path validation during connection
   migration.

   PATH_CHALLENGE frames are formatted as shown in Figure 41.

   PATH_CHALLENGE Frame {
     Type (i) = 0x1a,
     Data (64),
   }

                   Figure 41: PATH_CHALLENGE Frame Format

   PATH_CHALLENGE frames contain the following field:

   Data:  This 8-byte field contains arbitrary data.

   Including 64 bits of entropy in a PATH_CHALLENGE frame ensures that
   it is easier to receive the packet than it is to guess the value
   correctly.

> **MUST**: The recipient of this frame MUST generate a PATH_RESPONSE frame
   (Section 19.18) containing the same Data value.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
