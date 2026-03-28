---
title: "17.2.  Long Header Packets"
rfc_number: 9000
rfc_section: "17.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 17.2: Long Header Packets — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, long_header_packets]
---

# 17.2.  Long Header Packets


   Long Header Packet {
     Header Form (1) = 1,
     Fixed Bit (1) = 1,
     Long Packet Type (2),
     Type-Specific Bits (4),
     Version (32),
     Destination Connection ID Length (8),
     Destination Connection ID (0..160),
     Source Connection ID Length (8),
     Source Connection ID (0..160),
     Type-Specific Payload (..),
   }

                    Figure 13: Long Header Packet Format

   Long headers are used for packets that are sent prior to the
   establishment of 1-RTT keys.  Once 1-RTT keys are available, a sender
   switches to sending packets using the short header (Section 17.3).
   The long form allows for special packets -- such as the Version
   Negotiation packet -- to be represented in this uniform fixed-length
   packet format.  Packets that use the long header contain the
   following fields:

   Header Form:  The most significant bit (0x80) of byte 0 (the first
      byte) is set to 1 for long headers.

   Fixed Bit:  The next bit (0x40) of byte 0 is set to 1, unless the
      packet is a Version Negotiation packet.  Packets containing a zero
> **MUST**: value for this bit are not valid packets in this version and MUST
      be discarded.  A value of 1 for this bit allows QUIC to coexist
      with other protocols; see [RFC7983].

   Long Packet Type:  The next two bits (those with a mask of 0x30) of
      byte 0 contain a packet type.  Packet types are listed in Table 5.

   Type-Specific Bits:  The semantics of the lower four bits (those with
      a mask of 0x0f) of byte 0 are determined by the packet type.

   Version:  The QUIC Version is a 32-bit field that follows the first
      byte.  This field indicates the version of QUIC that is in use and
      determines how the rest of the protocol fields are interpreted.

   Destination Connection ID Length:  The byte following the version
      contains the length in bytes of the Destination Connection ID
      field that follows it.  This length is encoded as an 8-bit
> **MUST NOT**: unsigned integer.  In QUIC version 1, this value MUST NOT exceed
      20 bytes.  Endpoints that receive a version 1 long header with a
> **MUST**: value larger than 20 MUST drop the packet.  In order to properly
   form a Version Negotiation packet, servers SHOULD be able to read
      longer connection IDs from other QUIC versions.

   Destination Connection ID:  The Destination Connection ID field
      follows the Destination Connection ID Length field, which
      indicates the length of this field.  Section 7.2 describes the use
      of this field in more detail.

   Source Connection ID Length:  The byte following the Destination
      Connection ID contains the length in bytes of the Source
      Connection ID field that follows it.  This length is encoded as an
> **MUST NOT**: 8-bit unsigned integer.  In QUIC version 1, this value MUST NOT
      exceed 20 bytes.  Endpoints that receive a version 1 long header
> **MUST**: with a value larger than 20 MUST drop the packet.  In order to
   properly form a Version Negotiation packet, servers SHOULD be able
      to read longer connection IDs from other QUIC versions.

   Source Connection ID:  The Source Connection ID field follows the
      Source Connection ID Length field, which indicates the length of
      this field.  Section 7.2 describes the use of this field in more
      detail.

   Type-Specific Payload:  The remainder of the packet, if any, is type
      specific.

   In this version of QUIC, the following packet types with the long
   header are defined:

                   +======+===========+================+
                   | Type | Name      | Section        |
                   +======+===========+================+
                   | 0x00 | Initial   | Section 17.2.2 |
                   +------+-----------+----------------+
                   | 0x01 | 0-RTT     | Section 17.2.3 |
                   +------+-----------+----------------+
                   | 0x02 | Handshake | Section 17.2.4 |
                   +------+-----------+----------------+
                   | 0x03 | Retry     | Section 17.2.5 |
                   +------+-----------+----------------+

                     Table 5: Long Header Packet Types

   The header form bit, Destination and Source Connection ID lengths,
   Destination and Source Connection ID fields, and Version fields of a
   long header packet are version independent.  The other fields in the
   first byte are version specific.  See [QUIC-INVARIANTS] for details
   on how packets from different versions of QUIC are interpreted.

   The interpretation of the fields and the payload are specific to a
   version and packet type.  While type-specific semantics for this
   version are described in the following sections, several long header
   packets in this version of QUIC contain these additional fields:

   Reserved Bits:  Two bits (those with a mask of 0x0c) of byte 0 are
      reserved across multiple packet types.  These bits are protected
      using header protection; see Section 5.4 of [QUIC-TLS].  The value
