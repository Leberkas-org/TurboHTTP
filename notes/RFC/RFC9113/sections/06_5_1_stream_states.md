---
title: "5.1.  Stream States"
rfc_number: 9113
rfc_section: "5.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 5.1: Stream States — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, stream_states]
---

## 5.1.  Stream States

5.  Streams and Multiplexing

   A "stream" is an independent, bidirectional sequence of frames
   exchanged between the client and server within an HTTP/2 connection.
   Streams have several important characteristics:

   *  A single HTTP/2 connection can contain multiple concurrently open
      streams, with either endpoint interleaving frames from multiple
      streams.

   *  Streams can be established and used unilaterally or shared by
      either endpoint.

   *  Streams can be closed by either endpoint.

   *  The order in which frames are sent is significant.  Recipients
      process frames in the order they are received.  In particular, the
      order of HEADERS and DATA frames is semantically significant.

   *  Streams are identified by an integer.  Stream identifiers are
      assigned to streams by the endpoint initiating the stream.

## 5.1  Stream States

   The lifecycle of a stream is shown in Figure 2.

                                +--------+
                        send PP |        | recv PP
                       ,--------+  idle  +--------.
                      /         |        |         \
                     v          +--------+          v
              +----------+          |           +----------+
              |          |          | send H /  |          |
       ,------+ reserved |          | recv H    | reserved +------.
       |      | (local)  |          |           | (remote) |      |
       |      +---+------+          v           +------+---+      |
       |          |             +--------+             |          |
       |          |     recv ES |        | send ES     |          |
       |   send H |     ,-------+  open  +-------.     | recv H   |
       |          |    /        |        |        \    |          |
       |          v   v         +---+----+         v   v          |
       |      +----------+          |           +----------+      |
       |      |   half-  |          |           |   half-  |      |
       |      |  closed  |          | send R /  |  closed  |      |
       |      | (remote) |          | recv R    | (local)  |      |
       |      +----+-----+          |           +-----+----+      |
       |           |                |                 |           |
       |           | send ES /      |       recv ES / |           |
       |           |  send R /      v        send R / |           |
       |           |  recv R    +--------+   recv R   |           |
       | send R /  `----------->|        |<-----------'  send R / |
       | recv R                 | closed |               recv R   |
       `----------------------->|        |<-----------------------'
                                +--------+

                          Figure 2: Stream States

   send:  endpoint sends this frame
   recv:  endpoint receives this frame
   H:  HEADERS frame (with implied CONTINUATION frames)
   ES:  END_STREAM flag
   R:  RST_STREAM frame
   PP:  PUSH_PROMISE frame (with implied CONTINUATION frames); state
      transitions are for the promised stream

   Note that this diagram shows stream state transitions and the frames
   and flags that affect those transitions only.  In this regard,
   CONTINUATION frames do not result in state transitions; they are
   effectively part of the HEADERS or PUSH_PROMISE that they follow.
   For the purpose of state transitions, the END_STREAM flag is
   processed as a separate event to the frame that bears it; a HEADERS
   frame with the END_STREAM flag set can cause two state transitions.

   Both endpoints have a subjective view of the state of a stream that
   could be different when frames are in transit.  Endpoints do not
   coordinate the creation of streams; they are created unilaterally by
   either endpoint.  The negative consequences of a mismatch in states
   are limited to the "closed" state after sending RST_STREAM, where
   frames might be received for some time after closing.

   Streams have the following states:

   idle:  All streams start in the "idle" state.

      The following transitions are valid from this state:

      *  Sending a HEADERS frame as a client, or receiving a HEADERS
         frame as a server, causes the stream to become "open".  The
         stream identifier is selected as described in Section 5.1.1.
         The same HEADERS frame can also cause a stream to immediately
         become "half-closed".

      *  Sending a PUSH_PROMISE frame on another stream reserves the
         idle stream that is identified for later use.  The stream state
         for the reserved stream transitions to "reserved (local)".
         Only a server may send PUSH_PROMISE frames.

      *  Receiving a PUSH_PROMISE frame on another stream reserves an
         idle stream that is identified for later use.  The stream state
         for the reserved stream transitions to "reserved (remote)".
         Only a client may receive PUSH_PROMISE frames.

      *  Note that the PUSH_PROMISE frame is not sent on the idle stream
         but references the newly reserved stream in the Promised Stream
         ID field.

      *  Opening a stream with a higher-valued stream identifier causes
         the stream to transition immediately to a "closed" state; note
         that this transition is not shown in the diagram.

      Receiving any frame other than HEADERS or PRIORITY on a stream in
