---
title: "4.3.  HTTP Control Data"
rfc_number: 9114
rfc_section: "4.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9114"
description: "Section 4.3: HTTP Control Data — RFC 9114 — HTTP/3"
tags: [RFC9114, HTTP/3, QUIC, variable-length-frames, unidirectional-streams, QPACK, SETTINGS, GOAWAY, server-push, http_control_data]
---

## 4.3.  HTTP Control Data

## 4.3  HTTP Control Data

   Like HTTP/2, HTTP/3 employs a series of pseudo-header fields, where
   the field name begins with the : character (ASCII 0x3a).  These
   pseudo-header fields convey message control data; see Section 6.2 of
   [HTTP].

> **MUST NOT**: Pseudo-header fields are not HTTP fields.  Endpoints MUST NOT
   generate pseudo-header fields other than those defined in this
   document.  However, an extension could negotiate a modification of
   this restriction; see Section 9.

   Pseudo-header fields are only valid in the context in which they are
> **MUST NOT**: defined.  Pseudo-header fields defined for requests MUST NOT appear
   in responses; pseudo-header fields defined for responses MUST NOT
> **MUST NOT**: appear in requests.  Pseudo-header fields MUST NOT appear in trailer
   sections.  Endpoints MUST treat a request or response that contains
   undefined or invalid pseudo-header fields as malformed.

> **MUST**: All pseudo-header fields MUST appear in the header section before
   regular header fields.  Any request or response that contains a
   pseudo-header field that appears in a header section after a regular
> **MUST**: header field MUST be treated as malformed.

### 4.3.1  Request Pseudo-Header Fields

   The following pseudo-header fields are defined for requests:

   ":method":  Contains the HTTP method (Section 9 of [HTTP])

   ":scheme":  Contains the scheme portion of the target URI
      (Section 3.1 of [URI]).

      The :scheme pseudo-header is not restricted to URIs with scheme
      "http" and "https".  A proxy or gateway can translate requests for
      non-HTTP schemes, enabling the use of HTTP to interact with non-
      HTTP services.

      See Section 3.1.2 for guidance on using a scheme other than
      "https".

   ":authority":  Contains the authority portion of the target URI
> **MUST NOT**: (Section 3.2 of [URI]).  The authority MUST NOT include the
      deprecated userinfo subcomponent for URIs of scheme "http" or
      "https".

      To ensure that the HTTP/1.1 request line can be reproduced
> **MUST**: accurately, this pseudo-header field MUST be omitted when
      translating from an HTTP/1.1 request that has a request target in
      a method-specific form; see Section 7.1 of [HTTP].  Clients that
> **SHOULD**: generate HTTP/3 requests directly SHOULD use the :authority
      pseudo-header field instead of the Host header field.  An
> **MUST**: intermediary that converts an HTTP/3 request to HTTP/1.1 MUST
      create a Host field if one is not present in a request by copying
      the value of the :authority pseudo-header field.

   ":path":  Contains the path and query parts of the target URI (the
      "path-absolute" production and optionally a ? character (ASCII
      0x3f) followed by the "query" production; see Sections 3.3 and 3.4
      of [URI].

> **MUST NOT**: This pseudo-header field MUST NOT be empty for "http" or "https"
      URIs; "http" or "https" URIs that do not contain a path component
> **MUST**: MUST include a value of / (ASCII 0x2f).  An OPTIONS request that
      does not include a path component includes the value * (ASCII
      0x2a) for the :path pseudo-header field; see Section 7.1 of
      [HTTP].

> **MUST**: All HTTP/3 requests MUST include exactly one value for the :method,
   :scheme, and :path pseudo-header fields, unless the request is a
   CONNECT request; see Section 4.4.

   If the :scheme pseudo-header field identifies a scheme that has a
   mandatory authority component (including "http" and "https"), the
> **MUST**: request MUST contain either an :authority pseudo-header field or a
   Host header field.  If these fields are present, they MUST NOT be
> **MUST**: empty.  If both fields are present, they MUST contain the same value.
   If the scheme does not have a mandatory authority component and none
> **MUST NOT**: is provided in the request target, the request MUST NOT contain the
   :authority pseudo-header or Host header fields.

   An HTTP request that omits mandatory pseudo-header fields or contains
   invalid values for those pseudo-header fields is malformed.

   HTTP/3 does not define a way to carry the version identifier that is
   included in the HTTP/1.1 request line.  HTTP/3 requests implicitly
   have a protocol version of "3.0".

### 4.3.2  Response Pseudo-Header Fields

   For responses, a single ":status" pseudo-header field is defined that
   carries the HTTP status code; see Section 15 of [HTTP].  This pseudo-
> **MUST**: header field MUST be included in all responses; otherwise, the
   response is malformed (see Section 4.1.2).

   HTTP/3 does not define a way to carry the version or reason phrase
   that is included in an HTTP/1.1 status line.  HTTP/3 responses
   implicitly have a protocol version of "3.0".

---

**Navigation:** [[../RFC9114|RFC9114 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