> **MUST**: included prior to protection MUST be set to 0.  An endpoint MUST
      treat receipt of a packet that has a non-zero value for these bits
      after removing both packet and header protection as a connection
      error of type PROTOCOL_VIOLATION.  Discarding such a packet after
      only removing header protection can expose the endpoint to
      attacks; see Section 9.5 of [QUIC-TLS].

   Packet Number Length:  In packet types that contain a Packet Number
      field, the least significant two bits (those with a mask of 0x03)
      of byte 0 contain the length of the Packet Number field, encoded
      as an unsigned two-bit integer that is one less than the length of
      the Packet Number field in bytes.  That is, the length of the
      Packet Number field is the value of this field plus one.  These
      bits are protected using header protection; see Section 5.4 of
      [QUIC-TLS].

   Length:  This is the length of the remainder of the packet (that is,
      the Packet Number and Payload fields) in bytes, encoded as a
      variable-length integer (Section 16).

   Packet Number:  This field is 1 to 4 bytes long.  The packet number
      is protected using header protection; see Section 5.4 of
      [QUIC-TLS].  The length of the Packet Number field is encoded in
      the Packet Number Length bits of byte 0; see above.

   Packet Payload:  This is the payload of the packet -- containing a
      sequence of frames -- that is protected using packet protection.

### 17.2.1.  Version Negotiation Packet

   A Version Negotiation packet is inherently not version specific.
   Upon receipt by a client, it will be identified as a Version
   Negotiation packet based on the Version field having a value of 0.

   The Version Negotiation packet is a response to a client packet that
   contains a version that is not supported by the server.  It is only
   sent by servers.

   The layout of a Version Negotiation packet is:

   Version Negotiation Packet {
     Header Form (1) = 1,
     Unused (7),
     Version (32) = 0,
     Destination Connection ID Length (8),
     Destination Connection ID (0..2040),
     Source Connection ID Length (8),
     Source Connection ID (0..2040),
     Supported Version (32) ...,
   }

                   Figure 14: Version Negotiation Packet

   The value in the Unused field is set to an arbitrary value by the
> **MUST**: server.  Clients MUST ignore the value of this field.  Where QUIC
   might be multiplexed with other protocols (see [RFC7983]), servers
> **SHOULD**: SHOULD set the most significant bit of this field (0x40) to 1 so that
   Version Negotiation packets appear to have the Fixed Bit field.  Note
   that other versions of QUIC might not make a similar recommendation.

> **MUST**: The Version field of a Version Negotiation packet MUST be set to
   0x00000000.

> **MUST**: The server MUST include the value from the Source Connection ID field
   of the packet it receives in the Destination Connection ID field.
> **MUST**: The value for Source Connection ID MUST be copied from the
   Destination Connection ID of the received packet, which is initially
   randomly selected by a client.  Echoing both connection IDs gives
   clients some assurance that the server received the packet and that
   the Version Negotiation packet was not generated by an entity that
   did not observe the Initial packet.

   Future versions of QUIC could have different requirements for the
   lengths of connection IDs.  In particular, connection IDs might have
   a smaller minimum length or a greater maximum length.  Version-
> **MUST NOT**: specific rules for the connection ID therefore MUST NOT influence a
   decision about whether to send a Version Negotiation packet.

   The remainder of the Version Negotiation packet is a list of 32-bit
   versions that the server supports.

   A Version Negotiation packet is not acknowledged.  It is only sent in
   response to a packet that indicates an unsupported version; see
   Section 5.2.2.

   The Version Negotiation packet does not include the Packet Number and
   Length fields present in other packets that use the long header form.
   Consequently, a Version Negotiation packet consumes an entire UDP
   datagram.

> **MUST NOT**: A server MUST NOT send more than one Version Negotiation packet in
   response to a single UDP datagram.

   See Section 6 for a description of the version negotiation process.

