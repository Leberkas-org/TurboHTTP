---
title: 6.2.  Control Data
rfc_number: 9110
rfc_section: '6.2'
source_url: 'https://www.rfc-editor.org/rfc/rfc9110'
description: 'Section 6.2: Control Data — RFC 9110 — HTTP Semantics'
tags:
  - RFC9110
  - HTTP-semantics
  - methods
  - status-codes
  - redirects
  - retries
  - content-negotiation
  - conditional-requests
  - control_data
---

## 6.2.  Control Data

## 6.2  Control Data

   Messages start with control data that describe its primary purpose.
   Request message control data includes a request method (Section 9),
   request target (Section 7.1), and protocol version (Section 2.5).
   Response message control data includes a status code (Section 15),
   optional reason phrase, and protocol version.

   In HTTP/1.1 ([HTTP/1.1]) and earlier, control data is sent as the
   first line of a message.  In HTTP/2 ([HTTP/2]) and HTTP/3 ([HTTP/3]),
   control data is sent as pseudo-header fields with a reserved name
   prefix (e.g., ":authority").

   Every HTTP message has a protocol version.  Depending on the version
   in use, it might be identified within the message explicitly or
   inferred by the connection over which the message is received.
   Recipients use that version information to determine limitations or
   potential for later communication with that sender.

   When a message is forwarded by an intermediary, the protocol version
   is updated to reflect the version used by that intermediary.  The Via
   header field (Section 7.6.3) is used to communicate upstream protocol
   information within a forwarded message.

> **SHOULD**: A client SHOULD send a request version equal to the highest version
   to which the client is conformant and whose major version is no
   higher than the highest version supported by the server, if this is
> **MUST NOT**: known.  A client MUST NOT send a version to which it is not
   conformant.

> **MAY**: A client MAY send a lower request version if it is known that the
   server incorrectly implements the HTTP specification, but only after
   the client has attempted at least one normal request and determined
   from the response status code or header fields (e.g., Server) that
   the server improperly handles higher request versions.

> **SHOULD**: A server SHOULD send a response version equal to the highest version
   to which the server is conformant that has a major version less than
> **MUST NOT**: or equal to the one received in the request.  A server MUST NOT send
   a version to which it is not conformant.  A server can send a 505
   (HTTP Version Not Supported) response if it wishes, for any reason,
   to refuse service of the client's major protocol version.

   A recipient that receives a message with a major version number that
   it implements and a minor version number higher than what it
> **SHOULD**: implements SHOULD process the message as if it were in the highest
   minor version within that major version to which the recipient is
   conformant.  A recipient can assume that a message with a higher
   minor version, when sent to a recipient that has not yet indicated
   support for that higher version, is sufficiently backwards-compatible
   to be safely processed by any implementation of the same major
   version.


---

## TurboHttp Compliance

**Status**: ✅ Compliant

### Implementation Notes
- **`HttpRequestEncoder.cs`** — Sets protocol version in request control data; sends highest conformant version per §6.2
- **`Http11RequestEncoder.cs`** — Encodes request-line with method, request-target, and HTTP/1.1 version
- **`Http2RequestEncoder.cs`** — Maps control data to pseudo-header fields (`:method`, `:path`, `:scheme`, `:authority`)
- **`HttpResponseDecoder.cs`** — Parses status code and reason phrase from response control data

### Test References
- `TurboHttp.Tests/RFC9110/23_ControlDataTests.cs` — Version negotiation, pseudo-header mapping

### Known Gaps
- ⚠️ Version downgrade — Client does not automatically retry with lower HTTP version if server indicates incompatibility

---

**Navigation:** [[../RFC9110|RFC9110 Index]] | [[../../00-RFC_STATUS_MATRIX|Status Matrix]]
