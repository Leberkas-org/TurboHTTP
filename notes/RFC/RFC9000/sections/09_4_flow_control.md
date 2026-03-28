---
title: "4.  Flow Control"
rfc_number: 9000
rfc_section: "4"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 4: Flow Control — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, flow_control]
---

# 4.  Flow Control


   Receivers need to limit the amount of data that they are required to
   buffer, in order to prevent a fast sender from overwhelming them or a
   malicious sender from consuming a large amount of memory.  To enable
   a receiver to limit memory commitments for a connection, streams are
   flow controlled both individually and across a connection as a whole.
   A QUIC receiver controls the maximum amount of data the sender can
   send on a stream as well as across all streams at any time, as
   described in Sections 4.1 and 4.2.

   Similarly, to limit concurrency within a connection, a QUIC endpoint
   controls the maximum cumulative number of streams that its peer can
   initiate, as described in Section 4.6.

   Data sent in CRYPTO frames is not flow controlled in the same way as
   stream data.  QUIC relies on the cryptographic protocol
   implementation to avoid excessive buffering of data; see [QUIC-TLS].
   To avoid excessive buffering at multiple layers, QUIC implementations
> **SHOULD**: SHOULD provide an interface for the cryptographic protocol
   implementation to communicate its buffering limits.

## 4.1.  Data Flow Control

   QUIC employs a limit-based flow control scheme where a receiver
   advertises the limit of total bytes it is prepared to receive on a
   given stream or for the entire connection.  This leads to two levels
   of data flow control in QUIC:

   *  Stream flow control, which prevents a single stream from consuming
      the entire receive buffer for a connection by limiting the amount
      of data that can be sent on each stream.

   *  Connection flow control, which prevents senders from exceeding a
      receiver's buffer capacity for the connection by limiting the
      total bytes of stream data sent in STREAM frames on all streams.

> **MUST NOT**: Senders MUST NOT send data in excess of either limit.

   A receiver sets initial limits for all streams through transport
   parameters during the handshake (Section 7.4).  Subsequently, a
   receiver sends MAX_STREAM_DATA frames (Section 19.10) or MAX_DATA
   frames (Section 19.9) to the sender to advertise larger limits.

   A receiver can advertise a larger limit for a stream by sending a
   MAX_STREAM_DATA frame with the corresponding stream ID.  A
   MAX_STREAM_DATA frame indicates the maximum absolute byte offset of a
   stream.  A receiver could determine the flow control offset to be
   advertised based on the current offset of data consumed on that
   stream.

   A receiver can advertise a larger limit for a connection by sending a
   MAX_DATA frame, which indicates the maximum of the sum of the
   absolute byte offsets of all streams.  A receiver maintains a
   cumulative sum of bytes received on all streams, which is used to
   check for violations of the advertised connection or stream data
   limits.  A receiver could determine the maximum data limit to be
   advertised based on the sum of bytes consumed on all streams.

   Once a receiver advertises a limit for the connection or a stream, it
   is not an error to advertise a smaller limit, but the smaller limit
   has no effect.

> **MUST**: A receiver MUST close the connection with an error of type
   FLOW_CONTROL_ERROR if the sender violates the advertised connection
   or stream data limits; see Section 11 for details on error handling.

> **MUST**: A sender MUST ignore any MAX_STREAM_DATA or MAX_DATA frames that do
   not increase flow control limits.

   If a sender has sent data up to the limit, it will be unable to send
> **SHOULD**: new data and is considered blocked.  A sender SHOULD send a
   STREAM_DATA_BLOCKED or DATA_BLOCKED frame to indicate to the receiver
   that it has data to write but is blocked by flow control limits.  If
   a sender is blocked for a period longer than the idle timeout
   (Section 10.1), the receiver might close the connection even when the
   sender has data that is available for transmission.  To keep the
> **SHOULD**: connection from closing, a sender that is flow control limited SHOULD
   periodically send a STREAM_DATA_BLOCKED or DATA_BLOCKED frame when it
   has no ack-eliciting packets in flight.

## 4.2.  Increasing Flow Control Limits

   Implementations decide when and how much credit to advertise in
   MAX_STREAM_DATA and MAX_DATA frames, but this section offers a few
   considerations.

> **MAY**: To avoid blocking a sender, a receiver MAY send a MAX_STREAM_DATA or
   MAX_DATA frame multiple times within a round trip or send it early
   enough to allow time for loss of the frame and subsequent recovery.

   Control frames contribute to connection overhead.  Therefore,
   frequently sending MAX_STREAM_DATA and MAX_DATA frames with small
   changes is undesirable.  On the other hand, if updates are less
   frequent, larger increments to limits are necessary to avoid blocking
   a sender, requiring larger resource commitments at the receiver.
   There is a trade-off between resource commitment and overhead when
   determining how large a limit is advertised.

   A receiver can use an autotuning mechanism to tune the frequency and
   amount of advertised additional credit based on a round-trip time
   estimate and the rate at which the receiving application consumes
   data, similar to common TCP implementations.  As an optimization, an
   endpoint could send frames related to flow control only when there
   are other frames to send, ensuring that flow control does not cause
   extra packets to be sent.

   A blocked sender is not required to send STREAM_DATA_BLOCKED or