### 17.2.2.  Initial Packet

   An Initial packet uses long headers with a type value of 0x00.  It
   carries the first CRYPTO frames sent by the client and server to
   perform key exchange, and it carries ACK frames in either direction.

   Initial Packet {
     Header Form (1) = 1,
     Fixed Bit (1) = 1,
     Long Packet Type (2) = 0,
     Reserved Bits (2),
     Packet Number Length (2),
     Version (32),
     Destination Connection ID Length (8),
     Destination Connection ID (0..160),
     Source Connection ID Length (8),
     Source Connection ID (0..160),
     Token Length (i),
     Token (..),
     Length (i),
     Packet Number (8..32),
     Packet Payload (8..),
   }

                         Figure 15: Initial Packet

   The Initial packet contains a long header as well as the Length and
   Packet Number fields; see Section 17.2.  The first byte contains the
   Reserved and Packet Number Length bits; see also Section 17.2.
   Between the Source Connection ID and Length fields, there are two
   additional fields specific to the Initial packet.

   Token Length:  A variable-length integer specifying the length of the
      Token field, in bytes.  This value is 0 if no token is present.
> **MUST**: Initial packets sent by the server MUST set the Token Length field
      to 0; clients that receive an Initial packet with a non-zero Token
> **MUST**: Length field MUST either discard the packet or generate a
      connection error of type PROTOCOL_VIOLATION.

   Token:  The value of the token that was previously provided in a
      Retry packet or NEW_TOKEN frame; see Section 8.1.

   In order to prevent tampering by version-unaware middleboxes, Initial
   packets are protected with connection- and version-specific keys
   (Initial keys) as described in [QUIC-TLS].  This protection does not
   provide confidentiality or integrity against attackers that can
   observe packets, but it does prevent attackers that cannot observe
   packets from spoofing Initial packets.

   The client and server use the Initial packet type for any packet that
   contains an initial cryptographic handshake message.  This includes
   all cases where a new packet containing the initial cryptographic
   message needs to be created, such as the packets sent after receiving
   a Retry packet; see Section 17.2.5.

   A server sends its first Initial packet in response to a client
> **MAY**: Initial.  A server MAY send multiple Initial packets.  The
   cryptographic key exchange could require multiple round trips or
   retransmissions of this data.

   The payload of an Initial packet includes a CRYPTO frame (or frames)
   containing a cryptographic handshake message, ACK frames, or both.
   PING, PADDING, and CONNECTION_CLOSE frames of type 0x1c are also
   permitted.  An endpoint that receives an Initial packet containing
   other frames can either discard the packet as spurious or treat it as
   a connection error.

   The first packet sent by a client always includes a CRYPTO frame that
   contains the start or all of the first cryptographic handshake
   message.  The first CRYPTO frame sent always begins at an offset of
   0; see Section 7.

   Note that if the server sends a TLS HelloRetryRequest (see
   Section 4.7 of [QUIC-TLS]), the client will send another series of
   Initial packets.  These Initial packets will continue the
   cryptographic handshake and will contain CRYPTO frames starting at an
   offset matching the size of the CRYPTO frames sent in the first
   flight of Initial packets.

### 17.2.2.1.  Abandoning Initial Packets

   A client stops both sending and processing Initial packets when it
   sends its first Handshake packet.  A server stops sending and
   processing Initial packets when it receives its first Handshake
   packet.  Though packets might still be in flight or awaiting
   acknowledgment, no further Initial packets need to be exchanged
   beyond this point.  Initial packet protection keys are discarded (see
   Section 4.9.1 of [QUIC-TLS]) along with any loss recovery and
   congestion control state; see Section 6.4 of [QUIC-RECOVERY].

   Any data in CRYPTO frames is discarded -- and no longer retransmitted
   -- when Initial keys are discarded.

### 17.2.3.  0-RTT

   A 0-RTT packet uses long headers with a type value of 0x01, followed
   by the Length and Packet Number fields; see Section 17.2.  The first
   byte contains the Reserved and Packet Number Length bits; see
   Section 17.2.  A 0-RTT packet is used to carry "early" data from the
   client to the server as part of the first flight, prior to handshake
   completion.  As part of the TLS handshake, the server can accept or
   reject this early data.

   See Section 2.3 of [TLS13] for a discussion of 0-RTT data and its
   limitations.

   0-RTT Packet {
     Header Form (1) = 1,
     Fixed Bit (1) = 1,
     Long Packet Type (2) = 1,
     Reserved Bits (2),
     Packet Number Length (2),
     Version (32),
     Destination Connection ID Length (8),
     Destination Connection ID (0..160),
     Source Connection ID Length (8),
     Source Connection ID (0..160),
     Length (i),
     Packet Number (8..32),
     Packet Payload (8..),
   }

                          Figure 16: 0-RTT Packet

   Packet numbers for 0-RTT protected packets use the same space as
   1-RTT protected packets.

   After a client receives a Retry packet, 0-RTT packets are likely to
