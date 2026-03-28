---
title: "21.2.  Handshake Denial of Service"
rfc_number: 9000
rfc_section: "21.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.2: Handshake Denial of Service — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, handshake_denial_of_service]
---

# 21.2.  Handshake Denial of Service


   As an encrypted and authenticated transport, QUIC provides a range of
   protections against denial of service.  Once the cryptographic
   handshake is complete, QUIC endpoints discard most packets that are
   not authenticated, greatly limiting the ability of an attacker to
   interfere with existing connections.

   Once a connection is established, QUIC endpoints might accept some
   unauthenticated ICMP packets (see Section 14.2.1), but the use of
   these packets is extremely limited.  The only other type of packet
   that an endpoint might accept is a stateless reset (Section 10.3),
   which relies on the token being kept secret until it is used.

   During the creation of a connection, QUIC only provides protection
   against attacks from off the network path.  All QUIC packets contain
   proof that the recipient saw a preceding packet from its peer.

   Addresses cannot change during the handshake, so endpoints can
   discard packets that are received on a different network path.

   The Source and Destination Connection ID fields are the primary means
   of protection against an off-path attack during the handshake; see
   Section 8.1.  These are required to match those set by a peer.
   Except for Initial and Stateless Resets, an endpoint only accepts
   packets that include a Destination Connection ID field that matches a
   value the endpoint previously chose.  This is the only protection
   offered for Version Negotiation packets.

   The Destination Connection ID field in an Initial packet is selected
   by a client to be unpredictable, which serves an additional purpose.
   The packets that carry the cryptographic handshake are protected with
   a key that is derived from this connection ID and a salt specific to
   the QUIC version.  This allows endpoints to use the same process for
   authenticating packets that they receive as they use after the
   cryptographic handshake completes.  Packets that cannot be
   authenticated are discarded.  Protecting packets in this fashion
   provides a strong assurance that the sender of the packet saw the
   Initial packet and understood it.

   These protections are not intended to be effective against an
   attacker that is able to receive QUIC packets prior to the connection
   being established.  Such an attacker can potentially send packets
   that will be accepted by QUIC endpoints.  This version of QUIC
   attempts to detect this sort of attack, but it expects that endpoints
   will fail to establish a connection rather than recovering.  For the
   most part, the cryptographic handshake protocol [QUIC-TLS] is
   responsible for detecting tampering during the handshake.

   Endpoints are permitted to use other methods to detect and attempt to
   recover from interference with the handshake.  Invalid packets can be
   identified and discarded using other methods, but no specific method
   is mandated in this document.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
