---
title: "8.3.  HTTP Control Data"
rfc_number: 9113
rfc_section: "8.3"
source_url: "https://www.rfc-editor.org/rfc/rfc9113"
description: "Section 8.3: HTTP Control Data — RFC 9113 — HTTP/2"
tags: [RFC9113, HTTP/2, binary-framing, streams, multiplexing, flow-control, SETTINGS, HPACK, GOAWAY, WINDOW_UPDATE, http_control_data]
---

## 8.3.  HTTP Control Data

## 8.3  HTTP Control Data

   HTTP/2 uses special pseudo-header fields beginning with a ':'
   character (ASCII 0x3a) to convey message control data (see
   Section 6.2 of [HTTP]).

> **MUST NOT**: Pseudo-header fields are not HTTP header fields.  Endpoints MUST NOT
   generate pseudo-header fields other than those defined in this
   document.  Note that an extension could negotiate the use of
   additional pseudo-header fields; see Section 5.5.

   Pseudo-header fields are only valid in the context in which they are
> **MUST NOT**: defined.  Pseudo-header fields defined for requests MUST NOT appear
   in responses; pseudo-header fields defined for responses MUST NOT
> **MUST NOT**: appear in requests.  Pseudo-header fields MUST NOT appear in a
   trailer section.  Endpoints MUST treat a request or response that
   contains undefined or invalid pseudo-header fields as malformed
   (Section 8.1.1).

> **MUST**: All pseudo-header fields MUST appear in a field block before all
   regular field lines.  Any request or response that contains a pseudo-
   header field that appears in a field block after a regular field line
> **MUST**: MUST be treated as malformed (Section 8.1.1).

> **MUST NOT**: The same pseudo-header field name MUST NOT appear more than once in a
   field block.  A field block for an HTTP request or response that
> **MUST**: contains a repeated pseudo-header field name MUST be treated as
   malformed (Section 8.1.1).

### 8.3.1  Request Pseudo-Header Fields

   The following pseudo-header fields are defined for HTTP/2 requests:

   *  The ":method" pseudo-header field includes the HTTP method
      (Section 9 of [HTTP]).

   *  The ":scheme" pseudo-header field includes the scheme portion of
      the request target.  The scheme is taken from the target URI
      (Section 3.1 of [RFC3986]) when generating a request directly, or
      from the scheme of a translated request (for example, see
      Section 3.3 of [HTTP/1.1]).  Scheme is omitted for CONNECT
      requests (Section 8.5).

      ":scheme" is not restricted to "http" and "https" schemed URIs.  A
      proxy or gateway can translate requests for non-HTTP schemes,
      enabling the use of HTTP to interact with non-HTTP services.

   *  The ":authority" pseudo-header field conveys the authority portion
      (Section 3.2 of [RFC3986]) of the target URI (Section 7.1 of
> **MUST NOT**: [HTTP]).  The recipient of an HTTP/2 request MUST NOT use the Host
      header field to determine the target URI if ":authority" is
      present.

> **MUST**: Clients that generate HTTP/2 requests directly MUST use the
      ":authority" pseudo-header field to convey authority information,
      unless there is no authority information to convey (in which case
> **MUST NOT**: it MUST NOT generate ":authority").

> **MUST NOT**: Clients MUST NOT generate a request with a Host header field that
      differs from the ":authority" pseudo-header field.  A server
> **SHOULD**: SHOULD treat a request as malformed if it contains a Host header
      field that identifies an entity that differs from the entity in
      the ":authority" pseudo-header field.  The values of fields need
      to be normalized to compare them (see Section 6.2 of [RFC3986]).
      An origin server can apply any normalization method, whereas other
> **MUST**: servers MUST perform scheme-based normalization (see Section 6.2.3
      of [RFC3986]) of the two fields.

> **MUST**: An intermediary that forwards a request over HTTP/2 MUST construct
      an ":authority" pseudo-header field using the authority
      information from the control data of the original request, unless
      the original request's target URI does not contain authority
> **MUST NOT**: information (in which case it MUST NOT generate ":authority").
      Note that the Host header field is not the sole source of this
      information; see Section 7.2 of [HTTP].

      An intermediary that needs to generate a Host header field (which
> **MUST**: might be necessary to construct an HTTP/1.1 request) MUST use the
      value from the ":authority" pseudo-header field as the value of
      the Host field, unless the intermediary also changes the request
      target.  This replaces any existing Host field to avoid potential
      vulnerabilities in HTTP routing.

> **MAY**: An intermediary that forwards a request over HTTP/2 MAY retain any
      Host header field.

      Note that request targets for CONNECT or asterisk-form OPTIONS
      requests never include authority information; see Sections 7.1 and
      7.2 of [HTTP].

> **MUST NOT**: ":authority" MUST NOT include the deprecated userinfo subcomponent
      for "http" or "https" schemed URIs.

   *  The ":path" pseudo-header field includes the path and query parts
      of the target URI (the absolute-path production and, optionally, a
      '?' character followed by the query production; see Section 4.1 of
      [HTTP]).  A request in asterisk form (for OPTIONS) includes the
      value '*' for the ":path" pseudo-header field.

> **MUST NOT**: This pseudo-header field MUST NOT be empty for "http" or "https"
      URIs; "http" or "https" URIs that do not contain a path component
> **MUST**: MUST include a value of '/'.  The exceptions to this rule are:

      -  an OPTIONS request for an "http" or "https" URI that does not
> **MUST**: include a path component; these MUST include a ":path" pseudo-
         header field with a value of '*' (see Section 7.1 of [HTTP]).

      -  CONNECT requests (Section 8.5), where the ":path" pseudo-header
         field is omitted.

> **MUST**: All HTTP/2 requests MUST include exactly one valid value for the
   ":method", ":scheme", and ":path" pseudo-header fields, unless they
   are CONNECT requests (Section 8.5).  An HTTP request that omits
   mandatory pseudo-header fields is malformed (Section 8.1.1).

   Individual HTTP/2 requests do not carry an explicit indicator of
   protocol version.  All HTTP/2 requests implicitly have a protocol
   version of "2.0" (see Section 6.2 of [HTTP]).

### 8.3.2  Response Pseudo-Header Fields

   For HTTP/2 responses, a single ":status" pseudo-header field is
   defined that carries the HTTP status code field (see Section 15 of
> **MUST**: [HTTP]).  This pseudo-header field MUST be included in all responses,
   including interim responses; otherwise, the response is malformed
   (Section 8.1.1).

   HTTP/2 responses implicitly have a protocol version of "2.0".

---

**Navigation:** [[../RFC9113|RFC9113 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
