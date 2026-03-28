---
title: "8.2.  Path Validation"
rfc_number: 9000
rfc_section: "8.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 8.2: Path Validation — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, path_validation]
---

# 8.2.  Path Validation


   Path validation is used by both peers during connection migration
   (see Section 9) to verify reachability after a change of address.  In
   path validation, endpoints test reachability between a specific local
   address and a specific peer address, where an address is the 2-tuple
   of IP address and port.

   Path validation tests that packets sent on a path to a peer are
   received by that peer.  Path validation is used to ensure that
   packets received from a migrating peer do not carry a spoofed source
   address.

   Path validation does not validate that a peer can send in the return
   direction.  Acknowledgments cannot be used for return path validation
   because they contain insufficient entropy and might be spoofed.
   Endpoints independently determine reachability on each direction of a
   path, and therefore return reachability can only be established by
   the peer.

   Path validation can be used at any time by either endpoint.  For
   instance, an endpoint might check that a peer is still in possession
   of its address after a period of quiescence.

   Path validation is not designed as a NAT traversal mechanism.  Though
   the mechanism described here might be effective for the creation of
   NAT bindings that support NAT traversal, the expectation is that one
   endpoint is able to receive packets without first having sent a
   packet on that path.  Effective NAT traversal needs additional
   synchronization mechanisms that are not provided here.

> **MAY**: An endpoint MAY include other frames with the PATH_CHALLENGE and
   PATH_RESPONSE frames used for path validation.  In particular, an
   endpoint can include PADDING frames with a PATH_CHALLENGE frame for
   Path Maximum Transmission Unit Discovery (PMTUD); see Section 14.2.1.
   An endpoint can also include its own PATH_CHALLENGE frame when
   sending a PATH_RESPONSE frame.

   An endpoint uses a new connection ID for probes sent from a new local
   address; see Section 9.5.  When probing a new path, an endpoint can
   ensure that its peer has an unused connection ID available for
   responses.  Sending NEW_CONNECTION_ID and PATH_CHALLENGE frames in
   the same packet, if the peer's active_connection_id_limit permits,
   ensures that an unused connection ID will be available to the peer
   when sending a response.

   An endpoint can choose to simultaneously probe multiple paths.  The
   number of simultaneous paths used for probes is limited by the number
   of extra connection IDs its peer has previously supplied, since each
   new local address used for a probe requires a previously unused
   connection ID.

### 8.2.1.  Initiating Path Validation

   To initiate path validation, an endpoint sends a PATH_CHALLENGE frame
   containing an unpredictable payload on the path to be validated.

> **MAY**: An endpoint MAY send multiple PATH_CHALLENGE frames to guard against
   packet loss.  However, an endpoint SHOULD NOT send multiple
   PATH_CHALLENGE frames in a single packet.

> **SHOULD NOT**: An endpoint SHOULD NOT probe a new path with packets containing a
   PATH_CHALLENGE frame more frequently than it would send an Initial
   packet.  This ensures that connection migration is no more load on a
   new path than establishing a new connection.

> **MUST**: The endpoint MUST use unpredictable data in every PATH_CHALLENGE
   frame so that it can associate the peer's response with the
   corresponding PATH_CHALLENGE.

> **MUST**: An endpoint MUST expand datagrams that contain a PATH_CHALLENGE frame
   to at least the smallest allowed maximum datagram size of 1200 bytes,
   unless the anti-amplification limit for the path does not permit
   sending a datagram of this size.  Sending UDP datagrams of this size
   ensures that the network path from the endpoint to the peer can be
   used for QUIC; see Section 14.

   When an endpoint is unable to expand the datagram size to 1200 bytes
   due to the anti-amplification limit, the path MTU will not be
   validated.  To ensure that the path MTU is large enough, the endpoint
> **MUST**: MUST perform a second path validation by sending a PATH_CHALLENGE
   frame in a datagram of at least 1200 bytes.  This additional
   validation can be performed after a PATH_RESPONSE is successfully
   received or when enough bytes have been received on the path that
   sending the larger datagram will not result in exceeding the anti-
   amplification limit.

