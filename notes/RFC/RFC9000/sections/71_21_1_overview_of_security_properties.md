---
title: "21.1.  Overview of Security Properties"
rfc_number: 9000
rfc_section: "21.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.1: Overview of Security Properties — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, overview_of_security_properties]
---

# 21.1.  Overview of Security Properties



   The goal of QUIC is to provide a secure transport connection.
   Section 21.1 provides an overview of those properties; subsequent
   sections discuss constraints and caveats regarding these properties,
   including descriptions of known attacks and countermeasures.

## 21.1.  Overview of Security Properties

   A complete security analysis of QUIC is outside the scope of this
   document.  This section provides an informal description of the
   desired security properties as an aid to implementers and to help
   guide protocol analysis.

   QUIC assumes the threat model described in [SEC-CONS] and provides
   protections against many of the attacks that arise from that model.

   For this purpose, attacks are divided into passive and active
   attacks.  Passive attackers have the ability to read packets from the
   network, while active attackers also have the ability to write
   packets into the network.  However, a passive attack could involve an
   attacker with the ability to cause a routing change or other
   modification in the path taken by packets that comprise a connection.

   Attackers are additionally categorized as either on-path attackers or
   off-path attackers.  An on-path attacker can read, modify, or remove
   any packet it observes such that the packet no longer reaches its
   destination, while an off-path attacker observes the packets but
   cannot prevent the original packet from reaching its intended
   destination.  Both types of attackers can also transmit arbitrary
   packets.  This definition differs from that of Section 3.5 of
   [SEC-CONS] in that an off-path attacker is able to observe packets.

   Properties of the handshake, protected packets, and connection
   migration are considered separately.

### 21.1.1.  Handshake

   The QUIC handshake incorporates the TLS 1.3 handshake and inherits
   the cryptographic properties described in Appendix E.1 of [TLS13].
   Many of the security properties of QUIC depend on the TLS handshake
   providing these properties.  Any attack on the TLS handshake could
   affect QUIC.

   Any attack on the TLS handshake that compromises the secrecy or
   uniqueness of session keys, or the authentication of the
   participating peers, affects other security guarantees provided by
   QUIC that depend on those keys.  For instance, migration (Section 9)
   depends on the efficacy of confidentiality protections, both for the
   negotiation of keys using the TLS handshake and for QUIC packet
   protection, to avoid linkability across network paths.

   An attack on the integrity of the TLS handshake might allow an
   attacker to affect the selection of application protocol or QUIC
   version.

   In addition to the properties provided by TLS, the QUIC handshake
   provides some defense against DoS attacks on the handshake.

### 21.1.1.1.  Anti-Amplification

   Address validation (Section 8) is used to verify that an entity that
   claims a given address is able to receive packets at that address.
   Address validation limits amplification attack targets to addresses
   for which an attacker can observe packets.

   Prior to address validation, endpoints are limited in what they are
   able to send.  Endpoints cannot send data toward an unvalidated
   address in excess of three times the data received from that address.

      |  Note: The anti-amplification limit only applies when an
      |  endpoint responds to packets received from an unvalidated
      |  address.  The anti-amplification limit does not apply to
      |  clients when establishing a new connection or when initiating
      |  connection migration.

### 21.1.1.2.  Server-Side DoS

   Computing the server's first flight for a full handshake is
   potentially expensive, requiring both a signature and a key exchange
   computation.  In order to prevent computational DoS attacks, the
   Retry packet provides a cheap token exchange mechanism that allows
   servers to validate a client's IP address prior to doing any
   expensive computations at the cost of a single round trip.  After a
   successful handshake, servers can issue new tokens to a client, which
   will allow new connection establishment without incurring this cost.

### 21.1.1.3.  On-Path Handshake Termination

   An on-path or off-path attacker can force a handshake to fail by
   replacing or racing Initial packets.  Once valid Initial packets have
   been exchanged, subsequent Handshake packets are protected with the
   Handshake keys, and an on-path attacker cannot force handshake
   failure other than by dropping packets to cause endpoints to abandon
   the attempt.

   An on-path attacker can also replace the addresses of packets on
   either side and therefore cause the client or server to have an
   incorrect view of the remote addresses.  Such an attack is
   indistinguishable from the functions performed by a NAT.

