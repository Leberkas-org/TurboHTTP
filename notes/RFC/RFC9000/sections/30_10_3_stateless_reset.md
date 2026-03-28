---
title: "10.3.  Stateless Reset"
rfc_number: 9000
rfc_section: "10.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 10.3: Stateless Reset — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, stateless_reset]
---

# 10.3.  Stateless Reset


   A stateless reset is provided as an option of last resort for an
   endpoint that does not have access to the state of a connection.  A
   crash or outage might result in peers continuing to send data to an
   endpoint that is unable to properly continue the connection.  An
> **MAY**: endpoint MAY send a Stateless Reset in response to receiving a packet
   that it cannot associate with an active connection.

   A stateless reset is not appropriate for indicating errors in active
   connections.  An endpoint that wishes to communicate a fatal
> **MUST**: connection error MUST use a CONNECTION_CLOSE frame if it is able.

   To support this process, an endpoint issues a stateless reset token,
   which is a 16-byte value that is hard to guess.  If the peer
   subsequently receives a Stateless Reset, which is a UDP datagram that
   ends in that stateless reset token, the peer will immediately end the
   connection.

   A stateless reset token is specific to a connection ID.  An endpoint
   issues a stateless reset token by including the value in the
   Stateless Reset Token field of a NEW_CONNECTION_ID frame.  Servers
   can also issue a stateless_reset_token transport parameter during the
   handshake that applies to the connection ID that it selected during
   the handshake.  These exchanges are protected by encryption, so only
   client and server know their value.  Note that clients cannot use the
   stateless_reset_token transport parameter because their transport
   parameters do not have confidentiality protection.

   Tokens are invalidated when their associated connection ID is retired
   via a RETIRE_CONNECTION_ID frame (Section 19.16).

   An endpoint that receives packets that it cannot process sends a
   packet in the following layout (see Section 1.3):

   Stateless Reset {
     Fixed Bits (2) = 1,
     Unpredictable Bits (38..),
     Stateless Reset Token (128),
   }

                         Figure 10: Stateless Reset

   This design ensures that a Stateless Reset is -- to the extent
   possible -- indistinguishable from a regular packet with a short
   header.

   A Stateless Reset uses an entire UDP datagram, starting with the
   first two bits of the packet header.  The remainder of the first byte
   and an arbitrary number of bytes following it are set to values that
> **SHOULD**: SHOULD be indistinguishable from random.  The last 16 bytes of the
   datagram contain a stateless reset token.

   To entities other than its intended recipient, a Stateless Reset will
   appear to be a packet with a short header.  For the Stateless Reset
   to appear as a valid QUIC packet, the Unpredictable Bits field needs
   to include at least 38 bits of data (or 5 bytes, less the two fixed
   bits).

   The resulting minimum size of 21 bytes does not guarantee that a
   Stateless Reset is difficult to distinguish from other packets if the
   recipient requires the use of a connection ID.  To achieve that end,
> **SHOULD**: the endpoint SHOULD ensure that all packets it sends are at least 22
   bytes longer than the minimum connection ID length that it requests
   the peer to include in its packets, adding PADDING frames as
   necessary.  This ensures that any Stateless Reset sent by the peer is
   indistinguishable from a valid packet sent to the endpoint.  An
   endpoint that sends a Stateless Reset in response to a packet that is
> **SHOULD**: 43 bytes or shorter SHOULD send a Stateless Reset that is one byte
   shorter than the packet it responds to.

   These values assume that the stateless reset token is the same length
   as the minimum expansion of the packet protection AEAD.  Additional
   unpredictable bytes are necessary if the endpoint could have
   negotiated a packet protection scheme with a larger minimum
   expansion.

> **MUST NOT**: An endpoint MUST NOT send a Stateless Reset that is three times or
   more larger than the packet it receives to avoid being used for
   amplification.  Section 10.3.3 describes additional limits on
   Stateless Reset size.

