---
title: "8.  IANA Considerations"
rfc_number: 9204
rfc_section: "8"
source_url: "https://www.rfc-editor.org/rfc/rfc9204"
description: "Section 8: IANA Considerations — RFC 9204 — QPACK: Field Compression for HTTP/3"
tags: [RFC9204, QPACK, header-compression, HTTP/3, dynamic-table, static-table, blocking-references, encoder-instructions, decoder-instructions, iana_considerations]
---

## 8.  IANA Considerations

8.  IANA Considerations

   This document makes multiple registrations in the registries defined
   by [HTTP/3].  The allocations created by this document are all
   assigned permanent status and list a change controller of the IETF
   and a contact of the HTTP working group (ietf-http-wg@w3.org).

## 8.1  Settings Registration

   This document specifies two settings.  The entries in the following
   table are registered in the "HTTP/3 Settings" registry established in
   [HTTP/3].

       +==========================+======+===============+=========+
       | Setting Name             | Code | Specification | Default |
       +==========================+======+===============+=========+
       | QPACK_MAX_TABLE_CAPACITY | 0x01 | Section 5     | 0       |
       +--------------------------+------+---------------+---------+
       | QPACK_BLOCKED_STREAMS    | 0x07 | Section 5     | 0       |
       +--------------------------+------+---------------+---------+

             Table 1: Additions to the HTTP/3 Settings Registry

   For formatting reasons, the setting names here are abbreviated by
   removing the 'SETTINGS_' prefix.

## 8.2  Stream Type Registration

   This document specifies two stream types.  The entries in the
   following table are registered in the "HTTP/3 Stream Types" registry
   established in [HTTP/3].

         +======================+======+===============+========+
         | Stream Type          | Code | Specification | Sender |
         +======================+======+===============+========+
         | QPACK Encoder Stream | 0x02 | Section 4.2   | Both   |
         +----------------------+------+---------------+--------+
         | QPACK Decoder Stream | 0x03 | Section 4.2   | Both   |
         +----------------------+------+---------------+--------+

          Table 2: Additions to the HTTP/3 Stream Types Registry

## 8.3  Error Code Registration

   This document specifies three error codes.  The entries in the
   following table are registered in the "HTTP/3 Error Codes" registry
   established in [HTTP/3].

   +============================+========+=============+===============+
   | Name                       | Code   |Description  | Specification |
   +============================+========+=============+===============+
   | QPACK_DECOMPRESSION_FAILED | 0x0200 |Decoding of a| Section 6     |
   |                            |        |field section|               |
   |                            |        |failed       |               |
   +----------------------------+--------+-------------+---------------+
   | QPACK_ENCODER_STREAM_ERROR | 0x0201 |Error on the | Section 6     |
   |                            |        |encoder      |               |
   |                            |        |stream       |               |
   +----------------------------+--------+-------------+---------------+
   | QPACK_DECODER_STREAM_ERROR | 0x0202 |Error on the | Section 6     |
   |                            |        |decoder      |               |
   |                            |        |stream       |               |
   +----------------------------+--------+-------------+---------------+

           Table 3: Additions to the HTTP/3 Error Codes Registry

---

**Navigation:** [[../RFC9204|RFC9204 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