### 21.1.1.4.  Parameter Negotiation

   The entire handshake is cryptographically protected, with the Initial
   packets being encrypted with per-version keys and the Handshake and
   later packets being encrypted with keys derived from the TLS key
   exchange.  Further, parameter negotiation is folded into the TLS
   transcript and thus provides the same integrity guarantees as
   ordinary TLS negotiation.  An attacker can observe the client's
   transport parameters (as long as it knows the version-specific salt)
   but cannot observe the server's transport parameters and cannot
   influence parameter negotiation.

   Connection IDs are unencrypted but integrity protected in all
   packets.

   This version of QUIC does not incorporate a version negotiation
   mechanism; implementations of incompatible versions will simply fail
   to establish a connection.

### 21.1.2.  Protected Packets

   Packet protection (Section 12.1) applies authenticated encryption to
   all packets except Version Negotiation packets, though Initial and
   Retry packets have limited protection due to the use of version-
   specific keying material; see [QUIC-TLS] for more details.  This
   section considers passive and active attacks against protected
   packets.

   Both on-path and off-path attackers can mount a passive attack in
   which they save observed packets for an offline attack against packet
   protection at a future time; this is true for any observer of any
   packet on any network.

   An attacker that injects packets without being able to observe valid
   packets for a connection is unlikely to be successful, since packet
   protection ensures that valid packets are only generated by endpoints
   that possess the key material established during the handshake; see
   Sections 7 and 21.1.1.  Similarly, any active attacker that observes
   packets and attempts to insert new data or modify existing data in
   those packets should not be able to generate packets deemed valid by
   the receiving endpoint, other than Initial packets.

   A spoofing attack, in which an active attacker rewrites unprotected
   parts of a packet that it forwards or injects, such as the source or
   destination address, is only effective if the attacker can forward
   packets to the original endpoint.  Packet protection ensures that the
   packet payloads can only be processed by the endpoints that completed
   the handshake, and invalid packets are ignored by those endpoints.

   An attacker can also modify the boundaries between packets and UDP
   datagrams, causing multiple packets to be coalesced into a single
   datagram or splitting coalesced packets into multiple datagrams.
   Aside from datagrams containing Initial packets, which require
   padding, modification of how packets are arranged in datagrams has no
   functional effect on a connection, although it might change some
   performance characteristics.

### 21.1.3.  Connection Migration

   Connection migration (Section 9) provides endpoints with the ability
   to transition between IP addresses and ports on multiple paths, using
   one path at a time for transmission and receipt of non-probing
   frames.  Path validation (Section 8.2) establishes that a peer is
   both willing and able to receive packets sent on a particular path.
   This helps reduce the effects of address spoofing by limiting the
   number of packets sent to a spoofed address.

   This section describes the intended security properties of connection
   migration under various types of DoS attacks.

### 21.1.3.1.  On-Path Active Attacks

   An attacker that can cause a packet it observes to no longer reach
   its intended destination is considered an on-path attacker.  When an
   attacker is present between a client and server, endpoints are
   required to send packets through the attacker to establish
   connectivity on a given path.

   An on-path attacker can:

   *  Inspect packets

   *  Modify IP and UDP packet headers

   *  Inject new packets

   *  Delay packets

   *  Reorder packets

   *  Drop packets

   *  Split and merge datagrams along packet boundaries

   An on-path attacker cannot:

   *  Modify an authenticated portion of a packet and cause the
      recipient to accept that packet

   An on-path attacker has the opportunity to modify the packets that it
   observes; however, any modifications to an authenticated portion of a
   packet will cause it to be dropped by the receiving endpoint as
   invalid, as packet payloads are both authenticated and encrypted.

   QUIC aims to constrain the capabilities of an on-path attacker as
   follows:

   1.  An on-path attacker can prevent the use of a path for a
       connection, causing the connection to fail if it cannot use a
       different path that does not contain the attacker.  This can be
       achieved by dropping all packets, modifying them so that they
       fail to decrypt, or other methods.

   2.  An on-path attacker can prevent migration to a new path for which
       the attacker is also on-path by causing path validation to fail
       on the new path.

   3.  An on-path attacker cannot prevent a client from migrating to a
       path for which the attacker is not on-path.

   4.  An on-path attacker can reduce the throughput of a connection by
       delaying packets or dropping them.

   5.  An on-path attacker cannot cause an endpoint to accept a packet
       for which it has modified an authenticated portion of that
       packet.