> **MUST**: Endpoints MUST discard packets that are too small to be valid QUIC
   packets.  To give an example, with the set of AEAD functions defined
   in [QUIC-TLS], short header packets that are smaller than 21 bytes
   are never valid.

> **MUST**: Endpoints MUST send Stateless Resets formatted as a packet with a
   short header.  However, endpoints MUST treat any packet ending in a
   valid stateless reset token as a Stateless Reset, as other QUIC
   versions might allow the use of a long header.

> **MAY**: An endpoint MAY send a Stateless Reset in response to a packet with a
   long header.  Sending a Stateless Reset is not effective prior to the
   stateless reset token being available to a peer.  In this QUIC
   version, packets with a long header are only used during connection
   establishment.  Because the stateless reset token is not available
   until connection establishment is complete or near completion,
   ignoring an unknown packet with a long header might be as effective
   as sending a Stateless Reset.

   An endpoint cannot determine the Source Connection ID from a packet
   with a short header; therefore, it cannot set the Destination
   Connection ID in the Stateless Reset.  The Destination Connection ID
   will therefore differ from the value used in previous packets.  A
   random Destination Connection ID makes the connection ID appear to be
   the result of moving to a new connection ID that was provided using a
   NEW_CONNECTION_ID frame; see Section 19.15.

   Using a randomized connection ID results in two problems:

   *  The packet might not reach the peer.  If the Destination
      Connection ID is critical for routing toward the peer, then this
      packet could be incorrectly routed.  This might also trigger
      another Stateless Reset in response; see Section 10.3.3.  A
      Stateless Reset that is not correctly routed is an ineffective
      error detection and recovery mechanism.  In this case, endpoints
      will need to rely on other methods -- such as timers -- to detect
      that the connection has failed.

   *  The randomly generated connection ID can be used by entities other
      than the peer to identify this as a potential Stateless Reset.  An
      endpoint that occasionally uses different connection IDs might
      introduce some uncertainty about this.

   This stateless reset design is specific to QUIC version 1.  An
   endpoint that supports multiple versions of QUIC needs to generate a
   Stateless Reset that will be accepted by peers that support any
   version that the endpoint might support (or might have supported
   prior to losing state).  Designers of new versions of QUIC need to be
   aware of this and either (1) reuse this design or (2) use a portion
   of the packet other than the last 16 bytes for carrying data.

### 10.3.1.  Detecting a Stateless Reset

   An endpoint detects a potential Stateless Reset using the trailing 16
   bytes of the UDP datagram.  An endpoint remembers all stateless reset
   tokens associated with the connection IDs and remote addresses for
   datagrams it has recently sent.  This includes Stateless Reset Token
   field values from NEW_CONNECTION_ID frames and the server's transport
   parameters but excludes stateless reset tokens associated with
   connection IDs that are either unused or retired.  The endpoint
   identifies a received datagram as a Stateless Reset by comparing the
   last 16 bytes of the datagram with all stateless reset tokens
   associated with the remote address on which the datagram was
   received.

   This comparison can be performed for every inbound datagram.
> **MAY**: Endpoints MAY skip this check if any packet from a datagram is
   successfully processed.  However, the comparison MUST be performed
   when the first packet in an incoming datagram either cannot be
   associated with a connection or cannot be decrypted.

> **MUST NOT**: An endpoint MUST NOT check for any stateless reset tokens associated
   with connection IDs it has not used or for connection IDs that have
   been retired.

   When comparing a datagram to stateless reset token values, endpoints
> **MUST**: MUST perform the comparison without leaking information about the
   value of the token.  For example, performing this comparison in
   constant time protects the value of individual stateless reset tokens
   from information leakage through timing side channels.  Another
   approach would be to store and compare the transformed values of
   stateless reset tokens instead of the raw token values, where the
   transformation is defined as a cryptographically secure pseudorandom
   function using a secret key (e.g., block cipher, Hashed Message
   Authentication Code (HMAC) [RFC2104]).  An endpoint is not expected
   to protect information about whether a packet was successfully
   decrypted or the number of valid stateless reset tokens.

   If the last 16 bytes of the datagram are identical in value to a
