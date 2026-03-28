---
title: "17.1.  Packet Number Encoding and Decoding"
rfc_number: 9000
rfc_section: "17.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 17.1: Packet Number Encoding and Decoding — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, packet_number_encoding_and_decoding]
---

# 17.1.  Packet Number Encoding and Decoding



   All numeric values are encoded in network byte order (that is, big
   endian), and all field sizes are in bits.  Hexadecimal notation is
   used for describing the value of fields.

## 17.1.  Packet Number Encoding and Decoding

   Packet numbers are integers in the range 0 to 2^62-1 (Section 12.3).
   When present in long or short packet headers, they are encoded in 1
   to 4 bytes.  The number of bits required to represent the packet
   number is reduced by including only the least significant bits of the
   packet number.

   The encoded packet number is protected as described in Section 5.4 of
   [QUIC-TLS].

   Prior to receiving an acknowledgment for a packet number space, the
> **MUST**: full packet number MUST be included; it is not to be truncated, as
   described below.

   After an acknowledgment is received for a packet number space, the
> **MUST**: sender MUST use a packet number size able to represent more than
   twice as large a range as the difference between the largest
   acknowledged packet number and the packet number being sent.  A peer
   receiving the packet will then correctly decode the packet number,
   unless the packet is delayed in transit such that it arrives after
> **SHOULD**: many higher-numbered packets have been received.  An endpoint SHOULD
   use a large enough packet number encoding to allow the packet number
   to be recovered even if the packet arrives after packets that are
   sent afterwards.

   As a result, the size of the packet number encoding is at least one
   bit more than the base-2 logarithm of the number of contiguous
   unacknowledged packet numbers, including the new packet.  Pseudocode
   and an example for packet number encoding can be found in
   Appendix A.2.

   At a receiver, protection of the packet number is removed prior to
   recovering the full packet number.  The full packet number is then
   reconstructed based on the number of significant bits present, the
   value of those bits, and the largest packet number received in a
   successfully authenticated packet.  Recovering the full packet number
   is necessary to successfully complete the removal of packet
   protection.

   Once header protection is removed, the packet number is decoded by
   finding the packet number value that is closest to the next expected
   packet.  The next expected packet is the highest received packet
   number plus one.  Pseudocode and an example for packet number
   decoding can be found in Appendix A.3.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
