---
title: "19.18.  PATH_RESPONSE Frames"
rfc_number: 9000
rfc_section: "19.18"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.18: PATH_RESPONSE Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, path_response_frames]
---

# 19.18.  PATH_RESPONSE Frames


   A PATH_RESPONSE frame (type=0x1b) is sent in response to a
   PATH_CHALLENGE frame.

   PATH_RESPONSE frames are formatted as shown in Figure 42.  The format
   of a PATH_RESPONSE frame is identical to that of the PATH_CHALLENGE
   frame; see Section 19.17.

   PATH_RESPONSE Frame {
     Type (i) = 0x1b,
     Data (64),
   }

                   Figure 42: PATH_RESPONSE Frame Format

   If the content of a PATH_RESPONSE frame does not match the content of
   a PATH_CHALLENGE frame previously sent by the endpoint, the endpoint
> **MAY**: MAY generate a connection error of type PROTOCOL_VIOLATION.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
