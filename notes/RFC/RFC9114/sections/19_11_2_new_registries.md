---
title: "11.2.  New Registries"
rfc_number: 9114
rfc_section: "11.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 11.2: New Registries — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, new_registries]
---

## 11.2.  New Registries

## 11.2  New Registries

   New registries created in this document operate under the QUIC
   registration policy documented in Section 22.1 of [QUIC-TRANSPORT].
   These registries all include the common set of fields listed in
   Section 22.1.1 of [QUIC-TRANSPORT].  These registries are collected
   under the "Hypertext Transfer Protocol version 3 (HTTP/3)" heading.

   The initial allocations in these registries are all assigned
   permanent status and list a change controller of the IETF and a
   contact of the HTTP working group (ietf-http-wg@w3.org).

### 11.2.1  Frame Types

   This document establishes a registry for HTTP/3 frame type codes.
   The "HTTP/3 Frame Types" registry governs a 62-bit space.  This
   registry follows the QUIC registry policy; see Section 11.2.
   Permanent registrations in this registry are assigned using the
   Specification Required policy ([RFC8126]), except for values between
   0x00 and 0x3f (in hexadecimal; inclusive), which are assigned using
   Standards Action or IESG Approval as defined in Sections 4.9 and 4.10
   of [RFC8126].

   While this registry is separate from the "HTTP/2 Frame Type" registry
   defined in [HTTP/2], it is preferable that the assignments parallel
   each other where the code spaces overlap.  If an entry is present in
> **SHOULD**: only one registry, every effort SHOULD be made to avoid assigning the
   corresponding value to an unrelated operation.  Expert reviewers MAY
   reject unrelated registrations that would conflict with the same
   value in the corresponding registry.

   In addition to common fields as described in Section 11.2, permanent
> **MUST**: registrations in this registry MUST include the following field:

   Frame Type:  A name or label for the frame type.

> **MUST**: Specifications of frame types MUST include a description of the frame
   layout and its semantics, including any parts of the frame that are
   conditionally present.

   The entries in Table 2 are registered by this document.

                 +==============+=======+===============+
                 | Frame Type   | Value | Specification |
                 +==============+=======+===============+
                 | DATA         |  0x00 | Section 7.2.1 |
                 +--------------+-------+---------------+
                 | HEADERS      |  0x01 | Section 7.2.2 |
                 +--------------+-------+---------------+
                 | Reserved     |  0x02 | This document |
                 +--------------+-------+---------------+
                 | CANCEL_PUSH  |  0x03 | Section 7.2.3 |
                 +--------------+-------+---------------+
                 | SETTINGS     |  0x04 | Section 7.2.4 |
                 +--------------+-------+---------------+
                 | PUSH_PROMISE |  0x05 | Section 7.2.5 |
                 +--------------+-------+---------------+
                 | Reserved     |  0x06 | This document |
                 +--------------+-------+---------------+
                 | GOAWAY       |  0x07 | Section 7.2.6 |
                 +--------------+-------+---------------+
                 | Reserved     |  0x08 | This document |
                 +--------------+-------+---------------+
                 | Reserved     |  0x09 | This document |
                 +--------------+-------+---------------+
                 | MAX_PUSH_ID  |  0x0d | Section 7.2.7 |
                 +--------------+-------+---------------+

                   Table 2: Initial HTTP/3 Frame Types

   Each code of the format 0x1f * N + 0x21 for non-negative integer
   values of N (that is, 0x21, 0x40, ..., through 0x3ffffffffffffffe)
> **MUST NOT**: MUST NOT be assigned by IANA and MUST NOT appear in the listing of
   assigned values.

### 11.2.2  Settings Parameters

   This document establishes a registry for HTTP/3 settings.  The
   "HTTP/3 Settings" registry governs a 62-bit space.  This registry
   follows the QUIC registry policy; see Section 11.2.  Permanent
   registrations in this registry are assigned using the Specification
   Required policy ([RFC8126]), except for values between 0x00 and 0x3f
   (in hexadecimal; inclusive), which are assigned using Standards
   Action or IESG Approval as defined in Sections 4.9 and 4.10 of
   [RFC8126].

   While this registry is separate from the "HTTP/2 Settings" registry
   defined in [HTTP/2], it is preferable that the assignments parallel
   each other.  If an entry is present in only one registry, every
> **SHOULD**: effort SHOULD be made to avoid assigning the corresponding value to
   an unrelated operation.  Expert reviewers MAY reject unrelated
   registrations that would conflict with the same value in the
   corresponding registry.

   In addition to common fields as described in Section 11.2, permanent
