---
title: "10.2.  Immediate Close"
rfc_number: 9000
rfc_section: "10.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 10.2: Immediate Close — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, immediate_close]
---

# 10.2.  Immediate Close


   An endpoint sends a CONNECTION_CLOSE frame (Section 19.19) to
   terminate the connection immediately.  A CONNECTION_CLOSE frame
   causes all streams to immediately become closed; open streams can be
   assumed to be implicitly reset.

   After sending a CONNECTION_CLOSE frame, an endpoint immediately
   enters the closing state; see Section 10.2.1.  After receiving a
   CONNECTION_CLOSE frame, endpoints enter the draining state; see
   Section 10.2.2.

   Violations of the protocol lead to an immediate close.

   An immediate close can be used after an application protocol has
   arranged to close a connection.  This might be after the application
   protocol negotiates a graceful shutdown.  The application protocol
   can exchange messages that are needed for both application endpoints
   to agree that the connection can be closed, after which the
   application requests that QUIC close the connection.  When QUIC
   consequently closes the connection, a CONNECTION_CLOSE frame with an
   application-supplied error code will be used to signal closure to the
   peer.

   The closing and draining connection states exist to ensure that
   connections close cleanly and that delayed or reordered packets are
> **SHOULD**: properly discarded.  These states SHOULD persist for at least three
   times the current PTO interval as defined in [QUIC-RECOVERY].

   Disposing of connection state prior to exiting the closing or
   draining state could result in an endpoint generating a Stateless
   Reset unnecessarily when it receives a late-arriving packet.
   Endpoints that have some alternative means to ensure that late-
   arriving packets do not induce a response, such as those that are
> **MAY**: able to close the UDP socket, MAY end these states earlier to allow
   for faster resource recovery.  Servers that retain an open socket for
> **SHOULD NOT**: accepting new connections SHOULD NOT end the closing or draining
   state early.

> **SHOULD**: Once its closing or draining state ends, an endpoint SHOULD discard
   all connection state.  The endpoint MAY send a Stateless Reset in
   response to any further incoming packets belonging to this
   connection.

### 10.2.1.  Closing Connection State

   An endpoint enters the closing state after initiating an immediate
   close.

   In the closing state, an endpoint retains only enough information to
   generate a packet containing a CONNECTION_CLOSE frame and to identify
   packets as belonging to the connection.  An endpoint in the closing
   state sends a packet containing a CONNECTION_CLOSE frame in response
   to any incoming packet that it attributes to the connection.

> **SHOULD**: An endpoint SHOULD limit the rate at which it generates packets in
   the closing state.  For instance, an endpoint could wait for a
   progressively increasing number of received packets or amount of time
   before responding to received packets.

   An endpoint's selected connection ID and the QUIC version are
   sufficient information to identify packets for a closing connection;
> **MAY**: the endpoint MAY discard all other connection state.  An endpoint
   that is closing is not required to process any received frame.  An
> **MAY**: endpoint MAY retain packet protection keys for incoming packets to
   allow it to read and process a CONNECTION_CLOSE frame.

> **MAY**: An endpoint MAY drop packet protection keys when entering the closing
   state and send a packet containing a CONNECTION_CLOSE frame in
   response to any UDP datagram that is received.  However, an endpoint
   that discards packet protection keys cannot identify and discard
   invalid packets.  To avoid being used for an amplification attack,
> **MUST**: such endpoints MUST limit the cumulative size of packets it sends to
   three times the cumulative size of the packets that are received and
   attributed to the connection.  To minimize the state that an endpoint
> **MAY**: maintains for a closing connection, endpoints MAY send the exact same
   packet in response to any received packet.

      |  Note: Allowing retransmission of a closing packet is an
      |  exception to the requirement that a new packet number be used
      |  for each packet; see Section 12.3.  Sending new packet numbers
      |  is primarily of advantage to loss recovery and congestion
      |  control, which are not expected to be relevant for a closed
      |  connection.  Retransmitting the final packet requires less
      |  state.

   While in the closing state, an endpoint could receive packets from a
   new source address, possibly indicating a connection migration; see
> **MUST**: Section 9.  An endpoint in the closing state MUST either discard
   packets received from an unvalidated address or limit the cumulative
   size of packets it sends to an unvalidated address to three times the
   size of packets it receives from that address.

   An endpoint is not expected to handle key updates when it is closing
   (Section 6 of [QUIC-TLS]).  A key update might prevent the endpoint
   from moving from the closing state to the draining state, as the
   endpoint will not be able to process subsequently received packets,
   but it otherwise has no impact.

