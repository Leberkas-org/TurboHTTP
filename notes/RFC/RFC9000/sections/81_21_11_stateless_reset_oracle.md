---
title: "21.11.  Stateless Reset Oracle"
rfc_number: 9000
rfc_section: "21.11"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.11: Stateless Reset Oracle — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, stateless_reset_oracle]
---

# 21.11.  Stateless Reset Oracle


   Stateless resets create a possible denial-of-service attack analogous
   to a TCP reset injection.  This attack is possible if an attacker is
   able to cause a stateless reset token to be generated for a
   connection with a selected connection ID.  An attacker that can cause
   this token to be generated can reset an active connection with the
   same connection ID.

   If a packet can be routed to different instances that share a static
   key -- for example, by changing an IP address or port -- then an
   attacker can cause the server to send a stateless reset.  To defend
   against this style of denial of service, endpoints that share a
> **MUST**: static key for stateless resets (see Section 10.3.2) MUST be arranged
   so that packets with a given connection ID always arrive at an
   instance that has connection state, unless that connection is no
   longer active.

> **MUST NOT**: More generally, servers MUST NOT generate a stateless reset if a
   connection with the corresponding connection ID could be active on
   any endpoint using the same static key.

   In the case of a cluster that uses dynamic load balancing, it is
   possible that a change in load-balancer configuration could occur
   while an active instance retains connection state.  Even if an
   instance retains connection state, the change in routing and
   resulting stateless reset will result in the connection being
   terminated.  If there is no chance of the packet being routed to the
   correct instance, it is better to send a stateless reset than wait
   for the connection to time out.  However, this is acceptable only if
   the routing cannot be influenced by an attacker.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