### 21.1.3.2.  Off-Path Active Attacks

   An off-path attacker is not directly on the path between a client and
   server but could be able to obtain copies of some or all packets sent
   between the client and the server.  It is also able to send copies of
   those packets to either endpoint.

   An off-path attacker can:

   *  Inspect packets

   *  Inject new packets

   *  Reorder injected packets

   An off-path attacker cannot:

   *  Modify packets sent by endpoints

   *  Delay packets

   *  Drop packets

   *  Reorder original packets

   An off-path attacker can create modified copies of packets that it
   has observed and inject those copies into the network, potentially
   with spoofed source and destination addresses.

   For the purposes of this discussion, it is assumed that an off-path
   attacker has the ability to inject a modified copy of a packet into
   the network that will reach the destination endpoint prior to the
   arrival of the original packet observed by the attacker.  In other
   words, an attacker has the ability to consistently "win" a race with
   the legitimate packets between the endpoints, potentially causing the
   original packet to be ignored by the recipient.

   It is also assumed that an attacker has the resources necessary to
   affect NAT state.  In particular, an attacker can cause an endpoint
   to lose its NAT binding and then obtain the same port for use with
   its own traffic.

   QUIC aims to constrain the capabilities of an off-path attacker as
   follows:

   1.  An off-path attacker can race packets and attempt to become a
       "limited" on-path attacker.

   2.  An off-path attacker can cause path validation to succeed for
       forwarded packets with the source address listed as the off-path
       attacker as long as it can provide improved connectivity between
       the client and the server.

   3.  An off-path attacker cannot cause a connection to close once the
       handshake has completed.

   4.  An off-path attacker cannot cause migration to a new path to fail
       if it cannot observe the new path.

   5.  An off-path attacker can become a limited on-path attacker during
       migration to a new path for which it is also an off-path
       attacker.

   6.  An off-path attacker can become a limited on-path attacker by
       affecting shared NAT state such that it sends packets to the
       server from the same IP address and port that the client
       originally used.

### 21.1.3.3.  Limited On-Path Active Attacks

   A limited on-path attacker is an off-path attacker that has offered
   improved routing of packets by duplicating and forwarding original
   packets between the server and the client, causing those packets to
   arrive before the original copies such that the original packets are
   dropped by the destination endpoint.

   A limited on-path attacker differs from an on-path attacker in that
   it is not on the original path between endpoints, and therefore the
   original packets sent by an endpoint are still reaching their
   destination.  This means that a future failure to route copied
   packets to the destination faster than their original path will not
   prevent the original packets from reaching the destination.

   A limited on-path attacker can:

   *  Inspect packets

   *  Inject new packets

   *  Modify unencrypted packet headers

   *  Reorder packets

   A limited on-path attacker cannot:

   *  Delay packets so that they arrive later than packets sent on the
      original path

   *  Drop packets

   *  Modify the authenticated and encrypted portion of a packet and
      cause the recipient to accept that packet

   A limited on-path attacker can only delay packets up to the point
   that the original packets arrive before the duplicate packets,
   meaning that it cannot offer routing with worse latency than the
   original path.  If a limited on-path attacker drops packets, the
   original copy will still arrive at the destination endpoint.

   QUIC aims to constrain the capabilities of a limited off-path
   attacker as follows:

   1.  A limited on-path attacker cannot cause a connection to close
       once the handshake has completed.

   2.  A limited on-path attacker cannot cause an idle connection to
       close if the client is first to resume activity.

   3.  A limited on-path attacker can cause an idle connection to be
       deemed lost if the server is the first to resume activity.

   Note that these guarantees are the same guarantees provided for any
   NAT, for the same reasons.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