> **MUST NOT**: DATA_BLOCKED frames.  Therefore, a receiver MUST NOT wait for a
   STREAM_DATA_BLOCKED or DATA_BLOCKED frame before sending a
   MAX_STREAM_DATA or MAX_DATA frame; doing so could result in the
   sender being blocked for the rest of the connection.  Even if the
   sender sends these frames, waiting for them will result in the sender
   being blocked for at least an entire round trip.

   When a sender receives credit after being blocked, it might be able
   to send a large amount of data in response, resulting in short-term
   congestion; see Section 7.7 of [QUIC-RECOVERY] for a discussion of
   how a sender can avoid this congestion.

## 4.3.  Flow Control Performance

   If an endpoint cannot ensure that its peer always has available flow
   control credit that is greater than the peer's bandwidth-delay
   product on this connection, its receive throughput will be limited by
   flow control.

   Packet loss can cause gaps in the receive buffer, preventing the
   application from consuming data and freeing up receive buffer space.

   Sending timely updates of flow control limits can improve
   performance.  Sending packets only to provide flow control updates
   can increase network load and adversely affect performance.  Sending
   flow control updates along with other frames, such as ACK frames,
   reduces the cost of those updates.

## 4.4.  Handling Stream Cancellation

   Endpoints need to eventually agree on the amount of flow control
   credit that has been consumed on every stream, to be able to account
   for all bytes for connection-level flow control.

   On receipt of a RESET_STREAM frame, an endpoint will tear down state
   for the matching stream and ignore further data arriving on that
   stream.

   RESET_STREAM terminates one direction of a stream abruptly.  For a
   bidirectional stream, RESET_STREAM has no effect on data flow in the
> **MUST**: opposite direction.  Both endpoints MUST maintain flow control state
   for the stream in the unterminated direction until that direction
   enters a terminal state.

## 4.5.  Stream Final Size

   The final size is the amount of flow control credit that is consumed
   by a stream.  Assuming that every contiguous byte on the stream was
   sent once, the final size is the number of bytes sent.  More
   generally, this is one higher than the offset of the byte with the
   largest offset sent on the stream, or zero if no bytes were sent.

   A sender always communicates the final size of a stream to the
   receiver reliably, no matter how the stream is terminated.  The final
   size is the sum of the Offset and Length fields of a STREAM frame
   with a FIN flag, noting that these fields might be implicit.
   Alternatively, the Final Size field of a RESET_STREAM frame carries
   this value.  This guarantees that both endpoints agree on how much
   flow control credit was consumed by the sender on that stream.

   An endpoint will know the final size for a stream when the receiving
   part of the stream enters the "Size Known" or "Reset Recvd" state
> **MUST**: (Section 3).  The receiver MUST use the final size of the stream to
   account for all bytes sent on the stream in its connection-level flow
   controller.

> **MUST NOT**: An endpoint MUST NOT send data on a stream at or beyond the final
   size.

   Once a final size for a stream is known, it cannot change.  If a
   RESET_STREAM or STREAM frame is received indicating a change in the
> **SHOULD**: final size for the stream, an endpoint SHOULD respond with an error
   of type FINAL_SIZE_ERROR; see Section 11 for details on error
> **SHOULD**: handling.  A receiver SHOULD treat receipt of data at or beyond the
   final size as an error of type FINAL_SIZE_ERROR, even after a stream
   is closed.  Generating these errors is not mandatory, because
   requiring that an endpoint generate these errors also means that the
   endpoint needs to maintain the final size state for closed streams,
   which could mean a significant state commitment.

## 4.6.  Controlling Concurrency

   An endpoint limits the cumulative number of incoming streams a peer
   can open.  Only streams with a stream ID less than "(max_streams * 4
   + first_stream_id_of_type)" can be opened; see Table 1.  Initial
   limits are set in the transport parameters; see Section 18.2.
   Subsequent limits are advertised using MAX_STREAMS frames; see
   Section 19.11.  Separate limits apply to unidirectional and
   bidirectional streams.

   If a max_streams transport parameter or a MAX_STREAMS frame is
   received with a value greater than 2^60, this would allow a maximum
   stream ID that cannot be expressed as a variable-length integer; see
> **MUST**: Section 16.  If either is received, the connection MUST be closed
   immediately with a connection error of type TRANSPORT_PARAMETER_ERROR
   if the offending value was received in a transport parameter or of
   type FRAME_ENCODING_ERROR if it was received in a frame; see
   Section 10.2.

> **MUST NOT**: Endpoints MUST NOT exceed the limit set by their peer.  An endpoint
   that receives a frame with a stream ID exceeding the limit it has
> **MUST**: sent MUST treat this as a connection error of type
   STREAM_LIMIT_ERROR; see Section 11 for details on error handling.

   Once a receiver advertises a stream limit using the MAX_STREAMS
   frame, advertising a smaller limit has no effect.  MAX_STREAMS frames
> **MUST**: that do not increase the stream limit MUST be ignored.

   As with stream and connection flow control, this document leaves
   implementations to decide when and how many streams should be
   advertised to a peer via MAX_STREAMS.  Implementations might choose
   to increase limits as streams are closed, to keep the number of
   streams available to peers roughly consistent.

   An endpoint that is unable to open a new stream due to the peer's
> **SHOULD**: limits SHOULD send a STREAMS_BLOCKED frame (Section 19.14).  This
   signal is considered useful for debugging.  An endpoint MUST NOT wait
   to receive this signal before advertising additional credit, since
   doing so will mean that the peer will be blocked for at least an
   entire round trip, and potentially indefinitely if the peer chooses
   not to send STREAMS_BLOCKED frames.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
