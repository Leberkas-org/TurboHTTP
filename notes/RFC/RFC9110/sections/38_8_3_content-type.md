---
title: "8.3.  Content-Type"
rfc_number: 9110
rfc_section: "8.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9110"
description: "Section 8.3: Content-Type — RFC 9110 — HTTP Semantics"
tags: [RFC9110, HTTP-semantics, methods, status-codes, redirects, retries, content-negotiation, conditional-requests, content-type]
---

## 8.3.  Content-Type

## 8.3  Content-Type

   The "Content-Type" header field indicates the media type of the
   associated representation: either the representation enclosed in the
   message content or the selected representation, as determined by the
   message semantics.  The indicated media type defines both the data
   format and how that data is intended to be processed by a recipient,
   within the scope of the received message semantics, after any content
   codings indicated by Content-Encoding are decoded.


```abnf
     Content-Type = media-type
```


   Media types are defined in Section 8.3.1.  An example of the field is

   Content-Type: text/html; charset=ISO-8859-4

> **SHOULD**: A sender that generates a message containing content SHOULD generate
   a Content-Type header field in that message unless the intended media
   type of the enclosed representation is unknown to the sender.  If a
> **MAY**: Content-Type header field is not present, the recipient MAY either
   assume a media type of "application/octet-stream" ([RFC2046],
   Section 4.5.1) or examine the data to determine its type.

   In practice, resource owners do not always properly configure their
   origin server to provide the correct Content-Type for a given
   representation.  Some user agents examine the content and, in certain
   cases, override the received type (for example, see [Sniffing]).
   This "MIME sniffing" risks drawing incorrect conclusions about the
   data, which might expose the user to additional security risks (e.g.,
   "privilege escalation").  Furthermore, distinct media types often
   share a common data format, differing only in how the data is
   intended to be processed, which is impossible to distinguish by
   inspecting the data alone.  When sniffing is implemented,
   implementers are encouraged to provide a means for the user to
   disable it.

   Although Content-Type is defined as a singleton field, it is
   sometimes incorrectly generated multiple times, resulting in a
   combined field value that appears to be a list.  Recipients often
   attempt to handle this error by using the last syntactically valid
   member of the list, leading to potential interoperability and
   security issues if different implementations have different error
   handling behaviors.

### 8.3.1  Media Type

   HTTP uses media types [RFC2046] in the Content-Type (Section 8.3) and
   Accept (Section 12.5.1) header fields in order to provide open and
   extensible data typing and type negotiation.  Media types define both
   a data format and various processing models: how to process that data
   in accordance with the message context.


```abnf
     media-type = type "/" subtype parameters
     type       = token
     subtype    = token
```


   The type and subtype tokens are case-insensitive.

> **MAY**: The type/subtype MAY be followed by semicolon-delimited parameters
   (Section 5.6.6) in the form of name/value pairs.  The presence or
   absence of a parameter might be significant to the processing of a
   media type, depending on its definition within the media type
   registry.  Parameter values might or might not be case-sensitive,
   depending on the semantics of the parameter name.

   For example, the following media types are equivalent in describing
   HTML text data encoded in the UTF-8 character encoding scheme, but
   the first is preferred for consistency (the "charset" parameter value
   is defined as being case-insensitive in [RFC2046], Section 4.1.2):

     text/html;charset=utf-8
     Text/HTML;Charset="utf-8"
     text/html; charset="utf-8"
     text/html;charset=UTF-8

   Media types ought to be registered with IANA according to the
   procedures defined in [BCP13].

### 8.3.2  Charset

   HTTP uses "charset" names to indicate or negotiate the character
   encoding scheme ([RFC6365], Section 2) of a textual representation.
   In the fields defined by this document, charset names appear either
   in parameters (Content-Type), or, for Accept-Encoding, in the form of
   a plain token.  In both cases, charset names are matched case-
   insensitively.

   Charset names ought to be registered in the IANA "Character Sets"
   registry (<https://www.iana.org/assignments/character-sets>)
   according to the procedures defined in Section 2 of [RFC2978].

      |  *Note:* In theory, charset names are defined by the "mime-
      |  charset" ABNF rule defined in Section 2.3 of [RFC2978] (as
      |  corrected in [Err1912]).  That rule allows two characters that
      |  are not included in "token" ("{" and "}"), but no charset name
      |  registered at the time of this writing includes braces (see
      |  [Err5433]).

### 8.3.3  Multipart Types

   MIME provides for a number of "multipart" types -- encapsulations of
   one or more representations within a single message body.  All
   multipart types share a common syntax, as defined in Section 5.1.1 of
   [RFC2046], and include a boundary parameter as part of the media type
> **MUST**: value.  The message body is itself a protocol element; a sender MUST
   generate only CRLF to represent line breaks between body parts.

   HTTP message framing does not use the multipart boundary as an
   indicator of message body length, though it might be used by
   implementations that generate or process the content.  For example,
   the "multipart/form-data" type is often used for carrying form data
   in a request, as described in [RFC7578], and the "multipart/
   byteranges" type is defined by this specification for use in some 206
   (Partial Content) responses (see Section 15.3.7).

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