> **MUST**: registrations in this registry MUST include the following fields:

   Setting Name:  A symbolic name for the setting.  Specifying a setting
      name is optional.

   Default:  The value of the setting unless otherwise indicated.  A
> **SHOULD**: default SHOULD be the most restrictive possible value.

   The entries in Table 3 are registered by this document.

     +========================+=======+=================+===========+
     | Setting Name           | Value | Specification   | Default   |
     +========================+=======+=================+===========+
     | Reserved               |  0x00 | This document   | N/A       |
     +------------------------+-------+-----------------+-----------+
     | Reserved               |  0x02 | This document   | N/A       |
     +------------------------+-------+-----------------+-----------+
     | Reserved               |  0x03 | This document   | N/A       |
     +------------------------+-------+-----------------+-----------+
     | Reserved               |  0x04 | This document   | N/A       |
     +------------------------+-------+-----------------+-----------+
     | Reserved               |  0x05 | This document   | N/A       |
     +------------------------+-------+-----------------+-----------+
     | MAX_FIELD_SECTION_SIZE |  0x06 | Section 7.2.4.1 | Unlimited |
     +------------------------+-------+-----------------+-----------+

                     Table 3: Initial HTTP/3 Settings

   For formatting reasons, setting names can be abbreviated by removing
   the 'SETTINGS_' prefix.

   Each code of the format 0x1f * N + 0x21 for non-negative integer
   values of N (that is, 0x21, 0x40, ..., through 0x3ffffffffffffffe)
> **MUST NOT**: MUST NOT be assigned by IANA and MUST NOT appear in the listing of
   assigned values.

### 11.2.3  Error Codes

   This document establishes a registry for HTTP/3 error codes.  The
   "HTTP/3 Error Codes" registry manages a 62-bit space.  This registry
   follows the QUIC registry policy; see Section 11.2.  Permanent
   registrations in this registry are assigned using the Specification
   Required policy ([RFC8126]), except for values between 0x00 and 0x3f
   (in hexadecimal; inclusive), which are assigned using Standards
   Action or IESG Approval as defined in Sections 4.9 and 4.10 of
   [RFC8126].

   Registrations for error codes are required to include a description
   of the error code.  An expert reviewer is advised to examine new
   registrations for possible duplication with existing error codes.
   Use of existing registrations is to be encouraged, but not mandated.
   Use of values that are registered in the "HTTP/2 Error Code" registry
> **MAY**: is discouraged, and expert reviewers MAY reject such registrations.

   In addition to common fields as described in Section 11.2, this
   registry includes two additional fields.  Permanent registrations in
