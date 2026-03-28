---
title: "21.7.  Stream Fragmentation and Reassembly Attacks"
rfc_number: 9000
rfc_section: "21.7"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.7: Stream Fragmentation and Reassembly Attacks — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, stream_fragmentation_and_reassembly_attacks]
---

# 21.7.  Stream Fragmentation and Reassembly Attacks


   An adversarial sender might intentionally not send portions of the
   stream data, causing the receiver to commit resources for the unsent
   data.  This could cause a disproportionate receive buffer memory
   commitment and/or the creation of a large and inefficient data
   structure at the receiver.

   An adversarial receiver might intentionally not acknowledge packets
   containing stream data in an attempt to force the sender to store the
   unacknowledged stream data for retransmission.

   The attack on receivers is mitigated if flow control windows
   correspond to available memory.  However, some receivers will
   overcommit memory and advertise flow control offsets in the aggregate
   that exceed actual available memory.  The overcommitment strategy can
   lead to better performance when endpoints are well behaved, but
   renders endpoints vulnerable to the stream fragmentation attack.

> **SHOULD**: QUIC deployments SHOULD provide mitigations for stream fragmentation
   attacks.  Mitigations could consist of avoiding overcommitting
   memory, limiting the size of tracking data structures, delaying
   reassembly of STREAM frames, implementing heuristics based on the age
   and duration of reassembly holes, or some combination of these.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
