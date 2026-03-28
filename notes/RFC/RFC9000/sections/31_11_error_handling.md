---
title: "11.  Error Handling"
rfc_number: 9000
rfc_section: "11"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 11: Error Handling — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, error_handling]
---

# 11.  Error Handling


> **SHOULD**: An endpoint that detects an error SHOULD signal the existence of that
   error to its peer.  Both transport-level and application-level errors
   can affect an entire connection; see Section 11.1.  Only application-
   level errors can be isolated to a single stream; see Section 11.2.

> **SHOULD**: The most appropriate error code (Section 20) SHOULD be included in
   the frame that signals the error.  Where this specification
   identifies error conditions, it also identifies the error code that
   is used; though these are worded as requirements, different
   implementation strategies might lead to different errors being
> **MAY**: reported.  In particular, an endpoint MAY use any applicable error
   code when it detects an error condition; a generic error code (such
   as PROTOCOL_VIOLATION or INTERNAL_ERROR) can always be used in place
   of specific error codes.

   A stateless reset (Section 10.3) is not suitable for any error that
   can be signaled with a CONNECTION_CLOSE or RESET_STREAM frame.  A
> **MUST NOT**: stateless reset MUST NOT be used by an endpoint that has the state
   necessary to send a frame on the connection.

## 11.1.  Connection Errors

   Errors that result in the connection being unusable, such as an
   obvious violation of protocol semantics or corruption of state that
> **MUST**: affects an entire connection, MUST be signaled using a
   CONNECTION_CLOSE frame (Section 19.19).

   Application-specific protocol errors are signaled using the
   CONNECTION_CLOSE frame with a frame type of 0x1d.  Errors that are
   specific to the transport, including all those described in this
   document, are carried in the CONNECTION_CLOSE frame with a frame type
   of 0x1c.

   A CONNECTION_CLOSE frame could be sent in a packet that is lost.  An
> **SHOULD**: endpoint SHOULD be prepared to retransmit a packet containing a
   CONNECTION_CLOSE frame if it receives more packets on a terminated
   connection.  Limiting the number of retransmissions and the time over
   which this final packet is sent limits the effort expended on
   terminated connections.

   An endpoint that chooses not to retransmit packets containing a
   CONNECTION_CLOSE frame risks a peer missing the first such packet.
   The only mechanism available to an endpoint that continues to receive
   data for a terminated connection is to attempt the stateless reset
   process (Section 10.3).

   As the AEAD for Initial packets does not provide strong
> **MAY**: authentication, an endpoint MAY discard an invalid Initial packet.
   Discarding an Initial packet is permitted even where this
   specification otherwise mandates a connection error.  An endpoint can
   only discard a packet if it does not process the frames in the packet
   or reverts the effects of any processing.  Discarding invalid Initial
   packets might be used to reduce exposure to denial of service; see
   Section 21.2.

## 11.2.  Stream Errors

   If an application-level error affects a single stream but otherwise
   leaves the connection in a recoverable state, the endpoint can send a
   RESET_STREAM frame (Section 19.4) with an appropriate error code to
   terminate just the affected stream.

   Resetting a stream without the involvement of the application
   protocol could cause the application protocol to enter an
> **MUST**: unrecoverable state.  RESET_STREAM MUST only be instigated by the
   application protocol that uses QUIC.

   The semantics of the application error code carried in RESET_STREAM
   are defined by the application protocol.  Only the application
   protocol is able to cause a stream to be terminated.  A local
   instance of the application protocol uses a direct API call, and a
   remote instance uses the STOP_SENDING frame, which triggers an
   automatic RESET_STREAM.

> **SHOULD**: Application protocols SHOULD define rules for handling streams that
   are prematurely canceled by either endpoint.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
