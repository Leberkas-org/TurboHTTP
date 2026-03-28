---
title: "4.2.  HTTP Fields"
rfc_number: 9114
rfc_section: "4.2"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 4.2: HTTP Fields — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, http_fields]
---

## 4.2.  HTTP Fields

## 4.2  HTTP Fields

   HTTP messages carry metadata as a series of key-value pairs called
   "HTTP fields"; see Sections 6.3 and 6.5 of [HTTP].  For a listing of
   registered HTTP fields, see the "Hypertext Transfer Protocol (HTTP)
   Field Name Registry" maintained at <https://www.iana.org/assignments/
   http-fields/>.  Like HTTP/2, HTTP/3 has additional considerations
   related to the use of characters in field names, the Connection
   header field, and pseudo-header fields.

   Field names are strings containing a subset of ASCII characters.
   Properties of HTTP field names and values are discussed in more
> **MUST**: detail in Section 5.1 of [HTTP].  Characters in field names MUST be
   converted to lowercase prior to their encoding.  A request or
> **MUST**: response containing uppercase characters in field names MUST be
   treated as malformed.

   HTTP/3 does not use the Connection header field to indicate
   connection-specific fields; in this protocol, connection-specific
> **MUST NOT**: metadata is conveyed by other means.  An endpoint MUST NOT generate
   an HTTP/3 field section containing connection-specific fields; any
> **MUST**: message containing connection-specific fields MUST be treated as
   malformed.

> **MAY**: The only exception to this is the TE header field, which MAY be
   present in an HTTP/3 request header; when it is, it MUST NOT contain
   any value other than "trailers".

> **MUST**: An intermediary transforming an HTTP/1.x message to HTTP/3 MUST
   remove connection-specific header fields as discussed in
   Section 7.6.1 of [HTTP], or their messages will be treated by other
   HTTP/3 endpoints as malformed.

### 4.2.1  Field Compression

   [QPACK] describes a variation of HPACK that gives an encoder some
   control over how much head-of-line blocking can be caused by
   compression.  This allows an encoder to balance compression
   efficiency with latency.  HTTP/3 uses QPACK to compress header and
   trailer sections, including the control data present in the header
   section.

   To allow for better compression efficiency, the Cookie header field
> **MAY**: ([COOKIES]) MAY be split into separate field lines, each with one or
   more cookie-pairs, before compression.  If a decompressed field
> **MUST**: section contains multiple cookie field lines, these MUST be
   concatenated into a single byte string using the two-byte delimiter
   of "; " (ASCII 0x3b, 0x20) before being passed into a context other
   than HTTP/2 or HTTP/3, such as an HTTP/1.1 connection, or a generic
   HTTP server application.

### 4.2.2  Header Size Constraints

> **MAY**: An HTTP/3 implementation MAY impose a limit on the maximum size of
   the message header it will accept on an individual HTTP message.  A
   server that receives a larger header section than it is willing to
   handle can send an HTTP 431 (Request Header Fields Too Large) status
   code ([RFC6585]).  A client can discard responses that it cannot
   process.  The size of a field list is calculated based on the
   uncompressed size of fields, including the length of the name and
   value in bytes plus an overhead of 32 bytes for each field.

   If an implementation wishes to advise its peer of this limit, it can
   be conveyed as a number of bytes in the
   SETTINGS_MAX_FIELD_SECTION_SIZE parameter.  An implementation that
> **SHOULD NOT**: has received this parameter SHOULD NOT send an HTTP message header
   that exceeds the indicated size, as the peer will likely refuse to
   process it.  However, an HTTP message can traverse one or more
   intermediaries before reaching the origin server; see Section 3.7 of
   [HTTP].  Because this limit is applied separately by each
   implementation that processes the message, messages below this limit
   are not guaranteed to be accepted.

---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes

- **`QpackEncoder.cs`** — QPACK field compression with static and dynamic table support; lowercases field names per §4.2; splits Cookie headers per §4.2.1
- **`QpackDecoder.cs`** — QPACK decompression with Cookie concatenation using `"; "` delimiter per §4.2.1; validates field name characters
- **`Http3HeaderValidator.cs`** — Rejects connection-specific headers (Connection, Keep-Alive, Transfer-Encoding, Upgrade) per §4.2; allows `TE: trailers` as sole exception
- **`Http3Settings.cs`** — Supports `SETTINGS_MAX_FIELD_SECTION_SIZE` (0x06) for header size constraint advertisement per §4.2.2

### Test References

- `TurboHttp.Tests/RFC9114/12_Http3QpackTests.cs` — QPACK encoding/decoding round-trips, static table lookups
- `TurboHttp.Tests/RFC9114/13_Http3HeaderValidationTests.cs` — Connection-specific header rejection, uppercase field name detection, TE header validation
- `TurboHttp.Tests/RFC9114/14_Http3CookieTests.cs` — Cookie splitting and concatenation per §4.2.1

### Known Gaps

- ⚠️ QPACK dynamic table size is limited — encoder uses conservative settings to minimize head-of-line blocking at cost of compression ratio
- ⚠️ `SETTINGS_MAX_FIELD_SECTION_SIZE` is advertised but enforcement on received headers is approximate (checks uncompressed size estimate)

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
