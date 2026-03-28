---
title: "13.1.  Packet Processing"
rfc_number: 9000
rfc_section: "13.1"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 13.1: Packet Processing — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, packet_processing]
---

# 13.1.  Packet Processing



   A sender sends one or more frames in a QUIC packet; see Section 12.4.

   A sender can minimize per-packet bandwidth and computational costs by
   including as many frames as possible in each QUIC packet.  A sender
> **MAY**: MAY wait for a short period of time to collect multiple frames before
   sending a packet that is not maximally packed, to avoid sending out
> **MAY**: large numbers of small packets.  An implementation MAY use knowledge
   about application sending behavior or heuristics to determine whether
   and for how long to wait.  This waiting period is an implementation
   decision, and an implementation should be careful to delay
   conservatively, since any delay is likely to increase application-
   visible latency.

   Stream multiplexing is achieved by interleaving STREAM frames from
   multiple streams into one or more QUIC packets.  A single QUIC packet
   can include multiple STREAM frames from one or more streams.

   One of the benefits of QUIC is avoidance of head-of-line blocking
   across multiple streams.  When a packet loss occurs, only streams
   with data in that packet are blocked waiting for a retransmission to
   be received, while other streams can continue making progress.  Note
   that when data from multiple streams is included in a single QUIC
   packet, loss of that packet blocks all those streams from making
   progress.  Implementations are advised to include as few streams as
   necessary in outgoing packets without losing transmission efficiency
   to underfilled packets.

## 13.1.  Packet Processing

> **MUST NOT**: A packet MUST NOT be acknowledged until packet protection has been
   successfully removed and all frames contained in the packet have been
   processed.  For STREAM frames, this means the data has been enqueued
   in preparation to be received by the application protocol, but it
   does not require that data be delivered and consumed.

   Once the packet has been fully processed, a receiver acknowledges
   receipt by sending one or more ACK frames containing the packet
   number of the received packet.

> **SHOULD**: An endpoint SHOULD treat receipt of an acknowledgment for a packet it
   did not send as a connection error of type PROTOCOL_VIOLATION, if it
   is able to detect the condition.  For further discussion of how this
   might be achieved, see Section 21.4.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