> **MUST**: this state MUST be treated as a connection error (Section 5.4.1)
      of type PROTOCOL_ERROR.  If this stream is initiated by the
      server, as described in Section 5.1.1, then receiving a HEADERS
> **MUST**: frame MUST also be treated as a connection error (Section 5.4.1)
      of type PROTOCOL_ERROR.

   reserved (local):  A stream in the "reserved (local)" state is one
      that has been promised by sending a PUSH_PROMISE frame.  A
      PUSH_PROMISE frame reserves an idle stream by associating the
      stream with an open stream that was initiated by the remote peer
      (see Section 8.4).

      In this state, only the following transitions are possible:

      *  The endpoint can send a HEADERS frame.  This causes the stream
         to open in a "half-closed (remote)" state.

      *  Either endpoint can send a RST_STREAM frame to cause the stream
         to become "closed".  This releases the stream reservation.

> **MUST NOT**: An endpoint MUST NOT send any type of frame other than HEADERS,
      RST_STREAM, or PRIORITY in this state.

> **MAY**: A PRIORITY or WINDOW_UPDATE frame MAY be received in this state.
      Receiving any type of frame other than RST_STREAM, PRIORITY, or
> **MUST**: WINDOW_UPDATE on a stream in this state MUST be treated as a
      connection error (Section 5.4.1) of type PROTOCOL_ERROR.

   reserved (remote):  A stream in the "reserved (remote)" state has
      been reserved by a remote peer.

      In this state, only the following transitions are possible:

      *  Receiving a HEADERS frame causes the stream to transition to
         "half-closed (local)".

      *  Either endpoint can send a RST_STREAM frame to cause the stream
         to become "closed".  This releases the stream reservation.

> **MUST NOT**: An endpoint MUST NOT send any type of frame other than RST_STREAM,
      WINDOW_UPDATE, or PRIORITY in this state.

      Receiving any type of frame other than HEADERS, RST_STREAM, or
> **MUST**: PRIORITY on a stream in this state MUST be treated as a connection
      error (Section 5.4.1) of type PROTOCOL_ERROR.

   open:  A stream in the "open" state may be used by both peers to send
      frames of any type.  In this state, sending peers observe
      advertised stream-level flow-control limits (Section 5.2).

      From this state, either endpoint can send a frame with an
      END_STREAM flag set, which causes the stream to transition into
      one of the "half-closed" states.  An endpoint sending an
      END_STREAM flag causes the stream state to become "half-closed
      (local)"; an endpoint receiving an END_STREAM flag causes the
      stream state to become "half-closed (remote)".

      Either endpoint can send a RST_STREAM frame from this state,
      causing it to transition immediately to "closed".

   half-closed (local):  A stream that is in the "half-closed (local)"
      state cannot be used for sending frames other than WINDOW_UPDATE,
      PRIORITY, and RST_STREAM.

      A stream transitions from this state to "closed" when a frame is
      received with the END_STREAM flag set or when either peer sends a
      RST_STREAM frame.

      An endpoint can receive any type of frame in this state.
      Providing flow-control credit using WINDOW_UPDATE frames is
      necessary to continue receiving flow-controlled frames.  In this
      state, a receiver can ignore WINDOW_UPDATE frames, which might
      arrive for a short period after a frame with the END_STREAM flag
      set is sent.

      PRIORITY frames can be received in this state.

   half-closed (remote):  A stream that is "half-closed (remote)" is no
      longer being used by the peer to send frames.  In this state, an
      endpoint is no longer obligated to maintain a receiver flow-
      control window.

      If an endpoint receives additional frames, other than
      WINDOW_UPDATE, PRIORITY, or RST_STREAM, for a stream that is in