### 10.2.2.  Draining Connection State

   The draining state is entered once an endpoint receives a
   CONNECTION_CLOSE frame, which indicates that its peer is closing or
   draining.  While otherwise identical to the closing state, an
> **MUST NOT**: endpoint in the draining state MUST NOT send any packets.  Retaining
   packet protection keys is unnecessary once a connection is in the
   draining state.

> **MAY**: An endpoint that receives a CONNECTION_CLOSE frame MAY send a single
   packet containing a CONNECTION_CLOSE frame before entering the
   draining state, using a NO_ERROR code if appropriate.  An endpoint
> **MUST NOT**: MUST NOT send further packets.  Doing so could result in a constant
   exchange of CONNECTION_CLOSE frames until one of the endpoints exits
   the closing state.

> **MAY**: An endpoint MAY enter the draining state from the closing state if it
   receives a CONNECTION_CLOSE frame, which indicates that the peer is
   also closing or draining.  In this case, the draining state ends when
   the closing state would have ended.  In other words, the endpoint
   uses the same end time but ceases transmission of any packets on this
   connection.

### 10.2.3.  Immediate Close during the Handshake

   When sending a CONNECTION_CLOSE frame, the goal is to ensure that the
   peer will process the frame.  Generally, this means sending the frame
   in a packet with the highest level of packet protection to avoid the
   packet being discarded.  After the handshake is confirmed (see
> **MUST**: Section 4.1.2 of [QUIC-TLS]), an endpoint MUST send any
   CONNECTION_CLOSE frames in a 1-RTT packet.  However, prior to
   confirming the handshake, it is possible that more advanced packet
   protection keys are not available to the peer, so another
> **MAY**: CONNECTION_CLOSE frame MAY be sent in a packet that uses a lower
   packet protection level.  More specifically:

   *  A client will always know whether the server has Handshake keys
      (see Section 17.2.2.1), but it is possible that a server does not
      know whether the client has Handshake keys.  Under these
> **SHOULD**: circumstances, a server SHOULD send a CONNECTION_CLOSE frame in
      both Handshake and Initial packets to ensure that at least one of
      them is processable by the client.

   *  A client that sends a CONNECTION_CLOSE frame in a 0-RTT packet
      cannot be assured that the server has accepted 0-RTT.  Sending a
      CONNECTION_CLOSE frame in an Initial packet makes it more likely
      that the server can receive the close signal, even if the
      application error code might not be received.

   *  Prior to confirming the handshake, a peer might be unable to
> **SHOULD**: process 1-RTT packets, so an endpoint SHOULD send a
      CONNECTION_CLOSE frame in both Handshake and 1-RTT packets.  A
> **SHOULD**: server SHOULD also send a CONNECTION_CLOSE frame in an Initial
      packet.

   Sending a CONNECTION_CLOSE of type 0x1d in an Initial or Handshake
   packet could expose application state or be used to alter application
> **MUST**: state.  A CONNECTION_CLOSE of type 0x1d MUST be replaced by a
   CONNECTION_CLOSE of type 0x1c when sending the frame in Initial or
   Handshake packets.  Otherwise, information about the application
> **MUST**: state might be revealed.  Endpoints MUST clear the value of the
   Reason Phrase field and SHOULD use the APPLICATION_ERROR code when
   converting to a CONNECTION_CLOSE of type 0x1c.

   CONNECTION_CLOSE frames sent in multiple packet types can be
   coalesced into a single UDP datagram; see Section 12.2.

   An endpoint can send a CONNECTION_CLOSE frame in an Initial packet.
   This might be in response to unauthenticated information received in
   Initial or Handshake packets.  Such an immediate close might expose
   legitimate connections to a denial of service.  QUIC does not include
   defensive measures for on-path attacks during the handshake; see
   Section 21.2.  However, at the cost of reducing feedback about errors
   for legitimate peers, some forms of denial of service can be made
   more difficult for an attacker if endpoints discard illegal packets
   rather than terminating a connection with CONNECTION_CLOSE.  For this
> **MAY**: reason, endpoints MAY discard packets rather than immediately close
   if errors are detected in packets that lack authentication.

   An endpoint that has not established state, such as a server that
   detects an error in an Initial packet, does not enter the closing
   state.  An endpoint that has no state for the connection does not
   enter a closing or draining period on sending a CONNECTION_CLOSE
   frame.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
