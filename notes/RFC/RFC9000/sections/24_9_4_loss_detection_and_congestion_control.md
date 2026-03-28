---
title: "9.4.  Loss Detection and Congestion Control"
rfc_number: 9000
rfc_section: "9.4"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 9.4: Loss Detection and Congestion Control — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, loss_detection_and_congestion_control]
---

# 9.4.  Loss Detection and Congestion Control


   The capacity available on the new path might not be the same as the
> **MUST NOT**: old path.  Packets sent on the old path MUST NOT contribute to
   congestion control or RTT estimation for the new path.

> **MUST**: On confirming a peer's ownership of its new address, an endpoint MUST
   immediately reset the congestion controller and round-trip time
   estimator for the new path to initial values (see Appendices A.3 and
   B.3 of [QUIC-RECOVERY]) unless the only change in the peer's address
   is its port number.  Because port-only changes are commonly the
> **MAY**: result of NAT rebinding or other middlebox activity, the endpoint MAY
   instead retain its congestion control state and round-trip estimate
   in those cases instead of reverting to initial values.  In cases
   where congestion control state retained from an old path is used on a
   new path with substantially different characteristics, a sender could
   transmit too aggressively until the congestion controller and the RTT
   estimator have adapted.  Generally, implementations are advised to be
   cautious when using previous values on a new path.

   There could be apparent reordering at the receiver when an endpoint
   sends data and probes from/to multiple addresses during the migration
   period, since the two resulting paths could have different round-trip
   times.  A receiver of packets on multiple paths will still send ACK
   frames covering all received packets.

   While multiple paths might be used during connection migration, a
   single congestion control context and a single loss recovery context
   (as described in [QUIC-RECOVERY]) could be adequate.  For instance,
   an endpoint might delay switching to a new congestion control context
   until it is confirmed that an old path is no longer needed (such as
   the case described in Section 9.3.3).

   A sender can make exceptions for probe packets so that their loss
   detection is independent and does not unduly cause the congestion
   controller to reduce its sending rate.  An endpoint might set a
   separate timer when a PATH_CHALLENGE is sent, which is canceled if
   the corresponding PATH_RESPONSE is received.  If the timer fires
   before the PATH_RESPONSE is received, the endpoint might send a new
   PATH_CHALLENGE and restart the timer for a longer period of time.
> **SHOULD**: This timer SHOULD be set as described in Section 6.2.1 of
   [QUIC-RECOVERY] and MUST NOT be more aggressive.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
