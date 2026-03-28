---
title: "5.  Configuration"
rfc_number: 9204
rfc_section: "5"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 5: Configuration — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, configuration]
---

## 5.  Configuration

5.  Configuration

   QPACK defines two settings for the HTTP/3 SETTINGS frame:

   SETTINGS_QPACK_MAX_TABLE_CAPACITY (0x01):  The default value is zero.
      See Section 3.2 for usage.  This is the equivalent of the
      SETTINGS_HEADER_TABLE_SIZE from HTTP/2.

   SETTINGS_QPACK_BLOCKED_STREAMS (0x07):  The default value is zero.
      See Section 2.1.2.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
