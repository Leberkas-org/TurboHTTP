---
title: "1.  Overview"
rfc_number: 9000
rfc_section: "1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 1: Overview — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, overview]
---

# 1.  Overview


   QUIC is a secure general-purpose transport protocol.  This document
   defines version 1 of QUIC, which conforms to the version-independent
   properties of QUIC defined in [QUIC-INVARIANTS].

   QUIC is a connection-oriented protocol that creates a stateful
   interaction between a client and server.

   The QUIC handshake combines negotiation of cryptographic and
   transport parameters.  QUIC integrates the TLS handshake [TLS13],
   although using a customized framing for protecting packets.  The
   integration of TLS and QUIC is described in more detail in
   [QUIC-TLS].  The handshake is structured to permit the exchange of
   application data as soon as possible.  This includes an option for
   clients to send data immediately (0-RTT), which requires some form of
   prior communication or configuration to enable.

   Endpoints communicate in QUIC by exchanging QUIC packets.  Most
   packets contain frames, which carry control information and
   application data between endpoints.  QUIC authenticates the entirety
   of each packet and encrypts as much of each packet as is practical.
   QUIC packets are carried in UDP datagrams [UDP] to better facilitate
   deployment in existing systems and networks.

   Application protocols exchange information over a QUIC connection via
   streams, which are ordered sequences of bytes.  Two types of streams
   can be created: bidirectional streams, which allow both endpoints to
   send data; and unidirectional streams, which allow a single endpoint
   to send data.  A credit-based scheme is used to limit stream creation
   and to bound the amount of data that can be sent.

   QUIC provides the necessary feedback to implement reliable delivery
   and congestion control.  An algorithm for detecting and recovering
   from loss of data is described in Section 6 of [QUIC-RECOVERY].  QUIC
   depends on congestion control to avoid network congestion.  An
   exemplary congestion control algorithm is described in Section 7 of
   [QUIC-RECOVERY].

   QUIC connections are not strictly bound to a single network path.
   Connection migration uses connection identifiers to allow connections
   to transfer to a new network path.  Only clients are able to migrate
   in this version of QUIC.  This design also allows connections to
   continue after changes in network topology or address mappings, such
   as might be caused by NAT rebinding.

   Once established, multiple options are provided for connection
   termination.  Applications can manage a graceful shutdown, endpoints
   can negotiate a timeout period, errors can cause immediate connection
   teardown, and a stateless mechanism provides for termination of
   connections after one endpoint has lost state.

## 1.1.  Document Structure

   This document describes the core QUIC protocol and is structured as
   follows:

   *  Streams are the basic service abstraction that QUIC provides.

      -  Section 2 describes core concepts related to streams,

      -  Section 3 provides a reference model for stream states, and

      -  Section 4 outlines the operation of flow control.

   *  Connections are the context in which QUIC endpoints communicate.

      -  Section 5 describes core concepts related to connections,

      -  Section 6 describes version negotiation,

      -  Section 7 details the process for establishing connections,

      -  Section 8 describes address validation and critical denial-of-
         service mitigations,

      -  Section 9 describes how endpoints migrate a connection to a new
         network path,

      -  Section 10 lists the options for terminating an open
         connection, and

      -  Section 11 provides guidance for stream and connection error
         handling.

   *  Packets and frames are the basic unit used by QUIC to communicate.

      -  Section 12 describes concepts related to packets and frames,

      -  Section 13 defines models for the transmission, retransmission,
         and acknowledgment of data, and

      -  Section 14 specifies rules for managing the size of datagrams
         carrying QUIC packets.

   *  Finally, encoding details of QUIC protocol elements are described
      in:

      -  Section 15 (versions),

      -  Section 16 (integer encoding),

      -  Section 17 (packet headers),

      -  Section 18 (transport parameters),

      -  Section 19 (frames), and

      -  Section 20 (errors).

   Accompanying documents describe QUIC's loss detection and congestion
   control [QUIC-RECOVERY], and the use of TLS and other cryptographic
   mechanisms [QUIC-TLS].

   This document defines QUIC version 1, which conforms to the protocol
   invariants in [QUIC-INVARIANTS].

   To refer to QUIC version 1, cite this document.  References to the
   limited set of version-independent properties of QUIC can cite
   [QUIC-INVARIANTS].

