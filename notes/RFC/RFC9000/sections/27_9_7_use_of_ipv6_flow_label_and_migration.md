---
title: "9.7.  Use of IPv6 Flow Label and Migration"
rfc_number: 9000
rfc_section: "9.7"
source_url: "https://www.rfc-editor.org/rfc/rfc9000"
description: "Section 9.7: Use of IPv6 Flow Label and Migration — RFC 9000 — QUIC: A UDP-Based Multiplexed and Secure Transport"
tags: [RFC9000, QUIC, transport, UDP, variable-length-integer, connection-migration, stream-multiplexing, loss-detection, use_of_ipv6_flow_label_and_migration]
---

# 9.7.  Use of IPv6 Flow Label and Migration


> **SHOULD**: Endpoints that send data using IPv6 SHOULD apply an IPv6 flow label
   in compliance with [RFC6437], unless the local API does not allow
   setting IPv6 flow labels.

> **MUST**: The flow label generation MUST be designed to minimize the chances of
   linkability with a previously used flow label, as a stable flow label
   would enable correlating activity on multiple paths; see Section 9.5.

   [RFC6437] suggests deriving values using a pseudorandom function to
   generate flow labels.  Including the Destination Connection ID field
   in addition to source and destination addresses when generating flow
   labels ensures that changes are synchronized with changes in other
   observable identifiers.  A cryptographic hash function that combines
   these inputs with a local secret is one way this might be
   implemented.

---

**Navigation:** [[../RFC9000|RFC9000 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
