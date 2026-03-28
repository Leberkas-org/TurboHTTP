---
title: "21.8.  Stream Commitment Attack"
rfc_number: 9000
rfc_section: "21.8"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.8: Stream Commitment Attack — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, stream_commitment_attack]
---

# 21.8.  Stream Commitment Attack


   An adversarial endpoint can open a large number of streams,
   exhausting state on an endpoint.  The adversarial endpoint could
   repeat the process on a large number of connections, in a manner
   similar to SYN flooding attacks in TCP.

   Normally, clients will open streams sequentially, as explained in
   Section 2.1.  However, when several streams are initiated at short
   intervals, loss or reordering can cause STREAM frames that open
   streams to be received out of sequence.  On receiving a higher-
   numbered stream ID, a receiver is required to open all intervening
   streams of the same type; see Section 3.2.  Thus, on a new
   connection, opening stream 4000000 opens 1 million and 1 client-
   initiated bidirectional streams.

   The number of active streams is limited by the
   initial_max_streams_bidi and initial_max_streams_uni transport
   parameters as updated by any received MAX_STREAMS frames, as
   explained in Section 4.6.  If chosen judiciously, these limits
   mitigate the effect of the stream commitment attack.  However,
   setting the limit too low could affect performance when applications
   expect to open a large number of streams.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