> **MUST**: this registry MUST include the following field:

   Name:  A name for the error code.

   Description:  A brief description of the error code semantics.

   The entries in Table 4 are registered by this document.  These error
   codes were selected from the range that operates on a Specification
   Required policy to avoid collisions with HTTP/2 error codes.

   +===========================+========+==============+===============+
   | Name                      | Value  | Description  | Specification |
   +===========================+========+==============+===============+
   | H3_NO_ERROR               | 0x0100 | No error     | Section 8.1   |
   +---------------------------+--------+--------------+---------------+
   | H3_GENERAL_PROTOCOL_ERROR | 0x0101 | General      | Section 8.1   |
   |                           |        | protocol     |               |
   |                           |        | error        |               |
   +---------------------------+--------+--------------+---------------+
   | H3_INTERNAL_ERROR         | 0x0102 | Internal     | Section 8.1   |
   |                           |        | error        |               |
   +---------------------------+--------+--------------+---------------+
   | H3_STREAM_CREATION_ERROR  | 0x0103 | Stream       | Section 8.1   |
   |                           |        | creation     |               |
   |                           |        | error        |               |
   +---------------------------+--------+--------------+---------------+
   | H3_CLOSED_CRITICAL_STREAM | 0x0104 | Critical     | Section 8.1   |
   |                           |        | stream was   |               |
   |                           |        | closed       |               |
   +---------------------------+--------+--------------+---------------+
   | H3_FRAME_UNEXPECTED       | 0x0105 | Frame not    | Section 8.1   |
   |                           |        | permitted    |               |
   |                           |        | in the       |               |
   |                           |        | current      |               |
   |                           |        | state        |               |
   +---------------------------+--------+--------------+---------------+
   | H3_FRAME_ERROR            | 0x0106 | Frame        | Section 8.1   |
   |                           |        | violated     |               |
   |                           |        | layout or    |               |
   |                           |        | size rules   |               |
   +---------------------------+--------+--------------+---------------+
   | H3_EXCESSIVE_LOAD         | 0x0107 | Peer         | Section 8.1   |
   |                           |        | generating   |               |
   |                           |        | excessive    |               |
   |                           |        | load         |               |
   +---------------------------+--------+--------------+---------------+
   | H3_ID_ERROR               | 0x0108 | An           | Section 8.1   |
   |                           |        | identifier   |               |
   |                           |        | was used     |               |
   |                           |        | incorrectly  |               |
   +---------------------------+--------+--------------+---------------+
   | H3_SETTINGS_ERROR         | 0x0109 | SETTINGS     | Section 8.1   |
   |                           |        | frame        |               |
   |                           |        | contained    |               |
   |                           |        | invalid      |               |
   |                           |        | values       |               |
   +---------------------------+--------+--------------+---------------+
   | H3_MISSING_SETTINGS       | 0x010a | No SETTINGS  | Section 8.1   |
   |                           |        | frame        |               |
   |                           |        | received     |               |
   +---------------------------+--------+--------------+---------------+
   | H3_REQUEST_REJECTED       | 0x010b | Request not  | Section 8.1   |
   |                           |        | processed    |               |
   +---------------------------+--------+--------------+---------------+
   | H3_REQUEST_CANCELLED      | 0x010c | Data no      | Section 8.1   |
   |                           |        | longer       |               |
   |                           |        | needed       |               |
   +---------------------------+--------+--------------+---------------+
   | H3_REQUEST_INCOMPLETE     | 0x010d | Stream       | Section 8.1   |
   |                           |        | terminated   |               |
   |                           |        | early        |               |
   +---------------------------+--------+--------------+---------------+
   | H3_MESSAGE_ERROR          | 0x010e | Malformed    | Section 8.1   |
   |                           |        | message      |               |
   +---------------------------+--------+--------------+---------------+
   | H3_CONNECT_ERROR          | 0x010f | TCP reset    | Section 8.1   |
   |                           |        | or error on  |               |
   |                           |        | CONNECT      |               |
   |                           |        | request      |               |
   +---------------------------+--------+--------------+---------------+
   | H3_VERSION_FALLBACK       | 0x0110 | Retry over   | Section 8.1   |
   |                           |        | HTTP/1.1     |               |
   +---------------------------+--------+--------------+---------------+

                    Table 4: Initial HTTP/3 Error Codes

   Each code of the format 0x1f * N + 0x21 for non-negative integer
   values of N (that is, 0x21, 0x40, ..., through 0x3ffffffffffffffe)
> **MUST NOT**: MUST NOT be assigned by IANA and MUST NOT appear in the listing of
   assigned values.

### 11.2.4  Stream Types

   This document establishes a registry for HTTP/3 unidirectional stream
   types.  The "HTTP/3 Stream Types" registry governs a 62-bit space.
   This registry follows the QUIC registry policy; see Section 11.2.
   Permanent registrations in this registry are assigned using the
   Specification Required policy ([RFC8126]), except for values between
   0x00 and 0x3f (in hexadecimal; inclusive), which are assigned using
   Standards Action or IESG Approval as defined in Sections 4.9 and 4.10
   of [RFC8126].

   In addition to common fields as described in Section 11.2, permanent
> **MUST**: registrations in this registry MUST include the following fields:

   Stream Type:  A name or label for the stream type.

   Sender:  Which endpoint on an HTTP/3 connection may initiate a stream
      of this type.  Values are "Client", "Server", or "Both".

> **MUST**: Specifications for permanent registrations MUST include a description
   of the stream type, including the layout and semantics of the stream
   contents.

   The entries in Table 5 are registered by this document.

            +================+=======+===============+========+
            | Stream Type    | Value | Specification | Sender |
            +================+=======+===============+========+
            | Control Stream |  0x00 | Section 6.2.1 | Both   |
            +----------------+-------+---------------+--------+
            | Push Stream    |  0x01 | Section 4.6   | Server |
            +----------------+-------+---------------+--------+

                       Table 5: Initial Stream Types

   Each code of the format 0x1f * N + 0x21 for non-negative integer
   values of N (that is, 0x21, 0x40, ..., through 0x3ffffffffffffffe)
> **MUST NOT**: MUST NOT be assigned by IANA and MUST NOT appear in the listing of
   assigned values.

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
