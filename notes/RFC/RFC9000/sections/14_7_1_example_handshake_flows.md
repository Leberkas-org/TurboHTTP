---
title: "7.1.  Example Handshake Flows"
rfc_number: 9000
rfc_section: "7.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 7.1: Example Handshake Flows — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, example_handshake_flows]
---

# 7.1.  Example Handshake Flows



   QUIC relies on a combined cryptographic and transport handshake to
   minimize connection establishment latency.  QUIC uses the CRYPTO
   frame (Section 19.6) to transmit the cryptographic handshake.  The
   version of QUIC defined in this document is identified as 0x00000001
   and uses TLS as described in [QUIC-TLS]; a different QUIC version
   could indicate that a different cryptographic handshake protocol is
   in use.

   QUIC provides reliable, ordered delivery of the cryptographic
   handshake data.  QUIC packet protection is used to encrypt as much of
> **MUST**: the handshake protocol as possible.  The cryptographic handshake MUST
   provide the following properties:

   *  authenticated key exchange, where

      -  a server is always authenticated,

      -  a client is optionally authenticated,

      -  every connection produces distinct and unrelated keys, and

      -  keying material is usable for packet protection for both 0-RTT
         and 1-RTT packets.

   *  authenticated exchange of values for transport parameters of both
      endpoints, and confidentiality protection for server transport
      parameters (see Section 7.4).

   *  authenticated negotiation of an application protocol (TLS uses
      Application-Layer Protocol Negotiation (ALPN) [ALPN] for this
      purpose).

   The CRYPTO frame can be sent in different packet number spaces
   (Section 12.3).  The offsets used by CRYPTO frames to ensure ordered
   delivery of cryptographic handshake data start from zero in each
   packet number space.

   Figure 4 shows a simplified handshake and the exchange of packets and
   frames that are used to advance the handshake.  Exchange of
   application data during the handshake is enabled where possible,
   shown with an asterisk ("*").  Once the handshake is complete,
   endpoints are able to exchange application data freely.

   Client                                               Server

   Initial (CRYPTO)
   0-RTT (*)              ---------->
                                              Initial (CRYPTO)
                                            Handshake (CRYPTO)
                          <----------                1-RTT (*)
   Handshake (CRYPTO)
   1-RTT (*)              ---------->
                          <----------   1-RTT (HANDSHAKE_DONE)

   1-RTT                  <=========>                    1-RTT

                    Figure 4: Simplified QUIC Handshake

   Endpoints can use packets sent during the handshake to test for
   Explicit Congestion Notification (ECN) support; see Section 13.4.  An
   endpoint validates support for ECN by observing whether the ACK
   frames acknowledging the first packets it sends carry ECN counts, as
   described in Section 13.4.2.

> **MUST**: Endpoints MUST explicitly negotiate an application protocol.  This
   avoids situations where there is a disagreement about the protocol
   that is in use.

## 7.1.  Example Handshake Flows

   Details of how TLS is integrated with QUIC are provided in
   [QUIC-TLS], but some examples are provided here.  An extension of
   this exchange to support client address validation is shown in
   Section 8.1.2.

   Once any address validation exchanges are complete, the cryptographic
   handshake is used to agree on cryptographic keys.  The cryptographic
   handshake is carried in Initial (Section 17.2.2) and Handshake
   (Section 17.2.4) packets.

   Figure 5 provides an overview of the 1-RTT handshake.  Each line
   shows a QUIC packet with the packet type and packet number shown
   first, followed by the frames that are typically contained in those
   packets.  For instance, the first packet is of type Initial, with
   packet number 0, and contains a CRYPTO frame carrying the
   ClientHello.

   Multiple QUIC packets -- even of different packet types -- can be
   coalesced into a single UDP datagram; see Section 12.2.  As a result,
   this handshake could consist of as few as four UDP datagrams, or any
   number more (subject to limits inherent to the protocol, such as
   congestion control and anti-amplification).  For instance, the
   server's first flight contains Initial packets, Handshake packets,
   and "0.5-RTT data" in 1-RTT packets.

   Client                                                  Server

   Initial[0]: CRYPTO[CH] ->

                                    Initial[0]: CRYPTO[SH] ACK[0]
                          Handshake[0]: CRYPTO[EE, CERT, CV, FIN]
                                    <- 1-RTT[0]: STREAM[1, "..."]

   Initial[1]: ACK[0]
   Handshake[0]: CRYPTO[FIN], ACK[0]
   1-RTT[0]: STREAM[0, "..."], ACK[0] ->

                                             Handshake[1]: ACK[0]
            <- 1-RTT[1]: HANDSHAKE_DONE, STREAM[3, "..."], ACK[0]

                     Figure 5: Example 1-RTT Handshake

   Figure 6 shows an example of a connection with a 0-RTT handshake and
   a single packet of 0-RTT data.  Note that as described in
   Section 12.3, the server acknowledges 0-RTT data in 1-RTT packets,
   and the client sends 1-RTT packets in the same packet number space.

   Client                                                  Server

   Initial[0]: CRYPTO[CH]
   0-RTT[0]: STREAM[0, "..."] ->

                                    Initial[0]: CRYPTO[SH] ACK[0]
                                     Handshake[0] CRYPTO[EE, FIN]
                             <- 1-RTT[0]: STREAM[1, "..."] ACK[0]

   Initial[1]: ACK[0]
   Handshake[0]: CRYPTO[FIN], ACK[0]
   1-RTT[1]: STREAM[0, "..."] ACK[0] ->

                                             Handshake[1]: ACK[0]
            <- 1-RTT[1]: HANDSHAKE_DONE, STREAM[3, "..."], ACK[1]

                     Figure 6: Example 0-RTT Handshake

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
