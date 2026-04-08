---
title: "8.2.  HTTP Fields"
rfc_number: 9113
rfc_section: "8.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.2: HTTP Fields — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, http_fields]
---

## 8.2.  HTTP Fields

## 8.2  HTTP Fields

   HTTP fields (Section 5 of [HTTP]) are conveyed by HTTP/2 in the
   HEADERS, CONTINUATION, and PUSH_PROMISE frames, compressed with HPACK
   [COMPRESSION].

> **MUST**: Field names MUST be converted to lowercase when constructing an
   HTTP/2 message.

### 8.2.1  Field Validity

   The definitions of field names and values in HTTP prohibit some
   characters that HPACK might be able to convey.  HTTP/2
> **SHOULD**: implementations SHOULD validate field names and values according to
   their definitions in Sections 5.1 and 5.5 of [HTTP], respectively,
   and treat messages that contain prohibited characters as malformed
   (Section 8.1.1).

   Failure to validate fields can be exploited for request smuggling
   attacks.  In particular, unvalidated fields might enable attacks when
   messages are forwarded using HTTP/1.1 [HTTP/1.1], where characters
   such as carriage return (CR), line feed (LF), and COLON are used as
> **MUST**: delimiters.  Implementations MUST perform the following minimal
   validation of field names and values:

> **MUST NOT**: *  A field name MUST NOT contain characters in the ranges 0x00-0x20,
      0x41-0x5a, or 0x7f-0xff (all ranges inclusive).  This specifically
      excludes all non-visible ASCII characters, ASCII SP (0x20), and
      uppercase characters ('A' to 'Z', ASCII 0x41 to 0x5a).

   *  With the exception of pseudo-header fields (Section 8.3), which
> **MUST NOT**: have a name that starts with a single colon, field names MUST NOT
      include a colon (ASCII COLON, 0x3a).

> **MUST NOT**: *  A field value MUST NOT contain the zero value (ASCII NUL, 0x00),
      line feed (ASCII LF, 0x0a), or carriage return (ASCII CR, 0x0d) at
      any position.

> **MUST NOT**: *  A field value MUST NOT start or end with an ASCII whitespace
      character (ASCII SP or HTAB, 0x20 or 0x09).

      |  Note: An implementation that validates fields according to the
      |  definitions in Sections 5.1 and 5.5 of [HTTP] only needs an
      |  additional check that field names do not include uppercase
      |  characters.

   A request or response that contains a field that violates any of
> **MUST**: these conditions MUST be treated as malformed (Section 8.1.1).  In
   particular, an intermediary that does not process fields when
> **MUST NOT**: forwarding messages MUST NOT forward fields that contain any of the
   values that are listed as prohibited above.

   When a request message violates one of these requirements, an
> **SHOULD**: implementation SHOULD generate a 400 (Bad Request) status code (see
   Section 15.5.1 of [HTTP]), unless a more suitable status code is
   defined or the status code cannot be sent (e.g., because the error
   occurs in a trailer field).

      |  Note: Field values that are not valid according to the
      |  definition of the corresponding field do not cause a request to
      |  be malformed; the requirements above only apply to the generic
      |  syntax for fields as defined in Section 5 of [HTTP].

### 8.2.2  Connection-Specific Header Fields

   HTTP/2 does not use the Connection header field (Section 7.6.1 of
   [HTTP]) to indicate connection-specific header fields; in this
   protocol, connection-specific metadata is conveyed by other means.
> **MUST NOT**: An endpoint MUST NOT generate an HTTP/2 message containing
   connection-specific header fields.  This includes the Connection
   header field and those listed as having connection-specific semantics
   in Section 7.6.1 of [HTTP] (that is, Proxy-Connection, Keep-Alive,
   Transfer-Encoding, and Upgrade).  Any message containing connection-
> **MUST**: specific header fields MUST be treated as malformed (Section 8.1.1).

> **MAY**: The only exception to this is the TE header field, which MAY be
   present in an HTTP/2 request; when it is, it MUST NOT contain any
   value other than "trailers".

> **MUST**: An intermediary transforming an HTTP/1.x message to HTTP/2 MUST
   remove connection-specific header fields as discussed in
   Section 7.6.1 of [HTTP], or their messages will be treated by other
   HTTP/2 endpoints as malformed (Section 8.1.1).

      |  Note: HTTP/2 purposefully does not support upgrade to another
      |  protocol.  The handshake methods described in Section 3 are
      |  believed sufficient to negotiate the use of alternative
      |  protocols.

### 8.2.3  Compressing the Cookie Header Field

   The Cookie header field [COOKIE] uses a semicolon (";") to delimit
   cookie-pairs (or "crumbs").  This header field contains multiple
   values, but does not use a COMMA (",") as a separator, thereby
   preventing cookie-pairs from being sent on multiple field lines (see
   Section 5.2 of [HTTP]).  This can significantly reduce compression
   efficiency, as updates to individual cookie-pairs would invalidate
   any field lines that are stored in the HPACK table.

   To allow for better compression efficiency, the Cookie header field
> **MAY**: MAY be split into separate header fields, each with one or more
   cookie-pairs.  If there are multiple Cookie header fields after
> **MUST**: decompression, these MUST be concatenated into a single octet string
   using the two-octet delimiter of 0x3b, 0x20 (the ASCII string "; ")
   before being passed into a non-HTTP/2 context, such as an HTTP/1.1
   connection, or a generic HTTP server application.

   Therefore, the following two lists of Cookie header fields are
   semantically equivalent.

   cookie: a=b; c=d; e=f

   cookie: a=b
   cookie: c=d
   cookie: e=f

---

## TurboHTTP Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`HpackEncoder.cs`** — Converts field names to lowercase per §8.2; applies Cookie splitting for compression efficiency per §8.2.3
- **`HpackDecoder.cs`** — Validates field name/value character ranges per §8.2.1; rejects prohibited characters (NUL, CR, LF in values; uppercase/non-visible in names)
- **`Http2FrameDecoder.cs`** — Strips connection-specific headers per §8.2.2 (Connection, Keep-Alive, Transfer-Encoding, Upgrade, Proxy-Connection)
- **`Http2RequestEncoder.cs`** — Ensures TE header only contains "trailers" value when present

### Test References
- `TurboHTTP.Tests/RFC9113/23_Http2FieldTests.cs` — Field validation, connection-specific header rejection, Cookie compression

### Known Gaps
- ⚠️ Cookie reconstitution — Split Cookie headers are concatenated on decode but edge cases with malformed cookie-pairs may not be fully covered

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
