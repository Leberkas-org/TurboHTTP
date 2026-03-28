---
title: "10.  Enclosing Messages as Data"
rfc_number: 9112
rfc_section: "10"
source_url: "https://www.rfc-editor.org/rfc/rfc9112"
description: "Section 10: Enclosing Messages as Data — RFC 9112 — HTTP/1.1"
tags: [RFC9112, HTTP/1.1, message-framing, chunked-encoding, connection-management, keep-alive, Host-header, pipelining, enclosing_messages_as_data]
---

## 10.  Enclosing Messages as Data

10.  Enclosing Messages as Data

## 10.1  Media Type message/http

   The "message/http" media type can be used to enclose a single HTTP
   request or response message, provided that it obeys the MIME
   restrictions for all "message" types regarding line length and
   encodings.  Because of the line length limitations, field values
   within "message/http" are allowed to use line folding (obs-fold), as
   described in Section 5.2, to convey the field value over multiple
> **MUST**: lines.  A recipient of "message/http" data MUST replace any obsolete
   line folding with one or more SP characters when the message is
   consumed.

   Type name:  message

   Subtype name:  http

   Required parameters:  N/A

   Optional parameters:  version, msgtype

      version:  The HTTP-version number of the enclosed message (e.g.,
         "1.1").  If not present, the version can be determined from the
         first line of the body.

      msgtype:  The message type -- "request" or "response".  If not
         present, the type can be determined from the first line of the
         body.

   Encoding considerations:  only "7bit", "8bit", or "binary" are
      permitted

   Security considerations:  see Section 11

   Interoperability considerations:  N/A

   Published specification:  RFC 9112 (see Section 10.1).

   Applications that use this media type:  N/A

   Fragment identifier considerations:  N/A

   Additional information:  Magic number(s):  N/A

                            Deprecated alias names for this type:  N/A

                            File extension(s):  N/A

                            Macintosh file type code(s):  N/A

   Person and email address to contact for further information:  See Aut
      hors' Addresses section.

   Intended usage:  COMMON

   Restrictions on usage:  N/A

   Author:  See Authors' Addresses section.

   Change controller:  IESG

## 10.2  Media Type application/http

   The "application/http" media type can be used to enclose a pipeline
   of one or more HTTP request or response messages (not intermixed).

   Type name:  application

   Subtype name:  http

   Required parameters:  N/A

   Optional parameters:  version, msgtype

      version:  The HTTP-version number of the enclosed messages (e.g.,
         "1.1").  If not present, the version can be determined from the
         first line of the body.

      msgtype:  The message type -- "request" or "response".  If not
         present, the type can be determined from the first line of the
         body.

   Encoding considerations:  HTTP messages enclosed by this type are in
      "binary" format; use of an appropriate Content-Transfer-Encoding
      is required when transmitted via email.

   Security considerations:  see Section 11

   Interoperability considerations:  N/A

   Published specification:  RFC 9112 (see Section 10.2).

   Applications that use this media type:  N/A

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

**Navigation:** [[../RFC9112|RFC9112 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
