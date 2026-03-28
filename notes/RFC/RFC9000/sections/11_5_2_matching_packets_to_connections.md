---
title: "5.2.  Matching Packets to Connections"
rfc_number: 9000
rfc_section: "5.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 5.2: Matching Packets to Connections — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, matching_packets_to_connections]
---

# 5.2.  Matching Packets to Connections


   Incoming packets are classified on receipt.  Packets can either be
   associated with an existing connection or -- for servers --
   potentially create a new connection.

   Endpoints try to associate a packet with an existing connection.  If
   the packet has a non-zero-length Destination Connection ID
   corresponding to an existing connection, QUIC processes that packet
   accordingly.  Note that more than one connection ID can be associated
   with a connection; see Section 5.1.

   If the Destination Connection ID is zero length and the addressing
   information in the packet matches the addressing information the
   endpoint uses to identify a connection with a zero-length connection
   ID, QUIC processes the packet as part of that connection.  An
   endpoint can use just destination IP and port or both source and
   destination addresses for identification, though this makes
   connections fragile as described in Section 5.1.

   Endpoints can send a Stateless Reset (Section 10.3) for any packets
   that cannot be attributed to an existing connection.  A Stateless
   Reset allows a peer to more quickly identify when a connection
   becomes unusable.

   Packets that are matched to an existing connection are discarded if
   the packets are inconsistent with the state of that connection.  For
   example, packets are discarded if they indicate a different protocol
   version than that of the connection or if the removal of packet
   protection is unsuccessful once the expected keys are available.

   Invalid packets that lack strong integrity protection, such as
> **MAY**: Initial, Retry, or Version Negotiation, MAY be discarded.  An
   endpoint MUST generate a connection error if processing the contents
   of these packets prior to discovering an error, or fully revert any
   changes made during that processing.

### 5.2.1.  Client Packet Handling

   Valid packets sent to clients always include a Destination Connection
   ID that matches a value the client selects.  Clients that choose to
   receive zero-length connection IDs can use the local address and port
   to identify a connection.  Packets that do not match an existing
   connection -- based on Destination Connection ID or, if this value is
   zero length, local IP address and port -- are discarded.

   Due to packet reordering or loss, a client might receive packets for
   a connection that are encrypted with a key it has not yet computed.
> **MAY**: The client MAY drop these packets, or it MAY buffer them in
   anticipation of later packets that allow it to compute the key.

   If a client receives a packet that uses a different version than it
> **MUST**: initially selected, it MUST discard that packet.

### 5.2.2.  Server Packet Handling

   If a server receives a packet that indicates an unsupported version
   and if the packet is large enough to initiate a new connection for
> **SHOULD**: any supported version, the server SHOULD send a Version Negotiation
   packet as described in Section 6.1.  A server MAY limit the number of
   packets to which it responds with a Version Negotiation packet.
> **MUST**: Servers MUST drop smaller packets that specify unsupported versions.

   The first packet for an unsupported version can use different
   semantics and encodings for any version-specific field.  In
   particular, different packet protection keys might be used for
   different versions.  Servers that do not support a particular version
   are unlikely to be able to decrypt the payload of the packet or
> **SHOULD**: properly interpret the result.  Servers SHOULD respond with a Version
   Negotiation packet, provided that the datagram is sufficiently long.

   Packets with a supported version, or no Version field, are matched to
   a connection using the connection ID or -- for packets with zero-
   length connection IDs -- the local address and port.  These packets
   are processed using the selected connection; otherwise, the server
   continues as described below.

   If the packet is an Initial packet fully conforming with the
   specification, the server proceeds with the handshake (Section 7).
   This commits the server to the version that the client selected.

> **SHOULD**: If a server refuses to accept a new connection, it SHOULD send an
   Initial packet containing a CONNECTION_CLOSE frame with error code
   CONNECTION_REFUSED.

> **MAY**: If the packet is a 0-RTT packet, the server MAY buffer a limited
   number of these packets in anticipation of a late-arriving Initial
   packet.  Clients are not able to send Handshake packets prior to
> **SHOULD**: receiving a server response, so servers SHOULD ignore any such
   packets.

> **MUST**: Servers MUST drop incoming packets under all other circumstances.

### 5.2.3.  Considerations for Simple Load Balancers

   A server deployment could load-balance among servers using only
   source and destination IP addresses and ports.  Changes to the
   client's IP address or port could result in packets being forwarded
   to the wrong server.  Such a server deployment could use one of the
   following methods for connection continuity when a client's address
   changes.

   *  Servers could use an out-of-band mechanism to forward packets to
      the correct server based on connection ID.

   *  If servers can use a dedicated server IP address or port, other
      than the one that the client initially connects to, they could use
      the preferred_address transport parameter to request that clients
      move connections to that dedicated address.  Note that clients
      could choose not to use the preferred address.

   A server in a deployment that does not implement a solution to
> **SHOULD**: maintain connection continuity when the client address changes SHOULD
   indicate that migration is not supported by using the
   disable_active_migration transport parameter.  The
   disable_active_migration transport parameter does not prohibit
   connection migration after a client has acted on a preferred_address
   transport parameter.

> **MUST**: Server deployments that use this simple form of load balancing MUST
   avoid the creation of a stateless reset oracle; see Section 21.11.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
