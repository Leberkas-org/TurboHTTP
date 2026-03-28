---
title: "12.1.  Protected Packets"
rfc_number: 9000
rfc_section: "12.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 12.1: Protected Packets — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, protected_packets]
---

# 12.1.  Protected Packets



   QUIC endpoints communicate by exchanging packets.  Packets have
   confidentiality and integrity protection; see Section 12.1.  Packets
   are carried in UDP datagrams; see Section 12.2.

   This version of QUIC uses the long packet header during connection
   establishment; see Section 17.2.  Packets with the long header are
   Initial (Section 17.2.2), 0-RTT (Section 17.2.3), Handshake
   (Section 17.2.4), and Retry (Section 17.2.5).  Version negotiation
   uses a version-independent packet with a long header; see
   Section 17.2.1.

   Packets with the short header are designed for minimal overhead and
   are used after a connection is established and 1-RTT keys are
   available; see Section 17.3.

## 12.1.  Protected Packets

   QUIC packets have different levels of cryptographic protection based
   on the type of packet.  Details of packet protection are found in
   [QUIC-TLS]; this section includes an overview of the protections that
   are provided.

   Version Negotiation packets have no cryptographic protection; see
   [QUIC-INVARIANTS].

   Retry packets use an AEAD function [AEAD] to protect against
   accidental modification.

   Initial packets use an AEAD function, the keys for which are derived
   using a value that is visible on the wire.  Initial packets therefore
   do not have effective confidentiality protection.  Initial protection
   exists to ensure that the sender of the packet is on the network
   path.  Any entity that receives an Initial packet from a client can
   recover the keys that will allow them to both read the contents of
   the packet and generate Initial packets that will be successfully
   authenticated at either endpoint.  The AEAD also protects Initial
   packets against accidental modification.

   All other packets are protected with keys derived from the
   cryptographic handshake.  The cryptographic handshake ensures that
   only the communicating endpoints receive the corresponding keys for
   Handshake, 0-RTT, and 1-RTT packets.  Packets protected with 0-RTT
   and 1-RTT keys have strong confidentiality and integrity protection.

   The Packet Number field that appears in some packet types has
   alternative confidentiality protection that is applied as part of
   header protection; see Section 5.4 of [QUIC-TLS] for details.  The
   underlying packet number increases with each packet sent in a given
   packet number space; see Section 12.3 for details.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