> **MUST**: stateless reset token, the endpoint MUST enter the draining period
   and not send any further packets on this connection.

### 10.3.2.  Calculating a Stateless Reset Token

> **MUST**: The stateless reset token MUST be difficult to guess.  In order to
   create a stateless reset token, an endpoint could randomly generate
   [RANDOM] a secret for every connection that it creates.  However,
   this presents a coordination problem when there are multiple
   instances in a cluster or a storage problem for an endpoint that
   might lose state.  Stateless reset specifically exists to handle the
   case where state is lost, so this approach is suboptimal.

   A single static key can be used across all connections to the same
   endpoint by generating the proof using a pseudorandom function that
   takes a static key and the connection ID chosen by the endpoint (see
   Section 5.1) as input.  An endpoint could use HMAC [RFC2104] (for
   example, HMAC(static_key, connection_id)) or the HMAC-based Key
   Derivation Function (HKDF) [RFC5869] (for example, using the static
   key as input keying material, with the connection ID as salt).  The
   output of this function is truncated to 16 bytes to produce the
   stateless reset token for that connection.

   An endpoint that loses state can use the same method to generate a
   valid stateless reset token.  The connection ID comes from the packet
   that the endpoint receives.

   This design relies on the peer always sending a connection ID in its
   packets so that the endpoint can use the connection ID from a packet
> **MUST**: to reset the connection.  An endpoint that uses this design MUST
   either use the same connection ID length for all connections or
   encode the length of the connection ID such that it can be recovered
   without state.  In addition, it cannot provide a zero-length
   connection ID.

   Revealing the stateless reset token allows any entity to terminate
   the connection, so a value can only be used once.  This method for
   choosing the stateless reset token means that the combination of
> **MUST NOT**: connection ID and static key MUST NOT be used for another connection.
   A denial-of-service attack is possible if the same connection ID is
   used by instances that share a static key or if an attacker can cause
   a packet to be routed to an instance that has no state but the same
   static key; see Section 21.11.  A connection ID from a connection
> **MUST NOT**: that is reset by revealing the stateless reset token MUST NOT be
   reused for new connections at nodes that share a static key.

> **MUST NOT**: The same stateless reset token MUST NOT be used for multiple
   connection IDs.  Endpoints are not required to compare new values
> **MAY**: against all previous values, but a duplicate value MAY be treated as
   a connection error of type PROTOCOL_VIOLATION.

   Note that Stateless Resets do not have any cryptographic protection.

### 10.3.3.  Looping

   The design of a Stateless Reset is such that without knowing the
   stateless reset token it is indistinguishable from a valid packet.
   For instance, if a server sends a Stateless Reset to another server,
   it might receive another Stateless Reset in response, which could
   lead to an infinite exchange.

> **MUST**: An endpoint MUST ensure that every Stateless Reset that it sends is
   smaller than the packet that triggered it, unless it maintains state
   sufficient to prevent looping.  In the event of a loop, this results
   in packets eventually being too small to trigger a response.

   An endpoint can remember the number of Stateless Resets that it has
   sent and stop generating new Stateless Resets once a limit is
   reached.  Using separate limits for different remote addresses will
   ensure that Stateless Resets can be used to close connections when
   other peers or connections have exhausted limits.

   A Stateless Reset that is smaller than 41 bytes might be identifiable
   as a Stateless Reset by an observer, depending upon the length of the
   peer's connection IDs.  Conversely, not sending a Stateless Reset in
   response to a small packet might result in Stateless Resets not being
   useful in detecting cases of broken connections where only very small
   packets are sent; such failures might only be detected by other
   means, such as timers.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
