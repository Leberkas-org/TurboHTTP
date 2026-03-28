---
title: "3.4.  Bidirectional Stream States"
rfc_number: 9000
rfc_section: "3.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 3.4: Bidirectional Stream States — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, bidirectional_stream_states]
---

# 3.4.  Bidirectional Stream States


   A bidirectional stream is composed of sending and receiving parts.
   Implementations can represent states of the bidirectional stream as
   composites of sending and receiving stream states.  The simplest
   model presents the stream as "open" when either sending or receiving
   parts are in a non-terminal state and "closed" when both sending and
   receiving streams are in terminal states.

   Table 2 shows a more complex mapping of bidirectional stream states
   that loosely correspond to the stream states defined in HTTP/2
   [HTTP2].  This shows that multiple states on sending or receiving
   parts of streams are mapped to the same composite state.  Note that
   this is just one possibility for such a mapping; this mapping
   requires that data be acknowledged before the transition to a
   "closed" or "half-closed" state.

      +===================+=======================+=================+
      | Sending Part      | Receiving Part        | Composite State |
      +===================+=======================+=================+
      | No Stream / Ready | No Stream / Recv (*1) | idle            |
      +-------------------+-----------------------+-----------------+
      | Ready / Send /    | Recv / Size Known     | open            |
      | Data Sent         |                       |                 |
      +-------------------+-----------------------+-----------------+
      | Ready / Send /    | Data Recvd / Data     | half-closed     |
      | Data Sent         | Read                  | (remote)        |
      +-------------------+-----------------------+-----------------+
      | Ready / Send /    | Reset Recvd / Reset   | half-closed     |
      | Data Sent         | Read                  | (remote)        |
      +-------------------+-----------------------+-----------------+
      | Data Recvd        | Recv / Size Known     | half-closed     |
      |                   |                       | (local)         |
      +-------------------+-----------------------+-----------------+
      | Reset Sent /      | Recv / Size Known     | half-closed     |
      | Reset Recvd       |                       | (local)         |
      +-------------------+-----------------------+-----------------+
      | Reset Sent /      | Data Recvd / Data     | closed          |
      | Reset Recvd       | Read                  |                 |
      +-------------------+-----------------------+-----------------+
      | Reset Sent /      | Reset Recvd / Reset   | closed          |
      | Reset Recvd       | Read                  |                 |
      +-------------------+-----------------------+-----------------+
      | Data Recvd        | Data Recvd / Data     | closed          |
      |                   | Read                  |                 |
      +-------------------+-----------------------+-----------------+
      | Data Recvd        | Reset Recvd / Reset   | closed          |
      |                   | Read                  |                 |
      +-------------------+-----------------------+-----------------+

            Table 2: Possible Mapping of Stream States to HTTP/2

      |  Note (*1): A stream is considered "idle" if it has not yet been
      |  created or if the receiving part of the stream is in the "Recv"
      |  state without yet having received any frames.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
