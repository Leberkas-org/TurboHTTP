---
title: "6.  Version Negotiation"
rfc_number: 9000
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 6: Version Negotiation — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, version_negotiation]
---

# 6.  Version Negotiation


   Version negotiation allows a server to indicate that it does not
   support the version the client used.  A server sends a Version
   Negotiation packet in response to each packet that might initiate a
   new connection; see Section 5.2 for details.

   The size of the first packet sent by a client will determine whether
   a server sends a Version Negotiation packet.  Clients that support
> **SHOULD**: multiple QUIC versions SHOULD ensure that the first UDP datagram they
   send is sized to the largest of the minimum datagram sizes from all
   versions they support, using PADDING frames (Section 19.1) as
   necessary.  This ensures that the server responds if there is a
   mutually supported version.  A server might not send a Version
   Negotiation packet if the datagram it receives is smaller than the
   minimum size specified in a different version; see Section 14.1.

## 6.1.  Sending Version Negotiation Packets

   If the version selected by the client is not acceptable to the
   server, the server responds with a Version Negotiation packet; see
   Section 17.2.1.  This includes a list of versions that the server
> **MUST NOT**: will accept.  An endpoint MUST NOT send a Version Negotiation packet
   in response to receiving a Version Negotiation packet.

   This system allows a server to process packets with unsupported
   versions without retaining state.  Though either the Initial packet
   or the Version Negotiation packet that is sent in response could be
   lost, the client will send new packets until it successfully receives
   a response or it abandons the connection attempt.

> **MAY**: A server MAY limit the number of Version Negotiation packets it
   sends.  For instance, a server that is able to recognize packets as
   0-RTT might choose not to send Version Negotiation packets in
   response to 0-RTT packets with the expectation that it will
   eventually receive an Initial packet.

## 6.2.  Handling Version Negotiation Packets

   Version Negotiation packets are designed to allow for functionality
   to be defined in the future that allows QUIC to negotiate the version
   of QUIC to use for a connection.  Future Standards Track
   specifications might change how implementations that support multiple
   versions of QUIC react to Version Negotiation packets received in
   response to an attempt to establish a connection using this version.

> **MUST**: A client that supports only this version of QUIC MUST abandon the
   current connection attempt if it receives a Version Negotiation
> **MUST**: packet, with the following two exceptions.  A client MUST discard any
   Version Negotiation packet if it has received and successfully
   processed any other packet, including an earlier Version Negotiation
> **MUST**: packet.  A client MUST discard a Version Negotiation packet that
   lists the QUIC version selected by the client.

   How to perform version negotiation is left as future work defined by
   future Standards Track specifications.  In particular, that future
   work will ensure robustness against version downgrade attacks; see
   Section 21.12.

## 6.3.  Using Reserved Versions

   For a server to use a new version in the future, clients need to
   correctly handle unsupported versions.  Some version numbers
   (0x?a?a?a?a, as defined in Section 15) are reserved for inclusion in
   fields that contain version numbers.

> **MAY**: Endpoints MAY add reserved versions to any field where unknown or
   unsupported versions are ignored to test that a peer correctly
   ignores the value.  For instance, an endpoint could include a
   reserved version in a Version Negotiation packet; see Section 17.2.1.
> **MAY**: Endpoints MAY send packets with a reserved version to test that a
   peer correctly discards the packet.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