> **SHOULD**: have been lost or discarded by the server.  A client SHOULD attempt
   to resend data in 0-RTT packets after it sends a new Initial packet.
> **MUST**: New packet numbers MUST be used for any new packets that are sent; as
   described in Section 17.2.5.3, reusing packet numbers could
   compromise packet protection.

   A client only receives acknowledgments for its 0-RTT packets once the
   handshake is complete, as defined in Section 4.1.1 of [QUIC-TLS].

> **MUST NOT**: A client MUST NOT send 0-RTT packets once it starts processing 1-RTT
   packets from the server.  This means that 0-RTT packets cannot
   contain any response to frames from 1-RTT packets.  For instance, a
   client cannot send an ACK frame in a 0-RTT packet, because that can
   only acknowledge a 1-RTT packet.  An acknowledgment for a 1-RTT
> **MUST**: packet MUST be carried in a 1-RTT packet.

> **SHOULD**: A server SHOULD treat a violation of remembered limits
   (Section 7.4.1) as a connection error of an appropriate type (for
   instance, a FLOW_CONTROL_ERROR for exceeding stream data limits).

### 17.2.4.  Handshake Packet

   A Handshake packet uses long headers with a type value of 0x02,
   followed by the Length and Packet Number fields; see Section 17.2.
   The first byte contains the Reserved and Packet Number Length bits;
   see Section 17.2.  It is used to carry cryptographic handshake
   messages and acknowledgments from the server and client.

   Handshake Packet {
     Header Form (1) = 1,
     Fixed Bit (1) = 1,
     Long Packet Type (2) = 2,
     Reserved Bits (2),
     Packet Number Length (2),
     Version (32),
     Destination Connection ID Length (8),
     Destination Connection ID (0..160),
     Source Connection ID Length (8),
     Source Connection ID (0..160),
     Length (i),
     Packet Number (8..32),
     Packet Payload (8..),
   }

                   Figure 17: Handshake Protected Packet

   Once a client has received a Handshake packet from a server, it uses
   Handshake packets to send subsequent cryptographic handshake messages
   and acknowledgments to the server.

   The Destination Connection ID field in a Handshake packet contains a
   connection ID that is chosen by the recipient of the packet; the
   Source Connection ID includes the connection ID that the sender of
   the packet wishes to use; see Section 7.2.

   Handshake packets have their own packet number space, and thus the
   first Handshake packet sent by a server contains a packet number of
   0.

   The payload of this packet contains CRYPTO frames and could contain
> **MAY**: PING, PADDING, or ACK frames.  Handshake packets MAY contain
   CONNECTION_CLOSE frames of type 0x1c.  Endpoints MUST treat receipt
   of Handshake packets with other frames as a connection error of type
   PROTOCOL_VIOLATION.

   Like Initial packets (see Section 17.2.2.1), data in CRYPTO frames
   for Handshake packets is discarded -- and no longer retransmitted --
   when Handshake protection keys are discarded.

### 17.2.5.  Retry Packet

   As shown in Figure 18, a Retry packet uses a long packet header with
   a type value of 0x03.  It carries an address validation token created
   by the server.  It is used by a server that wishes to perform a
   retry; see Section 8.1.

   Retry Packet {
     Header Form (1) = 1,
     Fixed Bit (1) = 1,
     Long Packet Type (2) = 3,
     Unused (4),
     Version (32),
     Destination Connection ID Length (8),
     Destination Connection ID (0..160),
     Source Connection ID Length (8),
     Source Connection ID (0..160),
     Retry Token (..),
     Retry Integrity Tag (128),
   }

                          Figure 18: Retry Packet

   A Retry packet does not contain any protected fields.  The value in
   the Unused field is set to an arbitrary value by the server; a client
