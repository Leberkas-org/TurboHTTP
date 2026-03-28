---
title: "6.4.  RST_STREAM"
rfc_number: 9113
rfc_section: "6.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 6.4: RST_STREAM — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, rst_stream]
---

## 6.4.  RST_STREAM

## 6.4  RST_STREAM

   The RST_STREAM frame (type=0x03) allows for immediate termination of
   a stream.  RST_STREAM is sent to request cancellation of a stream or
   to indicate that an error condition has occurred.

   RST_STREAM Frame {
     Length (24) = 0x04,
     Type (8) = 0x03,

     Unused Flags (8),

     Reserved (1),
     Stream Identifier (31),

     Error Code (32),
   }

                     Figure 6: RST_STREAM Frame Format

   The Length, Type, Unused Flag(s), Reserved, and Stream Identifier
   fields are described in Section 4.  Additionally, the RST_STREAM
   frame contains a single unsigned, 32-bit integer identifying the
   error code (Section 7).  The error code indicates why the stream is
   being terminated.

   The RST_STREAM frame does not define any flags.

   The RST_STREAM frame fully terminates the referenced stream and
   causes it to enter the "closed" state.  After receiving a RST_STREAM
> **MUST NOT**: on a stream, the receiver MUST NOT send additional frames for that
   stream, except for PRIORITY.  However, after sending the RST_STREAM,
> **MUST**: the sending endpoint MUST be prepared to receive and process
   additional frames sent on the stream that might have been sent by the
   peer prior to the arrival of the RST_STREAM.

> **MUST**: RST_STREAM frames MUST be associated with a stream.  If a RST_STREAM
   frame is received with a stream identifier of 0x00, the recipient
> **MUST**: MUST treat this as a connection error (Section 5.4.1) of type
   PROTOCOL_ERROR.

> **MUST NOT**: RST_STREAM frames MUST NOT be sent for a stream in the "idle" state.
   If a RST_STREAM frame identifying an idle stream is received, the
> **MUST**: recipient MUST treat this as a connection error (Section 5.4.1) of
   type PROTOCOL_ERROR.

> **MUST**: A RST_STREAM frame with a length other than 4 octets MUST be treated
   as a connection error (Section 5.4.1) of type FRAME_SIZE_ERROR.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
