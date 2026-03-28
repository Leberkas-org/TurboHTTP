---
title: "6.9.  WINDOW_UPDATE"
rfc_number: 9113
rfc_section: "6.9"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 6.9: WINDOW_UPDATE — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE]
---

## 6.9.  WINDOW_UPDATE

## 6.9  WINDOW_UPDATE

   The WINDOW_UPDATE frame (type=0x08) is used to implement flow
   control; see Section 5.2 for an overview.

   Flow control operates at two levels: on each individual stream and on
   the entire connection.

   Both types of flow control are hop by hop, that is, only between the
   two endpoints.  Intermediaries do not forward WINDOW_UPDATE frames
   between dependent connections.  However, throttling of data transfer
   by any receiver can indirectly cause the propagation of flow-control
   information toward the original sender.

   Flow control only applies to frames that are identified as being
   subject to flow control.  Of the frame types defined in this
   document, this includes only DATA frames.  Frames that are exempt
> **MUST**: from flow control MUST be accepted and processed, unless the receiver
   is unable to assign resources to handling the frame.  A receiver MAY
   respond with a stream error (Section 5.4.2) or connection error
   (Section 5.4.1) of type FLOW_CONTROL_ERROR if it is unable to accept
   a frame.

   WINDOW_UPDATE Frame {
     Length (24) = 0x04,
     Type (8) = 0x08,

     Unused Flags (8),

     Reserved (1),
     Stream Identifier (31),

     Reserved (1),
     Window Size Increment (31),
   }

                   Figure 11: WINDOW_UPDATE Frame Format

   The Length, Type, Unused Flag(s), Reserved, and Stream Identifier
   fields are described in Section 4.  The frame payload of a
   WINDOW_UPDATE frame is one reserved bit plus an unsigned 31-bit
   integer indicating the number of octets that the sender can transmit
   in addition to the existing flow-control window.  The legal range for
   the increment to the flow-control window is 1 to 2^31-1
   (2,147,483,647) octets.

   The WINDOW_UPDATE frame does not define any flags.

   The WINDOW_UPDATE frame can be specific to a stream or to the entire
   connection.  In the former case, the frame's stream identifier
   indicates the affected stream; in the latter, the value "0" indicates
   that the entire connection is the subject of the frame.

> **MUST**: A receiver MUST treat the receipt of a WINDOW_UPDATE frame with a
   flow-control window increment of 0 as a stream error (Section 5.4.2)
   of type PROTOCOL_ERROR; errors on the connection flow-control window
> **MUST**: MUST be treated as a connection error (Section 5.4.1).

   WINDOW_UPDATE can be sent by a peer that has sent a frame with the
   END_STREAM flag set.  This means that a receiver could receive a
   WINDOW_UPDATE frame on a stream in a "half-closed (remote)" or
> **MUST NOT**: "closed" state.  A receiver MUST NOT treat this as an error (see
   Section 5.1).

> **MUST**: A receiver that receives a flow-controlled frame MUST always account
   for its contribution against the connection flow-control window,
   unless the receiver treats this as a connection error
   (Section 5.4.1).  This is necessary even if the frame is in error.
   The sender counts the frame toward the flow-control window, but if
   the receiver does not, the flow-control window at the sender and
   receiver can become different.

> **MUST**: A WINDOW_UPDATE frame with a length other than 4 octets MUST be
   treated as a connection error (Section 5.4.1) of type
   FRAME_SIZE_ERROR.

### 6.9.1  The Flow-Control Window

   Flow control in HTTP/2 is implemented using a window kept by each
   sender on every stream.  The flow-control window is a simple integer
   value that indicates how many octets of data the sender is permitted
   to transmit; as such, its size is a measure of the buffering capacity
   of the receiver.

   Two flow-control windows are applicable: the stream flow-control