> **MUST**: MUST ignore these bits.  In addition to the fields from the long
   header, it contains these additional fields:

   Retry Token:  An opaque token that the server can use to validate the
      client's address.

   Retry Integrity Tag:  Defined in Section 5.8 ("Retry Packet
      Integrity") of [QUIC-TLS].

### 17.2.5.1.  Sending a Retry Packet

   The server populates the Destination Connection ID with the
   connection ID that the client included in the Source Connection ID of
   the Initial packet.

   The server includes a connection ID of its choice in the Source
> **MUST NOT**: Connection ID field.  This value MUST NOT be equal to the Destination
   Connection ID field of the packet sent by the client.  A client MUST
   discard a Retry packet that contains a Source Connection ID field
   that is identical to the Destination Connection ID field of its
> **MUST**: Initial packet.  The client MUST use the value from the Source
   Connection ID field of the Retry packet in the Destination Connection
   ID field of subsequent packets that it sends.

> **MAY**: A server MAY send Retry packets in response to Initial and 0-RTT
   packets.  A server can either discard or buffer 0-RTT packets that it
   receives.  A server can send multiple Retry packets as it receives
> **MUST NOT**: Initial or 0-RTT packets.  A server MUST NOT send more than one Retry
   packet in response to a single UDP datagram.

### 17.2.5.2.  Handling a Retry Packet

> **MUST**: A client MUST accept and process at most one Retry packet for each
   connection attempt.  After the client has received and processed an
> **MUST**: Initial or Retry packet from the server, it MUST discard any
   subsequent Retry packets that it receives.

> **MUST**: Clients MUST discard Retry packets that have a Retry Integrity Tag
   that cannot be validated; see Section 5.8 of [QUIC-TLS].  This
   diminishes an attacker's ability to inject a Retry packet and
   protects against accidental corruption of Retry packets.  A client
> **MUST**: MUST discard a Retry packet with a zero-length Retry Token field.

   The client responds to a Retry packet with an Initial packet that
   includes the provided Retry token to continue connection
   establishment.

   A client sets the Destination Connection ID field of this Initial
   packet to the value from the Source Connection ID field in the Retry
   packet.  Changing the Destination Connection ID field also results in
   a change to the keys used to protect the Initial packet.  It also
   sets the Token field to the token provided in the Retry packet.  The
> **MUST NOT**: client MUST NOT change the Source Connection ID because the server
   could include the connection ID as part of its token validation
   logic; see Section 8.1.4.

   A Retry packet does not include a packet number and cannot be
   explicitly acknowledged by a client.

### 17.2.5.3.  Continuing a Handshake after Retry

   Subsequent Initial packets from the client include the connection ID
   and token values from the Retry packet.  The client copies the Source
   Connection ID field from the Retry packet to the Destination
   Connection ID field and uses this value until an Initial packet with
   an updated value is received; see Section 7.2.  The value of the
   Token field is copied to all subsequent Initial packets; see
   Section 8.1.2.

   Other than updating the Destination Connection ID and Token fields,
   the Initial packet sent by the client is subject to the same
> **MUST**: restrictions as the first Initial packet.  A client MUST use the same
   cryptographic handshake message it included in this packet.  A server
> **MAY**: MAY treat a packet that contains a different cryptographic handshake
   message as a connection error or discard it.  Note that including a
   Token field reduces the available space for the cryptographic
   handshake message, which might result in the client needing to send
   multiple Initial packets.

> **MAY**: A client MAY attempt 0-RTT after receiving a Retry packet by sending
   0-RTT packets to the connection ID provided by the server.

> **MUST NOT**: A client MUST NOT reset the packet number for any packet number space
   after processing a Retry packet.  In particular, 0-RTT packets
   contain confidential information that will most likely be
   retransmitted on receiving a Retry packet.  The keys used to protect
   these new 0-RTT packets will not change as a result of responding to
   a Retry packet.  However, the data sent in these packets could be
   different than what was sent earlier.  Sending these new packets with
   the same packet number is likely to compromise the packet protection
   for those packets because the same key and nonce could be used to
> **MAY**: protect different content.  A server MAY abort the connection if it
   detects that the client reset the packet number.

   The connection IDs used in Initial and Retry packets exchanged
   between client and server are copied to the transport parameters and
   validated as described in Section 7.3.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
