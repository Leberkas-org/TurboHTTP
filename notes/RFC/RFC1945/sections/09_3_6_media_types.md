---
title: "3.6.  Media Types"
rfc_number: 1945
rfc_section: "3.6"
source_url: "https://www.rfc-editor.org/rfc/rfc1945"
description: "Section 3.6: Media Types — RFC 1945 — HTTP/1.0"
tags: [RFC1945, HTTP/1.0, message-syntax, request-response, entity-body, content-length, status-codes, simple-request, media_types]
---

# 3.6.  Media Types

## 3.6  Media Types

   HTTP uses Internet Media Types [13] in the Content-Type header field
   (Section 10.5) in order to provide open and extensible data typing.


```abnf
       media-type     = type "/" subtype *( ";" parameter )
       type           = token
       subtype        = token
```


   Parameters may follow the type/subtype in the form of attribute/value
   pairs.


```abnf
       parameter      = attribute "=" value
       attribute      = token
       value          = token | quoted-string
```


   The type, subtype, and parameter attribute names are case-
   insensitive. Parameter values may or may not be case-sensitive,
   depending on the semantics of the parameter name. LWS must not be
   generated between the type and subtype, nor between an attribute and
   its value. Upon receipt of a media type with an unrecognized
   parameter, a user agent should treat the media type as if the
   unrecognized parameter and its value were not present.

   Some older HTTP applications do not recognize media type parameters.
   HTTP/1.0 applications should only use media type parameters when they
   are necessary to define the content of a message.

   Media-type values are registered with the Internet Assigned Number
   Authority (IANA [15]). The media type registration process is
   outlined in RFC 1590 [13]. Use of non-registered media types is
   discouraged.

## 3.6.1  Canonicalization and Text Defaults

   Internet media types are registered with a canonical form. In
   general, an Entity-Body transferred via HTTP must be represented in
   the appropriate canonical form prior to its transmission. If the body
   has been encoded with a Content-Encoding, the underlying data should
   be in canonical form prior to being encoded.

   Media subtypes of the "text" type use CRLF as the text line break
   when in canonical form. However, HTTP allows the transport of text
   media with plain CR or LF alone representing a line break when used
   consistently within the Entity-Body. HTTP applications must accept
   CRLF, bare CR, and bare LF as being representative of a line break in
   text media received via HTTP.




   In addition, if the text media is represented in a character set that
   does not use octets 13 and 10 for CR and LF respectively, as is the
   case for some multi-byte character sets, HTTP allows the use of
   whatever octet sequences are defined by that character set to
   represent the equivalent of CR and LF for line breaks. This
   flexibility regarding line breaks applies only to text media in the
   Entity-Body; a bare CR or LF should not be substituted for CRLF
   within any of the HTTP control structures (such as header fields and
   multipart boundaries).

   The "charset" parameter is used with some media types to define the
   character set (Section 3.4) of the data. When no explicit charset
   parameter is provided by the sender, media subtypes of the "text"
   type are defined to have a default charset value of "ISO-8859-1" when
   received via HTTP. Data in character sets other than "ISO-8859-1" or
   its subsets must be labelled with an appropriate charset value in
   order to be consistently interpreted by the recipient.

      Note: Many current HTTP servers provide data using charsets other
      than "ISO-8859-1" without proper labelling. This situation reduces
      interoperability and is not recommended. To compensate for this,
      some HTTP user agents provide a configuration option to allow the
      user to change the default interpretation of the media type
      character set when no charset parameter is given.

## 3.6.2  Multipart Types

   MIME provides for a number of "multipart" types -- encapsulations of
   several entities within a single message's Entity-Body. The multipart
   types registered by IANA [15] do not have any special meaning for
   HTTP/1.0, though user agents may need to understand each type in
   order to correctly interpret the purpose of each body-part. An HTTP
   user agent should follow the same or similar behavior as a MIME user
   agent does upon receipt of a multipart type. HTTP servers should not
   assume that all HTTP clients are prepared to handle multipart types.

   All multipart types share a common syntax and must include a boundary
   parameter as part of the media type value. The message body is itself
   a protocol element and must therefore use only CRLF to represent line
   breaks between body-parts. Multipart body-parts may contain HTTP
   header fields which are significant to the meaning of that part.

---

**Navigation:** [[../RFC1945|RFC1945 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
