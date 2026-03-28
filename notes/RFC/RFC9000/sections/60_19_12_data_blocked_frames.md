---
title: "19.12.  DATA_BLOCKED Frames"
rfc_number: 9000
rfc_section: "19.12"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.12: DATA_BLOCKED Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, data_blocked_frames]
---

# 19.12.  DATA_BLOCKED Frames


> **SHOULD**: A sender SHOULD send a DATA_BLOCKED frame (type=0x14) when it wishes
   to send data but is unable to do so due to connection-level flow
   control; see Section 4.  DATA_BLOCKED frames can be used as input to
   tuning of flow control algorithms; see Section 4.2.

   DATA_BLOCKED frames are formatted as shown in Figure 36.

   DATA_BLOCKED Frame {
     Type (i) = 0x14,
     Maximum Data (i),
   }

                    Figure 36: DATA_BLOCKED Frame Format

   DATA_BLOCKED frames contain the following field:

   Maximum Data:  A variable-length integer indicating the connection-
      level limit at which blocking occurred.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