> **MUST NOT**: Unlike other cases where datagrams are expanded, endpoints MUST NOT
   discard datagrams that appear to be too small when they contain
   PATH_CHALLENGE or PATH_RESPONSE.

### 8.2.2.  Path Validation Responses

> **MUST**: On receiving a PATH_CHALLENGE frame, an endpoint MUST respond by
   echoing the data contained in the PATH_CHALLENGE frame in a
> **MUST NOT**: PATH_RESPONSE frame.  An endpoint MUST NOT delay transmission of a
   packet containing a PATH_RESPONSE frame unless constrained by
   congestion control.

> **MUST**: A PATH_RESPONSE frame MUST be sent on the network path where the
   PATH_CHALLENGE frame was received.  This ensures that path validation
   by a peer only succeeds if the path is functional in both directions.
> **MUST NOT**: This requirement MUST NOT be enforced by the endpoint that initiates
   path validation, as that would enable an attack on migration; see
   Section 9.3.3.

> **MUST**: An endpoint MUST expand datagrams that contain a PATH_RESPONSE frame
   to at least the smallest allowed maximum datagram size of 1200 bytes.
   This verifies that the path is able to carry datagrams of this size
> **MUST NOT**: in both directions.  However, an endpoint MUST NOT expand the
   datagram containing the PATH_RESPONSE if the resulting data exceeds
   the anti-amplification limit.  This is expected to only occur if the
   received PATH_CHALLENGE was not sent in an expanded datagram.

> **MUST NOT**: An endpoint MUST NOT send more than one PATH_RESPONSE frame in
   response to one PATH_CHALLENGE frame; see Section 13.3.  The peer is
   expected to send more PATH_CHALLENGE frames as necessary to evoke
   additional PATH_RESPONSE frames.

### 8.2.3.  Successful Path Validation

   Path validation succeeds when a PATH_RESPONSE frame is received that
   contains the data that was sent in a previous PATH_CHALLENGE frame.
   A PATH_RESPONSE frame received on any network path validates the path
   on which the PATH_CHALLENGE was sent.

   If an endpoint sends a PATH_CHALLENGE frame in a datagram that is not
   expanded to at least 1200 bytes and if the response to it validates
   the peer address, the path is validated but not the path MTU.  As a
   result, the endpoint can now send more than three times the amount of
> **MUST**: data that has been received.  However, the endpoint MUST initiate
   another path validation with an expanded datagram to verify that the
   path supports the required MTU.

   Receipt of an acknowledgment for a packet containing a PATH_CHALLENGE
   frame is not adequate validation, since the acknowledgment can be
   spoofed by a malicious peer.

### 8.2.4.  Failed Path Validation

   Path validation only fails when the endpoint attempting to validate
   the path abandons its attempt to validate the path.

> **SHOULD**: Endpoints SHOULD abandon path validation based on a timer.  When
   setting this timer, implementations are cautioned that the new path
   could have a longer round-trip time than the original.  A value of
   three times the larger of the current PTO or the PTO for the new path
   (using kInitialRtt, as defined in [QUIC-RECOVERY]) is RECOMMENDED.

   This timeout allows for multiple PTOs to expire prior to failing path
   validation, so that loss of a single PATH_CHALLENGE or PATH_RESPONSE
   frame does not cause path validation failure.

   Note that the endpoint might receive packets containing other frames
   on the new path, but a PATH_RESPONSE frame with appropriate data is
   required for path validation to succeed.

   When an endpoint abandons path validation, it determines that the
   path is unusable.  This does not necessarily imply a failure of the
   connection -- endpoints can continue sending packets over other paths
   as appropriate.  If no paths are available, an endpoint can wait for
   a new path to become available or close the connection.  An endpoint
> **MAY**: that has no valid network path to its peer MAY signal this using the
   NO_VIABLE_PATH connection error, noting that this is only possible if
   the network path exists but does not support the required MTU
   (Section 14).

   A path validation might be abandoned for other reasons besides
   failure.  Primarily, this happens if a connection migration to a new
   path is initiated while a path validation on the old path is in
   progress.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
