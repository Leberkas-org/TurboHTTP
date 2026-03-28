---
title: "9.6.  Server's Preferred Address"
rfc_number: 9000
rfc_section: "9.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 9.6: Server's Preferred Address — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, servers_preferred_address]
---

# 9.6.  Server's Preferred Address


   QUIC allows servers to accept connections on one IP address and
   attempt to transfer these connections to a more preferred address
   shortly after the handshake.  This is particularly useful when
   clients initially connect to an address shared by multiple servers
   but would prefer to use a unicast address to ensure connection
   stability.  This section describes the protocol for migrating a
   connection to a preferred server address.

   Migrating a connection to a new server address mid-connection is not
   supported by the version of QUIC specified in this document.  If a
   client receives packets from a new server address when the client has
> **SHOULD**: not initiated a migration to that address, the client SHOULD discard
   these packets.

### 9.6.1.  Communicating a Preferred Address

   A server conveys a preferred address by including the
   preferred_address transport parameter in the TLS handshake.

> **MAY**: Servers MAY communicate a preferred address of each address family
   (IPv4 and IPv6) to allow clients to pick the one most suited to their
   network attachment.

> **SHOULD**: Once the handshake is confirmed, the client SHOULD select one of the
   two addresses provided by the server and initiate path validation
   (see Section 8.2).  A client constructs packets using any previously
   unused active connection ID, taken from either the preferred_address
   transport parameter or a NEW_CONNECTION_ID frame.

> **SHOULD**: As soon as path validation succeeds, the client SHOULD begin sending
   all future packets to the new server address using the new connection
   ID and discontinue use of the old server address.  If path validation
> **MUST**: fails, the client MUST continue sending all future packets to the
   server's original IP address.

### 9.6.2.  Migration to a Preferred Address

> **MUST**: A client that migrates to a preferred address MUST validate the
   address it chooses before migrating; see Section 21.5.3.

   A server might receive a packet addressed to its preferred IP address
   at any time after it accepts a connection.  If this packet contains a
   PATH_CHALLENGE frame, the server sends a packet containing a
> **MUST**: PATH_RESPONSE frame as per Section 8.2.  The server MUST send non-
   probing packets from its original address until it receives a non-
   probing packet from the client at its preferred address and until the
   server has validated the new path.

> **MUST**: The server MUST probe on the path toward the client from its
   preferred address.  This helps to guard against spurious migration
   initiated by an attacker.

   Once the server has completed its path validation and has received a
   non-probing packet with a new largest packet number on its preferred
   address, the server begins sending non-probing packets to the client
> **SHOULD**: exclusively from its preferred IP address.  The server SHOULD drop
   newer packets for this connection that are received on the old IP
> **MAY**: address.  The server MAY continue to process delayed packets that are
   received on the old IP address.

   The addresses that a server provides in the preferred_address
   transport parameter are only valid for the connection in which they
> **MUST NOT**: are provided.  A client MUST NOT use these for other connections,
   including connections that are resumed from the current connection.

### 9.6.3.  Interaction of Client Migration and Preferred Address

   A client might need to perform a connection migration before it has
   migrated to the server's preferred address.  In this case, the client
> **SHOULD**: SHOULD perform path validation to both the original and preferred
   server address from the client's new address concurrently.

   If path validation of the server's preferred address succeeds, the
> **MUST**: client MUST abandon validation of the original address and migrate to
   using the server's preferred address.  If path validation of the
   server's preferred address fails but validation of the server's
> **MAY**: original address succeeds, the client MAY migrate to its new address
   and continue sending to the server's original address.

   If packets received at the server's preferred address have a
   different source address than observed from the client during the
> **MUST**: handshake, the server MUST protect against potential attacks as
   described in Sections 9.3.1 and 9.3.2.  In addition to intentional
   simultaneous migration, this might also occur because the client's
   access network used a different NAT binding for the server's
   preferred address.

> **SHOULD**: Servers SHOULD initiate path validation to the client's new address
   upon receiving a probe packet from a different address; see
   Section 8.

> **SHOULD**: A client that migrates to a new address SHOULD use a preferred
   address from the same address family for the server.

   The connection ID provided in the preferred_address transport
   parameter is not specific to the addresses that are provided.  This
   connection ID is provided to ensure that the client has a connection
> **MAY**: ID available for migration, but the client MAY use this connection ID
   on any path.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
