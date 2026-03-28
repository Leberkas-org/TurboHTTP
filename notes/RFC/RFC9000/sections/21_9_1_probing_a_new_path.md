---
title: "9.1.  Probing a New Path"
rfc_number: 9000
rfc_section: "9.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 9.1: Probing a New Path — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, probing_a_new_path]
---

# 9.1.  Probing a New Path



   The use of a connection ID allows connections to survive changes to
   endpoint addresses (IP address and port), such as those caused by an
   endpoint migrating to a new network.  This section describes the
   process by which an endpoint migrates to a new address.

   The design of QUIC relies on endpoints retaining a stable address for
> **MUST NOT**: the duration of the handshake.  An endpoint MUST NOT initiate
   connection migration before the handshake is confirmed, as defined in
   Section 4.1.2 of [QUIC-TLS].

   If the peer sent the disable_active_migration transport parameter, an
> **MUST NOT**: endpoint also MUST NOT send packets (including probing packets; see
   Section 9.1) from a different local address to the address the peer
   used during the handshake, unless the endpoint has acted on a
   preferred_address transport parameter from the peer.  If the peer
> **MUST**: violates this requirement, the endpoint MUST either drop the incoming
   packets on that path without generating a Stateless Reset or proceed
   with path validation and allow the peer to migrate.  Generating a
   Stateless Reset or closing the connection would allow third parties
   in the network to cause connections to close by spoofing or otherwise
   manipulating observed traffic.

   Not all changes of peer address are intentional, or active,
   migrations.  The peer could experience NAT rebinding: a change of
   address due to a middlebox, usually a NAT, allocating a new outgoing
> **MUST**: port or even a new outgoing IP address for a flow.  An endpoint MUST
   perform path validation (Section 8.2) if it detects any change to a
   peer's address, unless it has previously validated that address.

   When an endpoint has no validated path on which to send packets, it
> **MAY**: MAY discard connection state.  An endpoint capable of connection
   migration MAY wait for a new path to become available before
   discarding connection state.

   This document limits migration of connections to new client
   addresses, except as described in Section 9.6.  Clients are
   responsible for initiating all migrations.  Servers do not send non-
   probing packets (see Section 9.1) toward a client address until they
   see a non-probing packet from that address.  If a client receives
> **MUST**: packets from an unknown server address, the client MUST discard these
   packets.

## 9.1.  Probing a New Path

> **MAY**: An endpoint MAY probe for peer reachability from a new local address
   using path validation (Section 8.2) prior to migrating the connection
   to the new local address.  Failure of path validation simply means
   that the new path is not usable for this connection.  Failure to
   validate a path does not cause the connection to end unless there are
   no valid alternative paths available.

   PATH_CHALLENGE, PATH_RESPONSE, NEW_CONNECTION_ID, and PADDING frames
   are "probing frames", and all other frames are "non-probing frames".
   A packet containing only probing frames is a "probing packet", and a
   packet containing any other frame is a "non-probing packet".

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