> **MUST**: this state, it MUST respond with a stream error (Section 5.4.2) of
      type STREAM_CLOSED.

      A stream that is "half-closed (remote)" can be used by the
      endpoint to send frames of any type.  In this state, the endpoint
      continues to observe advertised stream-level flow-control limits
      (Section 5.2).

      A stream can transition from this state to "closed" by sending a
      frame with the END_STREAM flag set or when either peer sends a
      RST_STREAM frame.

   closed:  The "closed" state is the terminal state.

      A stream enters the "closed" state after an endpoint both sends
      and receives a frame with an END_STREAM flag set.  A stream also
      enters the "closed" state after an endpoint either sends or
      receives a RST_STREAM frame.

> **MUST NOT**: An endpoint MUST NOT send frames other than PRIORITY on a closed
   stream.  An endpoint MAY treat receipt of any other type of frame
      on a closed stream as a connection error (Section 5.4.1) of type
      STREAM_CLOSED, except as noted below.

      An endpoint that sends a frame with the END_STREAM flag set or a
      RST_STREAM frame might receive a WINDOW_UPDATE or RST_STREAM frame
      from its peer in the time before the peer receives and processes
      the frame that closes the stream.

      An endpoint that sends a RST_STREAM frame on a stream that is in
      the "open" or "half-closed (local)" state could receive any type
      of frame.  The peer might have sent or enqueued for sending these
> **MUST**: frames before processing the RST_STREAM frame.  An endpoint MUST
      minimally process and then discard any frames it receives in this
      state.  This means updating header compression state for HEADERS
      and PUSH_PROMISE frames.  Receiving a PUSH_PROMISE frame also
      causes the promised stream to become "reserved (remote)", even
      when the PUSH_PROMISE frame is received on a closed stream.
      Additionally, the content of DATA frames counts toward the
      connection flow-control window.

      An endpoint can perform this minimal processing for all streams
> **MAY**: that are in the "closed" state.  Endpoints MAY use other signals
      to detect that a peer has received the frames that caused the
      stream to enter the "closed" state and treat receipt of any frame
      other than PRIORITY as a connection error (Section 5.4.1) of type
      PROTOCOL_ERROR.  Endpoints can use frames that indicate that the
      peer has received the closing signal to drive this.  Endpoints
> **SHOULD NOT**: SHOULD NOT use timers for this purpose.  For example, an endpoint
      that sends a SETTINGS frame after closing a stream can safely
      treat receipt of a DATA frame on that stream as an error after
      receiving an acknowledgment of the settings.  Other things that
      might be used are PING frames, receiving data on streams that were
      created after closing the stream, or responses to requests created
      after closing the stream.

> **SHOULD**: In the absence of more specific rules, implementations SHOULD treat
   the receipt of a frame that is not expressly permitted in the
   description of a state as a connection error (Section 5.4.1) of type
   PROTOCOL_ERROR.  Note that PRIORITY can be sent and received in any
   stream state.

   The rules in this section only apply to frames defined in this
   document.  Receipt of frames for which the semantics are unknown
   cannot be treated as an error, as the conditions for sending and
   receiving those frames are also unknown; see Section 5.5.

   An example of the state transitions for an HTTP request/response
   exchange can be found in Section 8.8.  An example of the state
   transitions for server push can be found in Sections 8.4.1 and 8.4.2.

### 5.1.1  Stream Identifiers

   Streams are identified by an unsigned 31-bit integer.  Streams
> **MUST**: initiated by a client MUST use odd-numbered stream identifiers; those
   initiated by the server MUST use even-numbered stream identifiers.  A
   stream identifier of zero (0x00) is used for connection control
   messages; the stream identifier of zero cannot be used to establish a
   new stream.

> **MUST**: The identifier of a newly established stream MUST be numerically
   greater than all streams that the initiating endpoint has opened or
   reserved.  This governs streams that are opened using a HEADERS frame
   and streams that are reserved using PUSH_PROMISE.  An endpoint that
