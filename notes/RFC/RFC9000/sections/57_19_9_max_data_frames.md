---
title: "19.9.  MAX_DATA Frames"
rfc_number: 9000
rfc_section: "19.9"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.9: MAX_DATA Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, max_data_frames]
---

# 19.9.  MAX_DATA Frames


   A MAX_DATA frame (type=0x10) is used in flow control to inform the
   peer of the maximum amount of data that can be sent on the connection
   as a whole.

   MAX_DATA frames are formatted as shown in Figure 33.

   MAX_DATA Frame {
     Type (i) = 0x10,
     Maximum Data (i),
   }

                      Figure 33: MAX_DATA Frame Format

   MAX_DATA frames contain the following field:

   Maximum Data:  A variable-length integer indicating the maximum
      amount of data that can be sent on the entire connection, in units
      of bytes.

   All data sent in STREAM frames counts toward this limit.  The sum of
   the final sizes on all streams -- including streams in terminal
> **MUST NOT**: states -- MUST NOT exceed the value advertised by a receiver.  An
   endpoint MUST terminate a connection with an error of type
   FLOW_CONTROL_ERROR if it receives more data than the maximum data
   value that it has sent.  This includes violations of remembered
   limits in Early Data; see Section 7.4.1.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
