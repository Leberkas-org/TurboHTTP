---
title: "19.13.  STREAM_DATA_BLOCKED Frames"
rfc_number: 9000
rfc_section: "19.13"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.13: STREAM_DATA_BLOCKED Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, stream_data_blocked_frames]
---

# 19.13.  STREAM_DATA_BLOCKED Frames


> **SHOULD**: A sender SHOULD send a STREAM_DATA_BLOCKED frame (type=0x15) when it
   wishes to send data but is unable to do so due to stream-level flow
   control.  This frame is analogous to DATA_BLOCKED (Section 19.12).

   An endpoint that receives a STREAM_DATA_BLOCKED frame for a send-only
> **MUST**: stream MUST terminate the connection with error STREAM_STATE_ERROR.

   STREAM_DATA_BLOCKED frames are formatted as shown in Figure 37.

   STREAM_DATA_BLOCKED Frame {
     Type (i) = 0x15,
     Stream ID (i),
     Maximum Stream Data (i),
   }

                Figure 37: STREAM_DATA_BLOCKED Frame Format

   STREAM_DATA_BLOCKED frames contain the following fields:

   Stream ID:  A variable-length integer indicating the stream that is
      blocked due to flow control.

   Maximum Stream Data:  A variable-length integer indicating the offset
      of the stream at which the blocking occurred.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
