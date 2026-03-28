---
title: "8.6.  The Upgrade Header Field"
rfc_number: 9113
rfc_section: "8.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.6: The Upgrade Header Field — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, the_upgrade_header_field]
---

## 8.6.  The Upgrade Header Field

## 8.6  The Upgrade Header Field

   HTTP/2 does not support the 101 (Switching Protocols) informational
   status code (Section 15.2.2 of [HTTP]).

   The semantics of 101 (Switching Protocols) aren't applicable to a
   multiplexed protocol.  Similar functionality might be enabled through
   the use of extended CONNECT [RFC8441], and other protocols are able
   to use the same mechanisms that HTTP/2 uses to negotiate their use
   (see Section 3).

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
