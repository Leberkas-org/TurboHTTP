---
title: "21.6.  Slowloris Attacks"
rfc_number: 9000
rfc_section: "21.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 21.6: Slowloris Attacks — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, slowloris_attacks]
---

# 21.6.  Slowloris Attacks


   The attacks commonly known as Slowloris [SLOWLORIS] try to keep many
   connections to the target endpoint open and hold them open as long as
   possible.  These attacks can be executed against a QUIC endpoint by
   generating the minimum amount of activity necessary to avoid being
   closed for inactivity.  This might involve sending small amounts of
   data, gradually opening flow control windows in order to control the
   sender rate, or manufacturing ACK frames that simulate a high loss
   rate.

> **SHOULD**: QUIC deployments SHOULD provide mitigations for the Slowloris
   attacks, such as increasing the maximum number of clients the server
   will allow, limiting the number of connections a single IP address is
   allowed to make, imposing restrictions on the minimum transfer speed
   a connection is allowed to have, and restricting the length of time
   an endpoint is allowed to stay connected.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
