---
title: "19.11.  MAX_STREAMS Frames"
rfc_number: 9000
rfc_section: "19.11"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.11: MAX_STREAMS Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, max_streams_frames]
---

# 19.11.  MAX_STREAMS Frames


   A MAX_STREAMS frame (type=0x12 or 0x13) informs the peer of the
   cumulative number of streams of a given type it is permitted to open.
   A MAX_STREAMS frame with a type of 0x12 applies to bidirectional
   streams, and a MAX_STREAMS frame with a type of 0x13 applies to
   unidirectional streams.

   MAX_STREAMS frames are formatted as shown in Figure 35.

   MAX_STREAMS Frame {
     Type (i) = 0x12..0x13,
     Maximum Streams (i),
   }

                    Figure 35: MAX_STREAMS Frame Format

   MAX_STREAMS frames contain the following field:

   Maximum Streams:  A count of the cumulative number of streams of the
      corresponding type that can be opened over the lifetime of the
      connection.  This value cannot exceed 2^60, as it is not possible
      to encode stream IDs larger than 2^62-1.  Receipt of a frame that
> **MUST**: permits opening of a stream larger than this limit MUST be treated
      as a connection error of type FRAME_ENCODING_ERROR.

   Loss or reordering can cause an endpoint to receive a MAX_STREAMS
   frame with a lower stream limit than was previously received.
> **MUST**: MAX_STREAMS frames that do not increase the stream limit MUST be
   ignored.

> **MUST NOT**: An endpoint MUST NOT open more streams than permitted by the current
   stream limit set by its peer.  For instance, a server that receives a
   unidirectional stream limit of 3 is permitted to open streams 3, 7,
> **MUST**: and 11, but not stream 15.  An endpoint MUST terminate a connection
   with an error of type STREAM_LIMIT_ERROR if a peer opens more streams
   than was permitted.  This includes violations of remembered limits in
   Early Data; see Section 7.4.1.

   Note that these frames (and the corresponding transport parameters)
   do not describe the number of streams that can be opened
   concurrently.  The limit includes streams that have been closed as
   well as those that are open.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
