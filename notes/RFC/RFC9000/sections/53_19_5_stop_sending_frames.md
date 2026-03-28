---
title: "19.5.  STOP_SENDING Frames"
rfc_number: 9000
rfc_section: "19.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.5: STOP_SENDING Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, stop_sending_frames]
---

# 19.5.  STOP_SENDING Frames


   An endpoint uses a STOP_SENDING frame (type=0x05) to communicate that
   incoming data is being discarded on receipt per application request.
   STOP_SENDING requests that a peer cease transmission on a stream.

   A STOP_SENDING frame can be sent for streams in the "Recv" or "Size
   Known" states; see Section 3.2.  Receiving a STOP_SENDING frame for a
> **MUST**: locally initiated stream that has not yet been created MUST be
   treated as a connection error of type STREAM_STATE_ERROR.  An
   endpoint that receives a STOP_SENDING frame for a receive-only stream
> **MUST**: MUST terminate the connection with error STREAM_STATE_ERROR.

   STOP_SENDING frames are formatted as shown in Figure 29.

   STOP_SENDING Frame {
     Type (i) = 0x05,
     Stream ID (i),
     Application Protocol Error Code (i),
   }

                    Figure 29: STOP_SENDING Frame Format

   STOP_SENDING frames contain the following fields:

   Stream ID:  A variable-length integer carrying the stream ID of the
      stream being ignored.

   Application Protocol Error Code:  A variable-length integer
      containing the application-specified reason the sender is ignoring
      the stream; see Section 20.2.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
