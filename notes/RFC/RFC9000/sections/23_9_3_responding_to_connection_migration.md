---
title: "9.3.  Responding to Connection Migration"
rfc_number: 9000
rfc_section: "9.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 9.3: Responding to Connection Migration — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, responding_to_connection_migration]
---

# 9.3.  Responding to Connection Migration


   Receiving a packet from a new peer address containing a non-probing
   frame indicates that the peer has migrated to that address.

> **MUST**: If the recipient permits the migration, it MUST send subsequent
   packets to the new peer address and MUST initiate path validation
   (Section 8.2) to verify the peer's ownership of the address if
   validation is not already underway.  If the recipient has no unused
   connection IDs from the peer, it will not be able to send anything on
   the new path until the peer provides one; see Section 9.5.

   An endpoint only changes the address to which it sends packets in
   response to the highest-numbered non-probing packet.  This ensures
   that an endpoint does not send packets to an old peer address in the
   case that it receives reordered packets.

> **MUST**: An endpoint MAY send data to an unvalidated peer address, but it MUST
   protect against potential attacks as described in Sections 9.3.1 and
## 9.3.2  An endpoint MAY skip validation of a peer address if that
   address has been seen recently.  In particular, if an endpoint
   returns to a previously validated path after detecting some form of
   spurious migration, skipping address validation and restoring loss
   detection and congestion state can reduce the performance impact of
   the attack.

   After changing the address to which it sends non-probing packets, an
   endpoint can abandon any path validation for other addresses.

   Receiving a packet from a new peer address could be the result of a
   NAT rebinding at the peer.

> **SHOULD**: After verifying a new client address, the server SHOULD send new
   address validation tokens (Section 8) to the client.

### 9.3.1.  Peer Address Spoofing

   It is possible that a peer is spoofing its source address to cause an
   endpoint to send excessive amounts of data to an unwilling host.  If
   the endpoint sends significantly more data than the spoofing peer,
   connection migration might be used to amplify the volume of data that
   an attacker can generate toward a victim.

   As described in Section 9.3, an endpoint is required to validate a
   peer's new address to confirm the peer's possession of the new
   address.  Until a peer's address is deemed valid, an endpoint limits
   the amount of data it sends to that address; see Section 8.  In the
   absence of this limit, an endpoint risks being used for a denial-of-
   service attack against an unsuspecting victim.

   If an endpoint skips validation of a peer address as described above,
   it does not need to limit its sending rate.

### 9.3.2.  On-Path Address Spoofing

   An on-path attacker could cause a spurious connection migration by
   copying and forwarding a packet with a spoofed address such that it
   arrives before the original packet.  The packet with the spoofed
   address will be seen to come from a migrating connection, and the
   original packet will be seen as a duplicate and dropped.  After a
   spurious migration, validation of the source address will fail
   because the entity at the source address does not have the necessary
   cryptographic keys to read or respond to the PATH_CHALLENGE frame
   that is sent to it even if it wanted to.

   To protect the connection from failing due to such a spurious
> **MUST**: migration, an endpoint MUST revert to using the last validated peer
   address when validation of a new peer address fails.  Additionally,
   receipt of packets with higher packet numbers from the legitimate
   peer address will trigger another connection migration.  This will
   cause the validation of the address of the spurious migration to be
   abandoned, thus containing migrations initiated by the attacker
   injecting a single packet.

   If an endpoint has no state about the last validated peer address, it
> **MUST**: MUST close the connection silently by discarding all connection
   state.  This results in new packets on the connection being handled
> **MAY**: generically.  For instance, an endpoint MAY send a Stateless Reset in
   response to any further incoming packets.

### 9.3.3.  Off-Path Packet Forwarding

   An off-path attacker that can observe packets might forward copies of
   genuine packets to endpoints.  If the copied packet arrives before
   the genuine packet, this will appear as a NAT rebinding.  Any genuine
   packet will be discarded as a duplicate.  If the attacker is able to
   continue forwarding packets, it might be able to cause migration to a
   path via the attacker.  This places the attacker on-path, giving it
   the ability to observe or drop all subsequent packets.

   This style of attack relies on the attacker using a path that has
   approximately the same characteristics as the direct path between
   endpoints.  The attack is more reliable if relatively few packets are
   sent or if packet loss coincides with the attempted attack.

   A non-probing packet received on the original path that increases the
   maximum received packet number will cause the endpoint to move back
   to that path.  Eliciting packets on this path increases the
   likelihood that the attack is unsuccessful.  Therefore, mitigation of
   this attack relies on triggering the exchange of packets.

> **MUST**: In response to an apparent migration, endpoints MUST validate the
   previously active path using a PATH_CHALLENGE frame.  This induces
   the sending of new packets on that path.  If the path is no longer
   viable, the validation attempt will time out and fail; if the path is
   viable but no longer desired, the validation will succeed but only
   results in probing packets being sent on the path.

> **SHOULD**: An endpoint that receives a PATH_CHALLENGE on an active path SHOULD
   send a non-probing packet in response.  If the non-probing packet
   arrives before any copy made by an attacker, this results in the
   connection being migrated back to the original path.  Any subsequent
   migration to another path restarts this entire process.

   This defense is imperfect, but this is not considered a serious
   problem.  If the path via the attack is reliably faster than the
   original path despite multiple attempts to use that original path, it
   is not possible to distinguish between an attack and an improvement
   in routing.

   An endpoint could also use heuristics to improve detection of this
   style of attack.  For instance, NAT rebinding is improbable if
   packets were recently received on the old path; similarly, rebinding
   is rare on IPv6 paths.  Endpoints can also look for duplicated
   packets.  Conversely, a change in connection ID is more likely to
   indicate an intentional migration rather than an attack.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
