---
title: "14.6.  Media Type multipart/byteranges"
rfc_number: 9110
rfc_section: "14.6"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 14.6: Media Type multipart/byteranges — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, media_type_multipartbyteranges]
---

## 14.6.  Media Type multipart/byteranges

## 14.6  Media Type multipart/byteranges

   When a 206 (Partial Content) response message includes the content of
   multiple ranges, they are transmitted as body parts in a multipart
   message body ([RFC2046], Section 5.1) with the media type of
   "multipart/byteranges".

   The "multipart/byteranges" media type includes one or more body
   parts, each with its own Content-Type and Content-Range fields.  The
   required boundary parameter specifies the boundary string used to
   separate each body part.

   Implementation Notes:

   1.  Additional CRLFs might precede the first boundary string in the
       body.

   2.  Although [RFC2046] permits the boundary string to be quoted, some
       existing implementations handle a quoted boundary string
       incorrectly.

   3.  A number of clients and servers were coded to an early draft of
       the byteranges specification that used a media type of
       "multipart/x-byteranges", which is almost (but not quite)
       compatible with this type.

   Despite the name, the "multipart/byteranges" media type is not
   limited to byte ranges.  The following example uses an "exampleunit"
   range unit:

   HTTP/1.1 206 Partial Content
   Date: Tue, 14 Nov 1995 06:25:24 GMT
   Last-Modified: Tue, 14 July 04:58:08 GMT
   Content-Length: 2331785
   Content-Type: multipart/byteranges; boundary=THIS_STRING_SEPARATES

   --THIS_STRING_SEPARATES
   Content-Type: video/example
   Content-Range: exampleunit 1.2-4.3/25

   ...the first range...
   --THIS_STRING_SEPARATES
   Content-Type: video/example
   Content-Range: exampleunit 11.2-14.3/25

   ...the second range
   --THIS_STRING_SEPARATES--

   The following information serves as the registration form for the
   "multipart/byteranges" media type.

   Type name:  multipart

   Subtype name:  byteranges

   Required parameters:  boundary

   Optional parameters:  N/A

   Encoding considerations:  only "7bit", "8bit", or "binary" are
      permitted

   Security considerations:  see Section 17

   Interoperability considerations:  N/A

   Published specification:  RFC 9110 (see Section 14.6)

   Applications that use this media type:  HTTP components supporting
      multiple ranges in a single request

   Fragment identifier considerations:  N/A

   Additional information:  Deprecated alias names for this type:  N/A

                            Magic number(s):  N/A

                            File extension(s):  N/A

                            Macintosh file type code(s):  N/A

   Person and email address to contact for further information:  See Aut
      hors' Addresses section.

   Intended usage:  COMMON

   Restrictions on usage:  N/A

   Author:  See Authors' Addresses section.

   Change controller:  IESG

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
