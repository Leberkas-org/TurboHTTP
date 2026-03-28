---
title: "3.1.  Sending Stream States"
rfc_number: 9000
rfc_section: "3.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 3.1: Sending Stream States — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, sending_stream_states]
---

# 3.1.  Sending Stream States



   This section describes streams in terms of their send or receive
   components.  Two state machines are described: one for the streams on
   which an endpoint transmits data (Section 3.1) and another for
   streams on which an endpoint receives data (Section 3.2).

   Unidirectional streams use either the sending or receiving state
   machine, depending on the stream type and endpoint role.
   Bidirectional streams use both state machines at both endpoints.  For
   the most part, the use of these state machines is the same whether
   the stream is unidirectional or bidirectional.  The conditions for
   opening a stream are slightly more complex for a bidirectional stream
   because the opening of either the send or receive side causes the
   stream to open in both directions.

   The state machines shown in this section are largely informative.
   This document uses stream states to describe rules for when and how
   different types of frames can be sent and the reactions that are
   expected when different types of frames are received.  Though these
   state machines are intended to be useful in implementing QUIC, these
   states are not intended to constrain implementations.  An
   implementation can define a different state machine as long as its
   behavior is consistent with an implementation that implements these
   states.

      |  Note: In some cases, a single event or action can cause a
      |  transition through multiple states.  For instance, sending
      |  STREAM with a FIN bit set can cause two state transitions for a
      |  sending stream: from the "Ready" state to the "Send" state, and
      |  from the "Send" state to the "Data Sent" state.

## 3.1.  Sending Stream States

   Figure 2 shows the states for the part of a stream that sends data to
   a peer.

          o
          | Create Stream (Sending)
          | Peer Creates Bidirectional Stream
          v
      +-------+
      | Ready | Send RESET_STREAM
      |       |-----------------------.
      +-------+                       |
          |                           |
          | Send STREAM /             |
          |      STREAM_DATA_BLOCKED  |
          v                           |
      +-------+                       |
      | Send  | Send RESET_STREAM     |
      |       |---------------------->|
      +-------+                       |
          |                           |
          | Send STREAM + FIN         |
          v                           v
      +-------+                   +-------+
      | Data  | Send RESET_STREAM | Reset |
      | Sent  |------------------>| Sent  |
      +-------+                   +-------+
          |                           |
          | Recv All ACKs             | Recv ACK
          v                           v
      +-------+                   +-------+
      | Data  |                   | Reset |
      | Recvd |                   | Recvd |
      +-------+                   +-------+

               Figure 2: States for Sending Parts of Streams

   The sending part of a stream that the endpoint initiates (types 0 and
   2 for clients, 1 and 3 for servers) is opened by the application.
   The "Ready" state represents a newly created stream that is able to
   accept data from the application.  Stream data might be buffered in
   this state in preparation for sending.

   Sending the first STREAM or STREAM_DATA_BLOCKED frame causes a
   sending part of a stream to enter the "Send" state.  An
   implementation might choose to defer allocating a stream ID to a
   stream until it sends the first STREAM frame and enters this state,
   which can allow for better stream prioritization.

   The sending part of a bidirectional stream initiated by a peer (type
   0 for a server, type 1 for a client) starts in the "Ready" state when
   the receiving part is created.

   In the "Send" state, an endpoint transmits -- and retransmits as
   necessary -- stream data in STREAM frames.  The endpoint respects the
   flow control limits set by its peer and continues to accept and
   process MAX_STREAM_DATA frames.  An endpoint in the "Send" state
   generates STREAM_DATA_BLOCKED frames if it is blocked from sending by
   stream flow control limits (Section 4.1).

   After the application indicates that all stream data has been sent
   and a STREAM frame containing the FIN bit is sent, the sending part
   of the stream enters the "Data Sent" state.  From this state, the
   endpoint only retransmits stream data as necessary.  The endpoint
   does not need to check flow control limits or send
   STREAM_DATA_BLOCKED frames for a stream in this state.
   MAX_STREAM_DATA frames might be received until the peer receives the
   final stream offset.  The endpoint can safely ignore any
   MAX_STREAM_DATA frames it receives from its peer for a stream in this
   state.

   Once all stream data has been successfully acknowledged, the sending
   part of the stream enters the "Data Recvd" state, which is a terminal
   state.

   From any state that is one of "Ready", "Send", or "Data Sent", an
   application can signal that it wishes to abandon transmission of
   stream data.  Alternatively, an endpoint might receive a STOP_SENDING
   frame from its peer.  In either case, the endpoint sends a
   RESET_STREAM frame, which causes the stream to enter the "Reset Sent"
   state.

> **MAY**: An endpoint MAY send a RESET_STREAM as the first frame that mentions
   a stream; this causes the sending part of that stream to open and
   then immediately transition to the "Reset Sent" state.

   Once a packet containing a RESET_STREAM has been acknowledged, the
   sending part of the stream enters the "Reset Recvd" state, which is a
   terminal state.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