> **MUST NOT**: window and the connection flow-control window.  The sender MUST NOT
   send a flow-controlled frame with a length that exceeds the space
   available in either of the flow-control windows advertised by the
   receiver.  Frames with zero length with the END_STREAM flag set (that
> **MAY**: is, an empty DATA frame) MAY be sent if there is no available space
   in either flow-control window.

   For flow-control calculations, the 9-octet frame header is not
   counted.

   After sending a flow-controlled frame, the sender reduces the space
   available in both windows by the length of the transmitted frame.

   The receiver of a frame sends a WINDOW_UPDATE frame as it consumes
   data and frees up space in flow-control windows.  Separate
   WINDOW_UPDATE frames are sent for the stream- and connection-level
   flow-control windows.  Receivers are advised to have mechanisms in
   place to avoid sending WINDOW_UPDATE frames with very small
   increments; see Section 4.2.3.3 of [RFC1122].

   A sender that receives a WINDOW_UPDATE frame updates the
   corresponding window by the amount specified in the frame.

> **MUST NOT**: A sender MUST NOT allow a flow-control window to exceed 2^31-1
   octets.  If a sender receives a WINDOW_UPDATE that causes a flow-
> **MUST**: control window to exceed this maximum, it MUST terminate either the
   stream or the connection, as appropriate.  For streams, the sender
   sends a RST_STREAM with an error code of FLOW_CONTROL_ERROR; for the
   connection, a GOAWAY frame with an error code of FLOW_CONTROL_ERROR
   is sent.

   Flow-controlled frames from the sender and WINDOW_UPDATE frames from
   the receiver are completely asynchronous with respect to each other.
   This property allows a receiver to aggressively update the window
   size kept by the sender to prevent streams from stalling.

### 6.9.2  Initial Flow-Control Window Size

   When an HTTP/2 connection is first established, new streams are
   created with an initial flow-control window size of 65,535 octets.
   The connection flow-control window is also 65,535 octets.  Both
   endpoints can adjust the initial window size for new streams by
   including a value for SETTINGS_INITIAL_WINDOW_SIZE in the SETTINGS
   frame.  The connection flow-control window can only be changed using
   WINDOW_UPDATE frames.

   Prior to receiving a SETTINGS frame that sets a value for
   SETTINGS_INITIAL_WINDOW_SIZE, an endpoint can only use the default
   initial window size when sending flow-controlled frames.  Similarly,
   the connection flow-control window is set based on the default
   initial window size until a WINDOW_UPDATE frame is received.

   In addition to changing the flow-control window for streams that are
   not yet active, a SETTINGS frame can alter the initial flow-control
   window size for streams with active flow-control windows (that is,
   streams in the "open" or "half-closed (remote)" state).  When the
> **MUST**: value of SETTINGS_INITIAL_WINDOW_SIZE changes, a receiver MUST adjust
   the size of all stream flow-control windows that it maintains by the
   difference between the new value and the old value.

   A change to SETTINGS_INITIAL_WINDOW_SIZE can cause the available
> **MUST**: space in a flow-control window to become negative.  A sender MUST
   track the negative flow-control window and MUST NOT send new flow-
   controlled frames until it receives WINDOW_UPDATE frames that cause
   the flow-control window to become positive.

   For example, if the client sends 60 KB immediately on connection
   establishment and the server sets the initial window size to be 16
   KB, the client will recalculate the available flow-control window to
   be -44 KB on receipt of the SETTINGS frame.  The client retains a
   negative flow-control window until WINDOW_UPDATE frames restore the
   window to being positive, after which the client can resume sending.

   A SETTINGS frame cannot alter the connection flow-control window.

> **MUST**: An endpoint MUST treat a change to SETTINGS_INITIAL_WINDOW_SIZE that
   causes any flow-control window to exceed the maximum size as a
   connection error (Section 5.4.1) of type FLOW_CONTROL_ERROR.

### 6.9.3  Reducing the Stream Window Size

   A receiver that wishes to use a smaller flow-control window than the
   current size can send a new SETTINGS frame.  However, the receiver
> **MUST**: MUST be prepared to receive data that exceeds this window size, since
   the sender might send data that exceeds the lower limit prior to
   processing the SETTINGS frame.

   After sending a SETTINGS frame that reduces the initial flow-control
> **MAY**: window size, a receiver MAY continue to process streams that exceed
   flow-control limits.  Allowing streams to continue does not allow the
   receiver to immediately reduce the space it reserves for flow-control
   windows.  Progress on these streams can also stall, since
   WINDOW_UPDATE frames are needed to allow the sender to resume
> **MAY**: sending.  The receiver MAY instead send a RST_STREAM with an error
   code of FLOW_CONTROL_ERROR for the affected streams.

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
