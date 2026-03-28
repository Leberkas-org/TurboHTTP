---
title: "6.  Error Handling"
rfc_number: 9204
rfc_section: "6"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 6: Error Handling — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, error_handling]
---

## 6.  Error Handling

6.  Error Handling

   The following error codes are defined for HTTP/3 to indicate failures
   of QPACK that prevent the stream or connection from continuing:

   QPACK_DECOMPRESSION_FAILED (0x0200):  The decoder failed to interpret
      an encoded field section and is not able to continue decoding that
      field section.

   QPACK_ENCODER_STREAM_ERROR (0x0201):  The decoder failed to interpret
      an encoder instruction received on the encoder stream.

   QPACK_DECODER_STREAM_ERROR (0x0202):  The encoder failed to interpret
      a decoder instruction received on the decoder stream.

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
