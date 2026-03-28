---
title: "3.3.  Permitted Frame Types"
rfc_number: 9000
rfc_section: "3.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 3.3: Permitted Frame Types — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, permitted_frame_types]
---

# 3.3.  Permitted Frame Types


   The sender of a stream sends just three frame types that affect the
   state of a stream at either the sender or the receiver: STREAM
   (Section 19.8), STREAM_DATA_BLOCKED (Section 19.13), and RESET_STREAM
   (Section 19.4).

> **MUST NOT**: A sender MUST NOT send any of these frames from a terminal state
   ("Data Recvd" or "Reset Recvd").  A sender MUST NOT send a STREAM or
   STREAM_DATA_BLOCKED frame for a stream in the "Reset Sent" state or
   any terminal state -- that is, after sending a RESET_STREAM frame.  A
   receiver could receive any of these three frames in any state, due to
   the possibility of delayed delivery of packets carrying them.

   The receiver of a stream sends MAX_STREAM_DATA frames (Section 19.10)
   and STOP_SENDING frames (Section 19.5).

   The receiver only sends MAX_STREAM_DATA frames in the "Recv" state.
> **MAY**: A receiver MAY send a STOP_SENDING frame in any state where it has
   not received a RESET_STREAM frame -- that is, states other than
   "Reset Recvd" or "Reset Read".  However, there is little value in
   sending a STOP_SENDING frame in the "Data Recvd" state, as all stream
   data has been received.  A sender could receive either of these two
   types of frames in any state as a result of delayed delivery of
   packets.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
