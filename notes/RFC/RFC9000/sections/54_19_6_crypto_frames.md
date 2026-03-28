---
title: "19.6.  CRYPTO Frames"
rfc_number: 9000
rfc_section: "19.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 19.6: CRYPTO Frames — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, crypto_frames]
---

# 19.6.  CRYPTO Frames


   A CRYPTO frame (type=0x06) is used to transmit cryptographic
   handshake messages.  It can be sent in all packet types except 0-RTT.
   The CRYPTO frame offers the cryptographic protocol an in-order stream
   of bytes.  CRYPTO frames are functionally identical to STREAM frames,
   except that they do not bear a stream identifier; they are not flow
   controlled; and they do not carry markers for optional offset,
   optional length, and the end of the stream.

   CRYPTO frames are formatted as shown in Figure 30.

   CRYPTO Frame {
     Type (i) = 0x06,
     Offset (i),
     Length (i),
     Crypto Data (..),
   }

                       Figure 30: CRYPTO Frame Format

   CRYPTO frames contain the following fields:

   Offset:  A variable-length integer specifying the byte offset in the
      stream for the data in this CRYPTO frame.

   Length:  A variable-length integer specifying the length of the
      Crypto Data field in this CRYPTO frame.

   Crypto Data:  The cryptographic message data.

   There is a separate flow of cryptographic handshake data in each
   encryption level, each of which starts at an offset of 0.  This
   implies that each encryption level is treated as a separate CRYPTO
   stream of data.

   The largest offset delivered on a stream -- the sum of the offset and
   data length -- cannot exceed 2^62-1.  Receipt of a frame that exceeds
> **MUST**: this limit MUST be treated as a connection error of type
   FRAME_ENCODING_ERROR or CRYPTO_BUFFER_EXCEEDED.

   Unlike STREAM frames, which include a stream ID indicating to which
   stream the data belongs, the CRYPTO frame carries data for a single
   stream per encryption level.  The stream does not have an explicit
   end, so CRYPTO frames do not have a FIN bit.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