> **MUST**: receives an unexpected stream identifier MUST respond with a
   connection error (Section 5.4.1) of type PROTOCOL_ERROR.

   A HEADERS frame will transition the client-initiated stream
   identified by the stream identifier in the frame header from "idle"
   to "open".  A PUSH_PROMISE frame will transition the server-initiated
   stream identified by the Promised Stream ID field in the frame
   payload from "idle" to "reserved (local)" or "reserved (remote)".
   When a stream transitions out of the "idle" state, all streams in the
   "idle" state that might have been opened by the peer with a lower-
   valued stream identifier immediately transition to "closed".  That
   is, an endpoint may skip a stream identifier, with the effect being
   that the skipped stream is immediately closed.

   Stream identifiers cannot be reused.  Long-lived connections can
   result in an endpoint exhausting the available range of stream
   identifiers.  A client that is unable to establish a new stream
   identifier can establish a new connection for new streams.  A server
   that is unable to establish a new stream identifier can send a GOAWAY
   frame so that the client is forced to open a new connection for new
   streams.

### 5.1.2  Stream Concurrency

   A peer can limit the number of concurrently active streams using the
   SETTINGS_MAX_CONCURRENT_STREAMS parameter (see Section 6.5.2) within
   a SETTINGS frame.  The maximum concurrent streams setting is specific
   to each endpoint and applies only to the peer that receives the
   setting.  That is, clients specify the maximum number of concurrent
   streams the server can initiate, and servers specify the maximum
   number of concurrent streams the client can initiate.

   Streams that are in the "open" state or in either of the "half-
   closed" states count toward the maximum number of streams that an
   endpoint is permitted to open.  Streams in any of these three states
   count toward the limit advertised in the
   SETTINGS_MAX_CONCURRENT_STREAMS setting.  Streams in either of the
   "reserved" states do not count toward the stream limit.

> **MUST NOT**: Endpoints MUST NOT exceed the limit set by their peer.  An endpoint
   that receives a HEADERS frame that causes its advertised concurrent
> **MUST**: stream limit to be exceeded MUST treat this as a stream error
   (Section 5.4.2) of type PROTOCOL_ERROR or REFUSED_STREAM.  The choice
   of error code determines whether the endpoint wishes to enable
   automatic retry (see Section 8.7 for details).

   An endpoint that wishes to reduce the value of
   SETTINGS_MAX_CONCURRENT_STREAMS to a value that is below the current
   number of open streams can either close streams that exceed the new
   value or allow streams to complete.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes

- **`Http2StreamStateMachine.cs`** — Implements the full stream state machine (idle → open → half-closed → closed) per §5.1 Figure 2; validates state transitions and raises `PROTOCOL_ERROR` or `STREAM_CLOSED` for invalid transitions
- **`Http2StreamManager.cs`** — Manages concurrent stream tracking; enforces `SETTINGS_MAX_CONCURRENT_STREAMS` per §5.1.2; assigns odd-numbered stream IDs for client-initiated streams per §5.1.1
- **`Http2Connection.cs`** — Coordinates stream lifecycle across the connection; handles RST_STREAM and END_STREAM flag processing for state transitions

### Test References

- `TurboHttp.Tests/RFC9113/05_Http2StreamStateTests.cs` — Stream state machine transitions, invalid state detection
- `TurboHttp.Tests/RFC9113/06_Http2StreamIdTests.cs` — Stream identifier ordering, odd/even validation
- `TurboHttp.Tests/RFC9113/07_Http2ConcurrencyTests.cs` — `SETTINGS_MAX_CONCURRENT_STREAMS` enforcement

### Known Gaps

- ⚠️ `SETTINGS_MAX_CONCURRENT_STREAMS` enforcement — tracked but not actively enforced as a hard limit when the server hasn't advertised a value (initial value is unlimited per spec)
- ❌ Reserved stream states (§5.1 reserved local/remote) — not fully implemented since server push (`PUSH_PROMISE`) is not supported
