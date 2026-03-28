---
title: "12.5.  Frames and Number Spaces"
rfc_number: 9000
rfc_section: "12.5"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 12.5: Frames and Number Spaces — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, frames_and_number_spaces]
---

# 12.5.  Frames and Number Spaces


   Some frames are prohibited in different packet number spaces.  The
   rules here generalize those of TLS, in that frames associated with
   establishing the connection can usually appear in packets in any
   packet number space, whereas those associated with transferring data
   can only appear in the application data packet number space:

> **MAY**: *  PADDING, PING, and CRYPTO frames MAY appear in any packet number
      space.

   *  CONNECTION_CLOSE frames signaling errors at the QUIC layer (type
> **MAY**: 0x1c) MAY appear in any packet number space.  CONNECTION_CLOSE
   frames signaling application errors (type 0x1d) MUST only appear
      in the application data packet number space.

> **MAY**: *  ACK frames MAY appear in any packet number space but can only
      acknowledge packets that appeared in that packet number space.
      However, as noted below, 0-RTT packets cannot contain ACK frames.

> **MUST**: *  All other frame types MUST only be sent in the application data
      packet number space.

   Note that it is not possible to send the following frames in 0-RTT
   packets for various reasons: ACK, CRYPTO, HANDSHAKE_DONE, NEW_TOKEN,
> **MAY**: PATH_RESPONSE, and RETIRE_CONNECTION_ID.  A server MAY treat receipt
   of these frames in 0-RTT packets as a connection error of type
   PROTOCOL_VIOLATION.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
