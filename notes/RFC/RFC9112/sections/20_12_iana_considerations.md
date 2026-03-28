---
title: "12.  IANA Considerations"
rfc_number: 9112
rfc_section: "12"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 12: IANA Considerations — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, iana_considerations]
---

## 12.  IANA Considerations

12.  IANA Considerations

   The change controller for the following registrations is: "IETF
   (iesg@ietf.org) - Internet Engineering Task Force".

## 12.1  Field Name Registration

   IANA has added the following field names to the "Hypertext Transfer
   Protocol (HTTP) Field Name Registry" at
   <https://www.iana.org/assignments/http-fields>, as described in
   Section 18.4 of [HTTP].

   +===================+===========+=========+============+
   | Field Name        | Status    | Section | Comments   |
   +===================+===========+=========+============+
   | Close             | permanent | 9.6     | (reserved) |
   +-------------------+-----------+---------+------------+
   | MIME-Version      | permanent | B.1     |            |
   +-------------------+-----------+---------+------------+
   | Transfer-Encoding | permanent | 6.1     |            |
   +-------------------+-----------+---------+------------+

                           Table 1

## 12.2  Media Type Registration

   IANA has updated the "Media Types" registry at
   <https://www.iana.org/assignments/media-types> with the registration
   information in Sections 10.1 and 10.2 for the media types "message/
   http" and "application/http", respectively.

## 12.3  Transfer Coding Registration

   IANA has updated the "HTTP Transfer Coding Registry" at
   <https://www.iana.org/assignments/http-parameters/> with the
   registration procedure of Section 7.3 and the content coding names
   summarized in the table below.

   +============+===========================================+=========+
   | Name       | Description                               | Section |
   +============+===========================================+=========+
   | chunked    | Transfer in a series of chunks            | 7.1     |
   +------------+-------------------------------------------+---------+
   | compress   | UNIX "compress" data format [Welch]       | 7.2     |
   +------------+-------------------------------------------+---------+
   | deflate    | "deflate" compressed data ([RFC1951])     | 7.2     |
   |            | inside the "zlib" data format ([RFC1950]) |         |
   +------------+-------------------------------------------+---------+
   | gzip       | GZIP file format [RFC1952]                | 7.2     |
   +------------+-------------------------------------------+---------+
   | trailers   | (reserved)                                | 12.3    |
   +------------+-------------------------------------------+---------+
   | x-compress | Deprecated (alias for compress)           | 7.2     |
   +------------+-------------------------------------------+---------+
   | x-gzip     | Deprecated (alias for gzip)               | 7.2     |
   +------------+-------------------------------------------+---------+

                                 Table 2

      |  *Note:* the coding name "trailers" is reserved because its use
      |  would conflict with the keyword "trailers" in the TE header
      |  field (Section 10.1.4 of [HTTP]).

## 12.4  ALPN Protocol ID Registration

   IANA has updated the "TLS Application-Layer Protocol Negotiation
   (ALPN) Protocol IDs" registry at <https://www.iana.org/assignments/
   tls-extensiontype-values/> with the registration below:

          +==========+=============================+===========+
          | Protocol | Identification Sequence     | Reference |
          +==========+=============================+===========+
          | HTTP/1.1 | 0x68 0x74 0x74 0x70 0x2f    | RFC 9112  |
          |          | 0x31 0x2e 0x31 ("http/1.1") |           |
          +----------+-----------------------------+-----------+

                                 Table 3

---

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