## 1.2.  Terms and Definitions

> **MUST**: The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT",
   "SHOULD", "SHOULD NOT", "RECOMMENDED", "NOT RECOMMENDED", "MAY", and
   "OPTIONAL" in this document are to be interpreted as described in BCP
   14 [RFC2119] [RFC8174] when, and only when, they appear in all
   capitals, as shown here.

   Commonly used terms in this document are described below.

   QUIC:  The transport protocol described by this document.  QUIC is a
      name, not an acronym.

   Endpoint:  An entity that can participate in a QUIC connection by
      generating, receiving, and processing QUIC packets.  There are
      only two types of endpoints in QUIC: client and server.

   Client:  The endpoint that initiates a QUIC connection.

   Server:  The endpoint that accepts a QUIC connection.

   QUIC packet:  A complete processable unit of QUIC that can be
      encapsulated in a UDP datagram.  One or more QUIC packets can be
      encapsulated in a single UDP datagram.

   Ack-eliciting packet:  A QUIC packet that contains frames other than
      ACK, PADDING, and CONNECTION_CLOSE.  These cause a recipient to
      send an acknowledgment; see Section 13.2.1.

   Frame:  A unit of structured protocol information.  There are
      multiple frame types, each of which carries different information.
      Frames are contained in QUIC packets.

   Address:  When used without qualification, the tuple of IP version,
      IP address, and UDP port number that represents one end of a
      network path.

   Connection ID:  An identifier that is used to identify a QUIC
      connection at an endpoint.  Each endpoint selects one or more
      connection IDs for its peer to include in packets sent towards the
      endpoint.  This value is opaque to the peer.

   Stream:  A unidirectional or bidirectional channel of ordered bytes
      within a QUIC connection.  A QUIC connection can carry multiple
      simultaneous streams.

   Application:  An entity that uses QUIC to send and receive data.

   This document uses the terms "QUIC packets", "UDP datagrams", and "IP
   packets" to refer to the units of the respective protocols.  That is,
   one or more QUIC packets can be encapsulated in a UDP datagram, which
   is in turn encapsulated in an IP packet.

## 1.3.  Notational Conventions

   Packet and frame diagrams in this document use a custom format.  The
   purpose of this format is to summarize, not define, protocol
   elements.  Prose defines the complete semantics and details of
   structures.

   Complex fields are named and then followed by a list of fields
   surrounded by a pair of matching braces.  Each field in this list is
   separated by commas.

   Individual fields include length information, plus indications about
   fixed value, optionality, or repetitions.  Individual fields use the
   following notational conventions, with all lengths in bits:

   x (A):  Indicates that x is A bits long

   x (i):  Indicates that x holds an integer value using the variable-
      length encoding described in Section 16

   x (A..B):  Indicates that x can be any length from A to B; A can be
      omitted to indicate a minimum of zero bits, and B can be omitted
      to indicate no set upper limit; values in this format always end
      on a byte boundary

   x (L) = C:  Indicates that x has a fixed value of C; the length of x
      is described by L, which can use any of the length forms above

   x (L) = C..D:  Indicates that x has a value in the range from C to D,
      inclusive, with the length described by L, as above

   [x (L)]:  Indicates that x is optional and has a length of L

   x (L) ...:  Indicates that x is repeated zero or more times and that
      each instance has a length of L

   This document uses network byte order (that is, big endian) values.
   Fields are placed starting from the high-order bits of each byte.

   By convention, individual fields reference a complex field by using
   the name of the complex field.

   Figure 1 provides an example:

   Example Structure {
     One-bit Field (1),
     7-bit Field with Fixed Value (7) = 61,
     Field with Variable-Length Integer (i),
     Arbitrary-Length Field (..),
     Variable-Length Field (8..24),
     Field With Minimum Length (16..),
     Field With Maximum Length (..128),
     [Optional Field (64)],
     Repeated Field (8) ...,
   }

                          Figure 1: Example Format

   When a single-bit field is referenced in prose, the position of that
   field can be clarified by using the value of the byte that carries
   the field with the field's value set.  For example, the value 0x80
   could be used to refer to the single-bit field in the most
   significant bit of the byte, such as One-bit Field in Figure 1.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
