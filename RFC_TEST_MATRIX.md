# RFC Conformance Test Matrix

> **Scope: Client-side only.**
> Encoders: `HttpRequestMessage → bytes`. Decoders: `bytes → HttpResponseMessage`.
> Server-side request parsing and response encoding are out of scope.

### Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Covered — test with matching ID or semantically equivalent test exists |
| ⚠ | Partial — related test exists but does not fully exercise this requirement |
| ❌ | Not covered — gap identified |
| N/A | Not applicable — requirement is server-side or otherwise out of client scope |

---

## RFC 1945 – HTTP/1.0

### §5.1 Request-Line

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 1945-5.1-001 | Valid HTTP/1.0 request-line | MUST | ✅ | `1945-enc-001` | Starts with `GET /path HTTP/1.0\r\n` |
| 1945-5.1-002 | HTTP-Version must be 1.0 (encoder) | MUST | ✅ | `1945-enc-001`, `enc1-m-001` | Version string is HTTP/1.0 |
| 1945-5.1-003 | Simple-Request (no version) | SHOULD | N/A | — | Client encoder always emits full request-line |
| 1945-5.1-004 | Method is case-sensitive | MUST | ✅ | `1945-5.1-004` | Lowercase method rejected by encoder |
| 1945-5.1-005 | Absolute URI allowed | SHOULD | ✅ | `1945-5.1-005` | Absolute URI encoded in request-line |
| 1945-5.1-006 | Missing SP between elements | MUST | N/A | — | Encoder always emits correct SP; decoder is response-only |
| 1945-5.1-007 | Invalid HTTP version format | MUST | N/A | — | Encoder always emits correct version; decoder is response-only |

### §4 Header Fields

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 1945-4-001 | Host header NOT required | MUST | ✅ | `1945-enc-002` | No Host in HTTP/1.0 output |
| 1945-4-002 | Header folding allowed (obs-fold) | MUST | ✅ | `1945-4-002` | Parse success |
| 1945-4-003 | Multiple headers same name | MUST | ✅ | `1945-4-003` | Accept |
| 1945-4-004 | Header without colon | MUST | ✅ | `1945-4-004` | Parse error |
| 1945-4-005 | Header name case-insensitive | MUST | ✅ | `1945-4-005` | Parse success |
| 1945-4-006 | Leading/trailing whitespace trimmed | MUST | ✅ | `1945-4-006` | Value trimmed |
| 1945-4-007 | Invalid header name (space inside) | MUST | ✅ | `1945-4-007` | Parse error |

### §7 Entity Body

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 1945-7-001 | Content-Length body | MUST | ✅ | `1945-7-001` | Parse 5 bytes |
| 1945-7-002 | Zero Content-Length | MUST | ✅ | `1945-7-002` | No body |
| 1945-7-003 | No Content-Length | MUST | ✅ | `1945-7-003` | Read until connection close |
| 1945-7-004 | Chunked encoding NOT supported (raw body) | MUST | ✅ | `1945-dec-006` | Raw bytes returned |
| 1945-7-005 | Multiple Content-Length headers | MUST | ✅ | `1945-7-005` | Reject for safety |
| 1945-7-006 | Negative Content-Length | MUST | ✅ | `1945-7-006` | Error |

### §8 Connection Management

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 1945-8-001 | Default connection is close | MUST | ✅ | `1945-8-001` | Close after response |
| 1945-8-002 | Keep-Alive extension | SHOULD | ✅ | `1945-8-002` | Keep connection open |
| 1945-8-003 | Keep-Alive header parameters | SHOULD | ✅ | `1945-8-003` | Parse parameters |
| 1945-8-004 | HTTP/1.1 keep-alive behavior must NOT apply | MUST | ✅ | `1945-8-004` | Do NOT default keep-alive |
| 1945-8-005 | Explicit Connection: close | MUST | ✅ | `1945-8-005` | Close connection |

### §5 — Encoder: HttpRequestMessage → bytes

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 1945-enc-001 | Request-line uses HTTP/1.0 | MUST | ✅ | `1945-enc-001` | Starts with `GET /path HTTP/1.0\r\n` |
| 1945-enc-002 | Host header NOT emitted | MUST | ✅ | `1945-enc-002` | Host absent in output |
| 1945-enc-003 | Transfer-Encoding NOT emitted | MUST | ✅ | `1945-enc-003` | TE absent in output |
| 1945-enc-004 | Connection NOT emitted | MUST | ✅ | `1945-enc-004` | Connection absent in output |
| 1945-enc-005 | Content-Length set for body | MUST | ✅ | `1945-enc-005` | `Content-Length: 5` present |
| 1945-enc-006 | No Content-Length for empty body | MUST | ✅ | `1945-enc-006` | Content-Length absent |
| 1945-enc-007 | Path-and-query preserved | MUST | ✅ | `1945-enc-007` | Request-target is `/search?q=hello` |
| 1945-enc-008 | Binary body preserved exactly | MUST | ✅ | `1945-enc-008` | Encoded body bytes match input |
| 1945-enc-009 | POST with UTF-8 body | MUST | ✅ | `1945-enc-009` | Body bytes match UTF-8 encoded JSON |

### §6 — Decoder: bytes → HttpResponseMessage

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 1945-dec-001 | Status-line HTTP/1.0 parsed | MUST | ✅ | `1945-dec-001` | StatusCode=200 |
| 1945-dec-002 | Version set to 1.0 | MUST | ✅ | `1945-dec-002` | response.Version == 1.0 |
| 1945-dec-003 | All RFC 1945 defined codes | MUST | ✅ | `1945-dec-003` (Theory) | All parsed |
| 1945-dec-004 | 304 Not Modified — no body | MUST | ✅ | `1945-dec-004` | Content.Length == 0 |
| 1945-dec-005 | LF-only line endings accepted | SHOULD | ✅ | `1945-dec-005` | Parsed successfully |
| 1945-dec-006 | Chunked treated as raw body | MUST | ✅ | `1945-dec-006` | Raw bytes returned |
| 1945-dec-007 | EOF body via TryDecodeEof | MUST | ✅ | `1945-dec-007` | Full body on connection close |

---

## RFC 7230 — HTTP/1.1 Message Syntax and Routing

### §3.1.1 Request-Line (Encoder)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7230-3.1.1-001 | Valid request-line produced | MUST | ✅ | `7230-enc-001`, `enc3-m-001` | `METHOD /path HTTP/1.1\r\n` |
| 7230-3.1.1-002 | Method is case-sensitive | MUST | ✅ | `7230-3.1.1-002` | Lowercase rejected by encoder |
| 7230-3.1.1-003 | SP between components | MUST | ✅ | `7230-3.1.1-004`, `enc3-m-001` | Single SP separators only |
| 7230-3.1.1-004 | CRLF line ending | MUST | ✅ | `7230-3.1.1-004` | Each line ends with CRLF |
| 7230-3.1.1-005 | OPTIONS with asterisk | MUST | ✅ | `enc3-uri-001` | `OPTIONS * HTTP/1.1` encoded |
| 7230-3.1.1-006 | Absolute URI | SHOULD | ✅ | `enc3-uri-002` | Absolute-URI preserved for proxy |
| 7230-3.1.1-007 | Missing HTTP version | MUST | N/A | — | Encoder always emits version |
| 7230-3.1.1-008 | Invalid HTTP version | MUST | N/A | — | Encoder always emits HTTP/1.1 |

### §3.2 Header Fields

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7230-3.2-001 | Header field format | MUST | ✅ | `7230-3.2-001` (enc+dec) | Name: SP value CRLF |
| 7230-3.2-002 | Optional whitespace | MUST | ✅ | `7230-3.2-002` (enc+dec) | OWS trimmed |
| 7230-3.2-003 | Empty header value | MUST | ✅ | `7230-3.2-003` | Parse success, empty value |
| 7230-3.2-004 | Multiple header values | MUST | ✅ | `7230-3.2-004` | Both accessible |
| 7230-3.2-005 | Obs-fold (obsolete) | MUST | ✅ | `7230-3.2-005` | Rejected in HTTP/1.1 |
| 7230-3.2-006 | Header without colon | MUST | ✅ | `7230-3.2-006` | Parse error |
| 7230-3.2-007 | Header name case | MUST | ✅ | `7230-3.2-007` (enc+dec) | Case-insensitive lookup |
| 7230-3.2-008 | Invalid header name | MUST | ✅ | `7230-3.2-008` | Space in name → parse error |

### §3.3 Message Body

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7230-3.3-001 | Content-Length body | MUST | ✅ | `7230-3.3-001` | Parse 5 bytes |
| 7230-3.3-002 | Zero Content-Length | MUST | ✅ | `7230-3.3-002` | No body |
| 7230-3.3-003 | Chunked encoding | MUST | ✅ | `7230-3.3-003` | Parse chunks |
| 7230-3.3-004 | Conflicting headers | MUST | ✅ | `7230-3.3-004` | Error or prefer chunked |
| 7230-3.3-005 | Multiple Content-Length | MUST | ✅ | `7230-3.3-005` | Error |
| 7230-3.3-006 | Negative Content-Length | MUST | ✅ | `7230-3.3-006` | Error |
| 7230-3.3-007 | No body indicators | MUST | ✅ | `7230-3.3-007` | No body |

### §4.1 Chunked Transfer Encoding

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7230-4.1-001 | Simple chunk | MUST | ✅ | `7230-4.1-001` | Parse "Hello" |
| 7230-4.1-002 | Multiple chunks | MUST | ✅ | `7230-4.1-002` | Parse "Hello World" |
| 7230-4.1-003 | Chunk extension | SHOULD | ✅ | `7230-4.1-003` | Parse "Hello", ignore extension |
| 7230-4.1-004 | Trailer fields | SHOULD | ✅ | `7230-4.1-004` | Parse "Hello" + trailer |
| 7230-4.1-005 | Invalid chunk size | MUST | ✅ | `7230-4.1-005` | Error |
| 7230-4.1-006 | Missing final chunk | MUST | ✅ | `7230-4.1-006` | NeedMoreData |
| 7230-4.1-007 | Zero-size chunk | MUST | ✅ | `7230-4.1-007` | End of body |
| 7230-4.1-008 | Chunk size too large | MUST | ✅ | `7230-4.1-008` | Error (overflow) |

### §6.1 Connection

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7230-6.1-001 | Connection: close | MUST | ✅ | `7230-6.1-001` | Close after response |
| 7230-6.1-002 | Connection: keep-alive | SHOULD | ✅ | `7230-6.1-002` | Keep connection open |
| 7230-6.1-003 | No Connection header (HTTP/1.1) | MUST | ✅ | `7230-6.1-003` | Default keep-alive |
| 7230-6.1-004 | HTTP/1.0 default | MUST | ✅ | `7230-6.1-004` | Default close |
| 7230-6.1-005 | Multiple Connection tokens | MUST | ✅ | `7230-6.1-005` (enc+dec) | All tokens recognized/encoded |

---

## RFC 7231 — HTTP/1.1 Semantics and Content

### §6.1 Status Codes

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7231-6.1-001 | 1xx Informational | MUST | ✅ | `7231-6.1-001` | No body |
| 7231-6.1-002 | 2xx Success | MUST | ✅ | `7231-6.1-002` (Theory) | Parse success |
| 7231-6.1-003 | 3xx Redirection | MUST | ✅ | `7231-6.1-003` (Theory) | Parse + Location header |
| 7231-6.1-004 | 4xx Client Error | MUST | ✅ | `7231-6.1-004` (Theory) | Parse error response |
| 7231-6.1-005 | 5xx Server Error | MUST | ✅ | `7231-6.1-005` (Theory) | Parse error response |
| 7231-6.1-006 | Custom status code | SHOULD | ✅ | `7231-6.1-006` | 599 parsed |
| 7231-6.1-007 | Invalid status code | MUST | ✅ | `7231-6.1-007` | >599 → parse error |
| 7231-6.1-008 | Empty reason phrase | MUST | ✅ | `7231-6.1-008` | Parse success |

### §7.1.1.1 Date/Time Formats

| Test ID | Requirement | Priority | Covered? | Covered By Test | Notes |
|---------|-------------|----------|----------|-----------------|-------|
| 7231-7.1.1-001 | IMF-fixdate | MUST | ❌ | — | Date header parsed as raw string only |
| 7231-7.1.1-002 | RFC 850 (obsolete) | MUST | ❌ | — | Date header not parsed |
| 7231-7.1.1-003 | ANSI C asctime | MUST | ❌ | — | Date header not parsed |
| 7231-7.1.1-004 | Non-GMT timezone | MUST | ❌ | — | Date header not parsed |
| 7231-7.1.1-005 | Invalid date format | MUST | ❌ | — | Date header passed through as-is |

> **Note:** RFC 7231 §7.1.1 date parsing is out of scope for the current protocol layer.
> `HttpResponseMessage` exposes the raw `Date` header string; callers are responsible for
> parsing date values. This is consistent with .NET's `HttpClient` behavior.

---

## RFC 7233 — HTTP/1.1 Range Requests

### §2.1 Range Units (Encoder)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7233-2.1-001 | Simple byte range | MUST | ✅ | `7233-2.1-001` | `Range: bytes=0-499` encoded |
| 7233-2.1-002 | Suffix byte range | MUST | ✅ | `7233-2.1-002` | `Range: bytes=-500` encoded |
| 7233-2.1-003 | Open-ended range | MUST | ✅ | `7233-2.1-003` | `Range: bytes=500-` encoded |
| 7233-2.1-004 | Multiple ranges | SHOULD | ✅ | `7233-2.1-004` | Multi-range encoded |
| 7233-2.1-005 | Invalid range value | MUST | N/A | — | Http11Encoder passes Range header verbatim |
| 7233-2.1-006 | Unsatisfiable range (416) | MUST | N/A | — | Server-side response; decoder parses 416 as any 4xx |

### §4.1 206 Partial Content (Decoder)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7233-4.1-001 | Content-Range header present | MUST | ✅ | `7233-4.1-001` | `Content-Range` header accessible |
| 7233-4.1-002 | Single part 206 response | MUST | ✅ | `7233-4.1-002` | StatusCode=206, partial body |
| 7233-4.1-003 | Multipart response | SHOULD | ✅ | `7233-4.1-003` | Body contains multipart/byteranges |
| 7233-4.1-004 | Complete length unknown | SHOULD | ✅ | `7233-4.1-004` | `Content-Range: bytes 0-499/*` parsed |

---

## RFC 7540 — HTTP/2

### §3.5 HTTP/2 Connection Preface

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-3.5-001 | Client preface | MUST | ✅ | `RFC9113-3.4-CP-001..008` | Exact magic + SETTINGS |
| 7540-3.5-002 | Invalid preface | MUST | ✅ | `RFC9113-3.4-SP-003..013` | PROTOCOL_ERROR |
| 7540-3.5-003 | SETTINGS frame after preface | MUST | ✅ | `RFC9113-3.4-SP-001` | Accepted |
| 7540-3.5-004 | Missing SETTINGS | MUST | ✅ | `RFC9113-3.4-SP-002` | NeedMoreData |

### §4.1 Frame Format

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-4.1-001 | Valid frame header | MUST | ✅ | `RFC7540-4.1-FP-001..006` | Parse success |
| 7540-4.1-002 | Frame length 24-bit | MUST | ✅ | `RFC7540-4.1-FP-006` | Correct length field |
| 7540-4.1-003 | Frame type recognized | MUST | ✅ | `RFC7540-4.1-FP-003` | Type dispatched |
| 7540-4.1-004 | Unknown frame type ignored | MUST | ✅ | `RFC7540-4.1-FP-013..015` | Silently ignored |
| 7540-4.1-005 | Stream ID 31-bit (R-bit cleared) | MUST | ✅ | `RFC9113-5.1-SS-021..022` | Stream 0 control frames |
| 7540-4.1-006 | R-bit ignored (must remain 0) | MUST | ✅ | Implicit — decoder masks R-bit | R-bit masked; not validated per RFC |
| 7540-4.1-007 | Frame size limit | MUST | ✅ | `RFC7540-4.2-FP-007..012` | FRAME_SIZE_ERROR |

### §5.1 Stream States

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-5.1-001 | Idle to open | MUST | ✅ | `RFC9113-5.1-SS-002` | Stream opens |
| 7540-5.1-002 | Open to half-closed (local) | MUST | ✅ | `RFC9113-5.1-SS-003` | Auto-close on END_STREAM |
| 7540-5.1-003 | Open to half-closed (remote) | MUST | ✅ | `RFC9113-5.1-SS-004` | Half-closed via DATA END_STREAM |
| 7540-5.1-004 | Half-closed to closed | MUST | ✅ | `RFC9113-5.1-SS-010` | Response produced |
| 7540-5.1-005 | Idle to reserved | MUST | ⚠ | `RT-2-013` (PUSH_PROMISE decoded) | Push promise partial coverage |
| 7540-5.1-006 | Invalid state transition | MUST | ✅ | `RFC9113-5.1-SS-006..008` | PROTOCOL_ERROR |
| 7540-5.1-007 | Stream ID reuse | MUST | ✅ | `RFC9113-5.1-SS-007` | STREAM_CLOSED error |
| 7540-5.1-008 | Odd stream IDs from client | MUST | ✅ | `RT-2-046`, `ST_20_REQ_003` | IDs: 1, 3, 5, 7, 9 |

### §5.2 Flow Control

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-5.2-001 | Initial window size | MUST | ✅ | `FC-001`, `FC-006` | Default 65535 |
| 7540-5.2-002 | WINDOW_UPDATE | MUST | ✅ | `FC-012`, `FC-017` | Window updated |
| 7540-5.2-003 | Flow control violation | MUST | ✅ | `FC-003`, `FC-008` | FLOW_CONTROL_ERROR |
| 7540-5.2-004 | Window overflow | MUST | ✅ | `FC-014`, `FC-019` | FLOW_CONTROL_ERROR |
| 7540-5.2-005 | Zero window size | MUST | ✅ | `FC-011` | Send blocked at 0 |
| 7540-5.2-006 | Connection window | MUST | ✅ | `FC-002`, `FC-007` | Connection-level enforced |
| 7540-5.2-007 | Stream window | MUST | ✅ | `FC-008`, `FC-009` | Per-stream enforced |
| 7540-5.2-008 | Negative window increment | MUST | ✅ | `FC-021`, `FC-022` | PROTOCOL_ERROR |

### §6.1 DATA Frame

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-6.1-001 | Valid DATA frame | MUST | ✅ | `RFC9113-5.1-SS-004` | Data parsed |
| 7540-6.1-002 | END_STREAM flag | MUST | ✅ | `RFC9113-5.1-SS-004` | Stream closed |
| 7540-6.1-003 | PADDED flag | SHOULD | ⚠ | — | Padding stripped in frame |
| 7540-6.1-004 | DATA on stream 0 | MUST | ✅ | `RFC9113-5.1-SS-021`, `EM-005` | PROTOCOL_ERROR |
| 7540-6.1-005 | DATA on closed stream | MUST | ✅ | `RFC9113-5.1-SS-007`, `EM-015` | STREAM_CLOSED |
| 7540-6.1-006 | Empty DATA frame | MUST | ✅ | `FC-027`, `FC-038` | Accepted |

### §6.2 HEADERS Frame

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-6.2-001 | Valid HEADERS frame | MUST | ✅ | `RFC9113-5.1-SS-002`, `HV-001` | Headers parsed |
| 7540-6.2-002 | END_STREAM flag | MUST | ✅ | `RFC9113-5.1-SS-003`, `RT-2-043` | Stream closed |
| 7540-6.2-003 | END_HEADERS flag | MUST | ✅ | `RFC9113-6.10-CF-001` | Headers complete |
| 7540-6.2-004 | PADDED flag | SHOULD | ⚠ | — | Frame-level padding |
| 7540-6.2-005 | PRIORITY flag | SHOULD | ⚠ | — | Priority data present in frame types |
| 7540-6.2-006 | CONTINUATION required | MUST | ✅ | `RFC9113-6.10-CF-002..003` | Awaits CONTINUATION |
| 7540-6.2-007 | HEADERS on stream 0 | MUST | ✅ | `RFC9113-5.1-SS-022` | PROTOCOL_ERROR |

### §6.9 CONTINUATION Frame

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-6.9-001 | After HEADERS | MUST | ✅ | `RFC9113-6.10-CF-003` | Header block appended |
| 7540-6.9-002 | END_HEADERS flag | MUST | ✅ | `RFC9113-6.10-CF-003` | Headers complete |
| 7540-6.9-003 | Multiple CONTINUATION | MUST | ✅ | `RFC9113-6.10-CF-005` | All blocks appended |
| 7540-6.9-004 | Wrong stream ID | MUST | ✅ | `RFC9113-6.10-CF-015` | PROTOCOL_ERROR |
| 7540-6.9-005 | Interleaved frames | MUST | ✅ | `RFC9113-6.10-CF-007..013` | PROTOCOL_ERROR |
| 7540-6.9-006 | CONTINUATION on stream 0 | MUST | ✅ | `RFC9113-6.10-CF-014` | PROTOCOL_ERROR |

### §8.1 Request Encoder (Frames)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7540-8.1-001 | GET produces HEADERS with END_STREAM | MUST | ✅ | `9113-8.1-001` | Single HEADERS frame |
| 7540-8.1-002 | POST produces HEADERS + DATA | MUST | ✅ | `9113-8.1-002` | HEADERS then DATA |
| 7540-8.1-003 | Pseudo-headers :method :path :scheme :authority | MUST | ✅ | `9113-8.3.1-001` | All four present |
| 7540-8.1-004 | :path includes query string | MUST | ✅ | `9113-8.3.1-002` | `/search?term=foo&page=2` |
| 7540-8.1-005 | Connection-specific headers stripped | MUST | ✅ | `9113-8.2.2-001` | `connection` absent |
| 7540-8.1-006 | Large header block uses CONTINUATION | MUST | ✅ | `9113-6.10-002` | HEADERS + CONTINUATION frames |
| 7540-8.1-007 | All frames share same stream ID | MUST | ✅ | `9113-5.1.1-001` | All frame stream IDs match |

---

## RFC 7541 — HPACK

### §2.3 Dynamic Table

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7541-2.3-001 | Table insertion | MUST | ✅ | `DT-020`, `HD-010` | Entry at index 62 |
| 7541-2.3-002 | Table eviction | MUST | ✅ | `DT-030`, `DT-033` | Evict oldest entry |
| 7541-2.3-003 | Table size update | MUST | ✅ | `TS-001`, `DT-040` | Resize table |
| 7541-2.3-004 | Table size 0 | MUST | ✅ | `DT-032`, `TS-003` | Clear all entries |
| 7541-2.3-005 | Table size too large | MUST | ✅ | `TS-005` | HpackException |

### §5.1 Integer Representation

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7541-5.1-001 | Small integer | MUST | ✅ | `HD-001..005`, `ST-020..025` | Prefix bits used |
| 7541-5.1-002 | Large integer | MUST | ✅ | `HD-012`, `DT-010` | Multi-byte encoding |
| 7541-5.1-003 | Maximum integer | MUST | ✅ | `FC-015`, `FC-020` | 2^31-1 accepted |
| 7541-5.1-004 | Integer overflow | MUST | ✅ | `HD-007`, `DT-042` | HpackException |

### §5.2 String Representation

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7541-5.2-001 | Plain string | MUST | ✅ | `HD-010..014`, `HD-020..025` | Raw string parsed |
| 7541-5.2-002 | Huffman string | MUST | ✅ | `ED-001..005`, `RT-2-036` | Huffman decoded |
| 7541-5.2-003 | Empty string | MUST | ✅ | `DT-012` | Empty string |
| 7541-5.2-004 | Large string | MUST | ✅ | `RT-2-012`, `RT-2-032` | Parse correctly |
| 7541-5.2-005 | Invalid Huffman | MUST | ✅ | `EO-001..005` | HpackException |

### §6.1 Indexed Header Field

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7541-6.1-001 | Static table index | MUST | ✅ | `ST-010..018`, `HD-001..005` | From static table |
| 7541-6.1-002 | Dynamic table index | MUST | ✅ | `HD-012`, `ES-001..002` | From dynamic table |
| 7541-6.1-003 | Index out of range | MUST | ✅ | `ST-030..033`, `HD-006..007` | HpackException |

### §6.2 Literal Header Field

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7541-6.2-001 | With incremental indexing | MUST | ✅ | `HD-010..013` | Added to dynamic table |
| 7541-6.2-002 | Without indexing | MUST | ✅ | `HD-020..025` | Not added to table |
| 7541-6.2-003 | Never indexed | MUST | ✅ | `RT-2-010..011`, `RT-2-025` | Not indexed, NeverIndex flag |
| 7541-6.2-004 | Indexed name | MUST | ✅ | `HD-011`, `ST-040..041` | Name from static table |
| 7541-6.2-005 | Literal name | MUST | ✅ | `HD-010`, `HD-020` | Both literal |

### Appendix C Examples

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 7541-C.2-001 | Request without Huffman | MUST | ✅ | `RT-2-036`, `ES-001..003` | Match expected output |
| 7541-C.3-001 | Request with Huffman | MUST | ✅ | `RT-2-036`, `RT-2-037` | Match expected output |
| 7541-C.4-001 | Response without Huffman | MUST | ✅ | `RT-2-038`, `ES-003` | Match expected output |
| 7541-C.5-001 | Response with Huffman | MUST | ✅ | `RT-2-036`, `RT-2-037` | Match expected output |
| 7541-C.6-001 | Response with Huffman (cont) | MUST | ✅ | `RT-2-055` | HPACK state survives |

---

## RFC 9110 — HTTP Semantics: Content Encoding

### §8.4.1 Content Codings (Decompression)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-8.4-001 | gzip decompression | MUST | ✅ | `RFC9110-8.4-gzip-001` | Body decompressed |
| 9110-8.4-002 | Remove Content-Encoding after decode | MUST | ✅ | `RFC9110-8.4-gzip-002` | Header removed |
| 9110-8.4-003 | Update Content-Length after decode | MUST | ✅ | `RFC9110-8.4-gzip-003` | Length corrected |
| 9110-8.4-004 | Case-insensitive Content-Encoding | MUST | ✅ | `RFC9110-8.4-gzip-004` | `GZIP` accepted |
| 9110-8.4-005 | x-gzip alias | SHOULD | ✅ | `RFC9110-8.4-gzip-005` | x-gzip decompressed |
| 9110-8.4-006 | Empty body with gzip encoding | MUST | ✅ | `RFC9110-8.4-gzip-006` | No error |
| 9110-8.4-007 | Corrupt gzip data | MUST | ✅ | `RFC9110-8.4-gzip-007` | DecompressionFailed |
| 9110-8.4-008 | Large gzip body (64 KB) | MUST | ✅ | `RFC9110-8.4-gzip-008` | Correct decompression |
| 9110-8.4-009 | UTF-8 multibyte content | MUST | ✅ | `RFC9110-8.4-gzip-009` | Bytes preserved |
| 9110-8.4-010 | 204 No Content — skip decompression | MUST | ✅ | `RFC9110-8.4-gzip-010` | No decompression attempted |

### §8.4.1.2 deflate Coding

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-8.4-deflate-001 | deflate decompression | MUST | ✅ | `RFC9110-8.4-deflate-001` | Body decompressed |
| 9110-8.4-deflate-002 | identity encoding passthrough | MUST | ✅ | `RFC9110-8.4-deflate-002` | Body unchanged |
| 9110-8.4-deflate-003 | No Content-Encoding passthrough | MUST | ✅ | `RFC9110-8.4-deflate-003` | Body unchanged |
| 9110-8.4-deflate-004 | Unknown encoding | MUST | ✅ | `RFC9110-8.4-deflate-004` | DecompressionFailed |

### §8.4.1.3 br (Brotli) Coding

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-8.4-br-001 | Brotli decompression | MUST | ✅ | `RFC9110-8.4-br-001` | Body decompressed |
| 9110-8.4-br-002 | Large Brotli content | MUST | ✅ | `RFC9110-8.4-br-002` | Correct decompression |

### §8.4 Cross-Version Consistency

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-8.4-h10-001 | gzip in HTTP/1.0 | MUST | ✅ | `RFC9110-8.4-gzip-h10-001..003` | Decompressed correctly |
| 9110-8.4-h10-002 | deflate in HTTP/1.0 | MUST | ✅ | `RFC9110-8.4-deflate-h10-001` | Decompressed correctly |
| 9110-8.4-h10-003 | Brotli in HTTP/1.0 | MUST | ✅ | `RFC9110-8.4-br-h10-001` | Decompressed correctly |
| 9110-8.4-h2-001 | gzip in HTTP/2 | MUST | ✅ | `RFC9110-8.4-gzip-h2-001..003` | Decompressed correctly |
| 9110-8.4-h2-002 | deflate in HTTP/2 | MUST | ✅ | `RFC9110-8.4-deflate-h2-001` | Decompressed correctly |
| 9110-8.4-h2-003 | Brotli via HTTP/2 | MUST | ✅ | `RFC9110-8.4-gzip-h2-004` | Decompressed correctly |
| 9110-8.4-h2-004 | No encoding in HTTP/2 | MUST | ✅ | `RFC9110-8.4-identity-h2-001` | Body unchanged |
| 9110-8.4-dist-001 | Transfer-Encoding vs Content-Encoding | MUST | ✅ | `RFC9110-8.4-distinction-001` | Not confused |

### §8.4 Stacked Encodings

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-8.4-stack-001 | gzip + br stacked | MUST | ✅ | `RFC9110-8.4-stacked-001` | Both layers removed |
| 9110-8.4-stack-002 | deflate + gzip + br stacked | MUST | ✅ | `RFC9110-8.4-stacked-002` | All layers removed |
| 9110-8.4-stack-003 | All headers removed after stacked decode | MUST | ✅ | `RFC9110-8.4-stacked-003` | No Content-Encoding |
| 9110-8.4-stack-004 | Content-Length updated after stacked decode | MUST | ✅ | `RFC9110-8.4-stacked-004` | Length corrected |

### §12.5.3 Accept-Encoding (Request Enrichment)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-12.5-001 | Add Accept-Encoding when not set | SHOULD | ✅ | `RFC9110-8.4-accept-001` | Header added |
| 9110-12.5-002 | Preserve existing Accept-Encoding | MUST | ✅ | `RFC9110-8.4-accept-002` | Not overridden |
| 9110-12.5-003 | Accept-Encoding on POST | SHOULD | ✅ | `RFC9110-8.4-accept-003` | Header added |
| 9110-12.5-004 | Accept-Encoding on PUT | SHOULD | ✅ | `RFC9110-8.4-accept-004` | Header added |

### §8.4 Round-Trip and Compatibility

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9110-8.4-rt-001 | Request/response compression cycle | MUST | ✅ | `RFC9110-8.4-roundtrip-001` | Full cycle works |
| 9110-8.4-rt-002 | No encoding with Accept-Encoding | MUST | ✅ | `RFC9110-8.4-roundtrip-002` | Content preserved |
| 9110-8.4-rt-003 | Brotli round-trip | MUST | ✅ | `RFC9110-8.4-roundtrip-003` | End-to-end |
| 9110-8.4-compat-001 | Consistent across HTTP versions | MUST | ✅ | `RFC9110-8.4-compat-001` | All versions match |
| 9110-8.4-compat-002 | Encoding mismatch handling | MUST | ✅ | `RFC9110-8.4-compat-002` | Error or fallback |

---

## RFC 9111 — HTTP Caching

### §5.2 Cache-Control Directives (Parser)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-5.2-001 | Null/empty input | MUST | ✅ | `RFC-9111-§5.2: null/empty/whitespace` | Returns null |
| 9111-5.2-002 | no-cache directive | MUST | ✅ | `RFC-9111-§5.2: no-cache` | Parsed correctly |
| 9111-5.2-003 | no-store directive | MUST | ✅ | `RFC-9111-§5.2: no-store` | Parsed correctly |
| 9111-5.2-004 | max-age=N parsed as TimeSpan | MUST | ✅ | `RFC-9111-§5.2: max-age=3600` | TimeSpan(1h) |
| 9111-5.2-005 | s-maxage=N parsed | MUST | ✅ | `RFC-9111-§5.2: s-maxage=600` | TimeSpan(10m) |
| 9111-5.2-006 | max-stale=N parsed | MUST | ✅ | `RFC-9111-§5.2: max-stale=300` | TimeSpan(5m) |
| 9111-5.2-007 | min-fresh=N parsed | MUST | ✅ | `RFC-9111-§5.2: min-fresh=60` | TimeSpan(1m) |
| 9111-5.2-008 | must-revalidate flag | MUST | ✅ | `RFC-9111-§5.2: must-revalidate` | Flag set |
| 9111-5.2-009 | public directive | MUST | ✅ | `RFC-9111-§5.2: public` | Flag set |
| 9111-5.2-010 | private directive | MUST | ✅ | `RFC-9111-§5.2: private` | Flag set |
| 9111-5.2-011 | immutable directive | SHOULD | ✅ | `RFC-9111-§5.2: immutable` | Flag set |
| 9111-5.2-012 | only-if-cached | MUST | ✅ | `RFC-9111-§5.2: only-if-cached` | Flag set |
| 9111-5.2-013 | Multiple directives in one header | MUST | ✅ | `RFC-9111-§5.2: multiple directives` | All parsed |
| 9111-5.2-014 | no-cache with field list | SHOULD | ✅ | `RFC-9111-§5.2: no-cache with field list` | Field list parsed |
| 9111-5.2-015 | Unknown directive ignored | MUST | ✅ | `RFC-9111-§5.2: unknown directive` | Silently ignored |
| 9111-5.2-016 | Case-insensitive parsing | MUST | ✅ | `RFC-9111-§5.2: case-insensitive` | MAX-AGE accepted |
| 9111-5.2-017 | no-transform directive | MUST | ✅ | `RFC-9111-§5.2: no-transform` | Flag set |
| 9111-5.2-018 | max-stale without value | SHOULD | ✅ | `RFC-9111-§5.2: max-stale without value` | Any staleness |

### §4.2 Freshness Calculation

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-4.2-001 | max-age freshness lifetime | MUST | ✅ | `RFC-9111-§4.2: max-age=60` | 60s lifetime |
| 9111-4.2-002 | s-maxage overrides max-age (shared) | MUST | ✅ | `RFC-9111-§4.2: s-maxage=120 overrides` | 120s for shared cache |
| 9111-4.2-003 | s-maxage ignored for private cache | MUST | ✅ | `RFC-9111-§4.2: s-maxage ignored` | max-age used |
| 9111-4.2-004 | Expires header fallback | MUST | ✅ | `RFC-9111-§5.3: Expires header` | Used when no max-age |
| 9111-4.2-005 | Heuristic freshness (10% of age) | SHOULD | ✅ | `RFC-9111-§4.2.2: heuristic 10%` | 10% of Last-Modified age |
| 9111-4.2-006 | Heuristic freshness capped at 1 day | SHOULD | ✅ | `RFC-9111-§4.2.2: capped at 1 day` | Max 86400s |
| 9111-4.2-007 | No freshness info → zero lifetime | MUST | ✅ | `RFC-9111-§4.2: no freshness info` | Lifetime = 0 |
| 9111-4.2-008 | Current age from Age header | MUST | ✅ | `RFC-9111-§4.2.3: Age header` | Age value used |
| 9111-4.2-009 | Current age from response delay | MUST | ✅ | `RFC-9111-§4.2.3: response delay` | Delay calculated |
| 9111-4.2-010 | Fresh: lifetime > age | MUST | ✅ | `RFC-9111-§4.2: IsFresh=true` | Fresh |
| 9111-4.2-011 | Stale: lifetime ≤ age | MUST | ✅ | `RFC-9111-§4.2: IsFresh=false` | Stale |

### §4.3 Conditional Requests (Validation)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-4.3-001 | ETag → If-None-Match | MUST | ✅ | `RFC-9111-§4.3.1: ETag adds INM` | Header added |
| 9111-4.3-002 | Last-Modified → If-Modified-Since | MUST | ✅ | `RFC-9111-§4.3.1: LM adds IMS` | Header added |
| 9111-4.3-003 | Both ETag + LM → both headers | MUST | ✅ | `RFC-9111-§4.3.1: both` | Both headers |
| 9111-4.3-004 | No validators → no conditional headers | MUST | ✅ | `RFC-9111-§4.3.1: neither` | No headers |
| 9111-4.3-005 | Preserves original URI and method | MUST | ✅ | `RFC-9111-§4.3.1: preserves URI` | URI+method unchanged |
| 9111-4.3-006 | CanRevalidate false without validators | MUST | ✅ | `RFC-9111-§4.3.2: false` | false |
| 9111-4.3-007 | CanRevalidate true with ETag | MUST | ✅ | `RFC-9111-§4.3.2: true ETag` | true |
| 9111-4.3-008 | CanRevalidate true with LM | MUST | ✅ | `RFC-9111-§4.3.2: true LM` | true |
| 9111-4.3-009 | 304 merge: StatusCode=200 | MUST | ✅ | `RFC-9111-§4.3.4: merged 200` | StatusCode=200 |
| 9111-4.3-010 | 304 merge: cached body preserved | MUST | ✅ | `RFC-9111-§4.3.4: cached body` | Body from cache |
| 9111-4.3-011 | 304 merge: new headers override cached | MUST | ✅ | `RFC-9111-§4.3.4: header override` | ETag updated |
| 9111-4.3-012 | 304 merge: version preserved | MUST | ✅ | `RFC-9111-§4.3.4: version preserved` | Same version |

### §3 Cache Storage

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-3-001 | GET 200 with max-age is cacheable | MUST | ✅ | `RFC-9111-§3.1: GET 200` | Cacheable |
| 9111-3-002 | Cacheable status codes (200,203,204,300,301,308,404,410,414,501) | MUST | ✅ | `RFC-9111-§3.1: cacheable statuses` (Theory) | All cacheable |
| 9111-3-003 | 500 not cacheable by default | MUST | ✅ | `RFC-9111-§3.1: 500 false` | Not cacheable |
| 9111-3-004 | GET 200 stored | MUST | ✅ | `RFC-9111-§3: GET 200` | Stored |
| 9111-3-005 | POST 200 not stored (unsafe) | MUST | ✅ | `RFC-9111-§3: POST 200 false` | Not stored |
| 9111-3-006 | no-store on request | MUST | ✅ | `RFC-9111-§5.2.1.5: request no-store` | Not stored |
| 9111-3-007 | no-store on response | MUST | ✅ | `RFC-9111-§5.2.2.5: response no-store` | Not stored |
| 9111-3-008 | Put then Get returns cached entry | MUST | ✅ | `RFC-9111-§3: Put/Get` | Entry returned |
| 9111-3-009 | Empty store returns null | MUST | ✅ | `RFC-9111-§4: empty store` | null |

### §4.1 Vary Header Matching

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-4.1-001 | Different Vary field → cache miss | MUST | ✅ | `RFC-9111-§4.1: different Accept` | Miss |
| 9111-4.1-002 | Matching Vary field → cache hit | MUST | ✅ | `RFC-9111-§4.1: matching Accept` | Hit |
| 9111-4.1-003 | Vary: * → never matches | MUST | ✅ | `RFC-9111-§4.1: Vary: *` | Never matches |

### §4.4 Invalidation

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-4.4-001 | Invalidate removes entry | MUST | ✅ | `RFC-9111-§4.4: Invalidate` | Entry removed |
| 9111-4.4-002 | Unsafe method invalidates GET | MUST | ✅ | `RFC-9111-§4.4: POST invalidates` | GET entry removed |

### §3 LRU Eviction

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-3-lru-001 | LRU eviction when full | SHOULD | ✅ | `RFC-9111-§3: LRU eviction` | Oldest evicted |

### §4–5 Integration (Evaluate Pipeline)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| 9111-int-001 | Full cycle: store → fresh hit | MUST | ✅ | `RFC-9111-§4: PUT then GET fresh` | Fresh hit |
| 9111-int-002 | Stale entry → must revalidate | MUST | ✅ | `RFC-9111-§4: stale revalidate` | MustRevalidate |
| 9111-int-003 | must-revalidate + stale | MUST | ✅ | `RFC-9111-§5.2.2.8: must-revalidate` | MustRevalidate status |
| 9111-int-004 | Stale without must-revalidate | SHOULD | ✅ | `RFC-9111-§4.2: stale status` | Stale status |
| 9111-int-005 | no-cache on request forces revalidation | MUST | ✅ | `RFC-9111-§5.2.1.4: no-cache` | Forces revalidation |
| 9111-int-006 | only-if-cached + fresh → Fresh | MUST | ✅ | `RFC-9111-§5.2.1.7: only-if-cached` | Fresh |
| 9111-int-007 | max-stale tolerance | SHOULD | ✅ | `RFC-9111-§5.2.1.2: max-stale=300` | Accepts within tolerance |

---

## IO Layer — TCP Options and Client Provider

### TcpOptionsFactory

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| TCP-001 | HTTP default port 80 | MUST | ✅ | `TCP_001_Http_DefaultPort_80` | Port 80 |
| TCP-002 | HTTPS default port 443 + TLS | MUST | ✅ | `TCP_002_Https_DefaultPort_443` | TlsOptions returned |
| TCP-003 | HTTP explicit port | MUST | ✅ | `TCP_003_Http_ExplicitPort_8080` | Port 8080 |
| TCP-004 | HTTPS explicit port | MUST | ✅ | `TCP_004_Https_ExplicitPort_8443` | Port 8443 + TLS |
| TCP-005 | IPv4 literal → InterNetwork | MUST | ✅ | `TCP_005_IPv4Literal_InterNetwork` | AddressFamily correct |
| TCP-006 | IPv6 literal → InterNetworkV6 | MUST | ✅ | `TCP_006_IPv6Literal_InterNetworkV6` | AddressFamily correct |
| TCP-007 | Hostname → Unspecified | MUST | ✅ | `TCP_007_Hostname_Unspecified` | DNS resolution deferred |
| TCP-008 | ConnectTimeout propagated | MUST | ✅ | `TCP_008_ConnectTimeout_Propagated` | Timeout matches |
| TCP-009 | ReconnectInterval propagated | MUST | ✅ | `TCP_009_ReconnectInterval_Propagated` | Interval matches |
| TCP-010 | MaxReconnectAttempts propagated | MUST | ✅ | `TCP_010_MaxReconnectAttempts_Propagated` | Attempts match |
| TCP-011 | MaxFrameSize propagated | MUST | ✅ | `TCP_011_MaxFrameSize_Propagated` | Frame size matches |
| TCP-012 | HTTPS TLS callback propagated | MUST | ✅ | `TCP_012_Https_CallbackPropagated` | Callback set |
| TCP-013 | HTTP ignores TLS callback | MUST | ✅ | `TCP_013_Http_CallbackIgnored` | TcpOptions, not TlsOptions |
| TCP-014 | TLS TargetHost equals host | MUST | ✅ | `TCP_014_TlsOptions_TargetHost` | TargetHost matches |
| TCP-015 | WSS returns TlsOptions | MUST | ✅ | `TCP_015_Wss_ReturnsTlsOptions` | TlsOptions for wss:// |

### ClientManager Provider Selection

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| CLT-001 | TcpOptions → TcpClientProvider | MUST | ✅ | `CLT_001_TcpOptions_NoStreamProvider` | TcpClientProvider created |
| CLT-002 | TlsOptions → TlsClientProvider | MUST | ✅ | `CLT_002_TlsOptions_NoStreamProvider` | TlsClientProvider created |
| CLT-003 | StreamProvider overrides options type | MUST | ✅ | `CLT_003_StreamProviderSet` | StreamProvider used |

---

## Streams Layer — Akka.Streams Graph Stages

### RequestEnricherStage (ENR)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| ENR-001 | Null URI + BaseAddress → absolute | MUST | ✅ | `ENR_001_NullUri_WithBaseAddress` | BaseAddress root |
| ENR-002 | Relative URI combined with BaseAddress | MUST | ✅ | `ENR_002_RelativeUri_WithBaseAddress` | Combined URI |
| ENR-003 | Absolute URI unchanged | MUST | ✅ | `ENR_003_AbsoluteUri_NotChanged` | No modification |
| ENR-004 | Null URI + null BaseAddress → error | MUST | ✅ | `ENR_004_NullUri_NullBaseAddress_Fails` | InvalidOperationException |
| ENR-005 | Relative URI + null BaseAddress → error | MUST | ✅ | `ENR_005_RelativeUri_NullBaseAddress` | InvalidOperationException |
| ENR-006 | Default version override (1.1→2.0) | MUST | ✅ | `ENR_006_DefaultVersion_11_DefaultIs20` | Version becomes 2.0 |
| ENR-007 | Default version identity (1.1→1.1) | MUST | ✅ | `ENR_007_DefaultVersion_11_DefaultIs11` | Unchanged |
| ENR-008 | Explicit 1.0 not overridden | MUST | ✅ | `ENR_008_ExplicitV10_NotOverridden` | Stays 1.0 |
| ENR-009 | Explicit 2.0 not overridden | MUST | ✅ | `ENR_009_ExplicitV20_NotOverridden` | Stays 2.0 |
| ENR-010 | Default headers merged | MUST | ✅ | `ENR_010_DefaultHeader_Merged` | Header present |
| ENR-011 | Existing request header not overridden | MUST | ✅ | `ENR_011_RequestHeaderNotOverridden` | Original kept |
| ENR-012 | Multiple default headers merged | MUST | ✅ | `ENR_012_TwoDefaultHeaders_BothMerged` | Both present |
| ENR-013 | Empty defaults → no change | MUST | ✅ | `ENR_013_EmptyDefaults_NoHeadersAdded` | Unchanged |
| ENR-014 | Case-insensitive header dedup | MUST | ✅ | `ENR_014_HeaderCaseInsensitive` | Not doubled |
| ENR-015 | Multi-value default header | SHOULD | ✅ | `ENR_015_MultipleValuesForOneName` | All values added |
| ENR-016 | Sequential requests enriched independently | MUST | ✅ | `ENR_016_ThreeRequests_AllEnriched` | Order preserved |

### HostRoutingStage (HRS)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| HRS-001 | HTTP URI → TcpOptions pool | MUST | ✅ | `HRS_001_HttpUri_CreatesTcpOptions` | TcpOptions |
| HRS-002 | HTTPS URI → TlsOptions pool | MUST | ✅ | `HRS_002_HttpsUri_CreatesTlsOptions` | TlsOptions |
| HRS-003 | ConnectTimeout propagated to pool | MUST | ✅ | `HRS_003_ClientOptionsConnectTimeoutPropagated` | 20s |
| HRS-004 | Same host:port:scheme reuses pool | MUST | ✅ | `HRS_004_SameHostPortScheme_ReusesPool` | Single pool |
| HRS-005 | Different hosts → separate pools | MUST | ✅ | `HRS_005_DifferentHosts_CreatesSeparatePools` | Two pools |
| HRS-006 | Same host, different scheme → separate | MUST | ✅ | `HRS_006_SameHostDifferentScheme` | Two pools |

### HostConnectionPool (ST-POOL)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| ST-POOL-001 | HTTP/1.0 through pool | MUST | ✅ | `Http10_Request_Through_Pool` | Correct status+version |
| ST-POOL-002 | HTTP/1.1 through pool | MUST | ✅ | `Http11_Request_Through_Pool` | Correct status+version |
| ST-POOL-003 | HTTP/2.0 through pool | MUST | ✅ | `Http20_Request_Through_Pool` | Correct status+version |
| ST-POOL-004 | Mixed-version batch | MUST | ✅ | `Mixed_Version_Batch_Via_Pool` | Version matches request |
| ST-POOL-005 | HTTP/1.0 routing isolation | MUST | ✅ | `Http10_Bytes_Only_Reach_Http10` | Correct connection |
| ST-POOL-006 | HTTP/1.1 routing isolation | MUST | ✅ | `Http11_Bytes_Only_Reach_Http11` | Correct connection |
| ST-POOL-007 | HTTP/2.0 routing isolation | MUST | ✅ | `Http20_Bytes_Only_Reach_Http20` | Correct connection |
| ST-POOL-008 | Backpressure (256 requests) | MUST | ✅ | `Backpressure_Queue_256` | No deadlock |

### PrependPrefaceStage (HTTP/2 Connection Preface)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| ST-20-PRE-001 | Connection preface magic (24 bytes) | MUST | ✅ | `ST_20_PRE_001` | Exact bytes match |
| ST-20-PRE-002 | SETTINGS frame after magic | MUST | ✅ | `ST_20_PRE_002` | type=0x4, stream=0 |
| ST-20-PRE-003 | Pass-through after preface | MUST | ✅ | `ST_20_PRE_003` | Data unchanged |
| ST-20-PRE-004 | Preface emitted once only | MUST | ✅ | `ST_20_PRE_004` | Not repeated |

### Request2FrameStage (HTTP/2 Request Encoding)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| ST-20-REQ-001 | HEADERS frame with :method | MUST | ✅ | `ST_20_REQ_001` | Pseudo-header present |
| ST-20-REQ-002 | All four pseudo-headers | MUST | ✅ | `ST_20_REQ_002` | :method :path :scheme :authority |
| ST-20-REQ-003 | Stream IDs odd and ascending | MUST | ✅ | `ST_20_REQ_003` | 1, 3, 5... |
| ST-20-REQ-004 | POST → HEADERS + DATA | MUST | ✅ | `ST_20_REQ_004` | Two frames |
| ST-20-REQ-005 | GET → END_STREAM on HEADERS | MUST | ✅ | `ST_20_REQ_005` | Flag set |

### TurboClientStreamManager (MGR)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| MGR-001 | Manager creates successfully | MUST | ✅ | `MGR_001_ManagerCreatesSuccessfully` | Non-null channels |
| MGR-002 | Request enriched with BaseAddress | MUST | ✅ | `MGR_002_RequestEnrichedWithBaseAddress` | Absolute URI |
| MGR-003 | Response callback → channel | MUST | ✅ | `MGR_003_ResponseCallback` | Response appears |
| MGR-004 | Bounded channel backpressure | MUST | ✅ | `MGR_004_BoundedChannel_TryWriteFalse` | TryWrite false when full |

### TurboHttpClient (CLI)

| Test ID | Requirement | Priority | Covered? | Covered By Test | Expected Result |
|---------|-------------|----------|----------|-----------------|-----------------|
| CLI-001 | Single request → response | MUST | ✅ | `CLI_001_SingleRequest_ReturnsResponse` | Response returned |
| CLI-002 | BaseAddress applied to pipeline | MUST | ✅ | `CLI_002_BaseAddress_Applied` | Absolute URI at pool |
| CLI-003 | DefaultRequestVersion applied | MUST | ✅ | `CLI_003_DefaultRequestVersion` | Correct version |
| CLI-004 | DefaultRequestHeaders merged | MUST | ✅ | `CLI_004_DefaultRequestHeaders` | Header present |
| CLI-005 | Explicit headers not overridden | MUST | ✅ | `CLI_005_ExplicitHeaders` | Original preserved |
| CLI-006 | Timeout → TimeoutException | MUST | ✅ | `CLI_006_Timeout` | Exception thrown |
| CLI-007 | Cancellation → TaskCanceledException | MUST | ✅ | `CLI_007_CancellationToken` | Exception thrown |
| CLI-008 | Sequential requests complete in order | MUST | ✅ | `CLI_008_FiveSequentialRequests` | All complete |
| CLI-009 | Concurrent requests complete | MUST | ✅ | `CLI_009_TenConcurrentRequests` | All complete |
| CLI-010 | CancelPendingRequests cancels inflight | MUST | ✅ | `CLI_010_CancelPendingRequests` | OperationCanceledException |
| CLI-011 | New request after cancel works | MUST | ✅ | `CLI_011_AfterCancelPendingRequests` | Normal operation |

---

## Coverage Summary

| RFC / Layer | MUST Count | MUST Covered | SHOULD Count | SHOULD Covered | Overall |
|-------------|------------|--------------|--------------|----------------|---------|
| RFC 1945 | 27 | 24 ✅, 3 N/A | 6 | 5 ✅, 1 N/A | 100% (N/A excluded) |
| RFC 7230 | 34 | 32 ✅, 2 N/A | 6 | 6 ✅ | 100% (N/A excluded) |
| RFC 7231 §6.1 | 6 | 6 ✅ | 2 | 2 ✅ | 100% |
| RFC 7231 §7.1.1 | 4 | 0 ❌ (out of scope) | 0 | — | N/A (deferred) |
| RFC 7233 §2.1 | 4 | 3 ✅, 1 N/A | 1 | 1 ✅ | 100% (N/A excluded) |
| RFC 7233 §4.1 | 2 | 2 ✅ | 2 | 2 ✅ | 100% |
| RFC 7540 | 45 | 43 ✅, 2 ⚠ | 8 | 4 ✅, 4 ⚠ | 96% (MUST) |
| RFC 7541 | 25 | 25 ✅ | 0 | — | 100% |
| RFC 9110 §8.4 | 30 | 30 ✅ | 5 | 5 ✅ | 100% |
| RFC 9111 | 51 | 51 ✅ | 5 | 5 ✅ | 100% |
| IO Layer | 18 | 18 ✅ | 0 | — | 100% |
| Streams Layer | 55 | 55 ✅ | 1 | 1 ✅ | 100% |

### Known Remaining Gaps

| Gap ID | RFC Section | Requirement | Priority | Status | Notes |
|--------|-------------|-------------|----------|--------|-------|
| GAP-001 | RFC 7231 §7.1.1 | Date/time format parsing | MUST | Deferred | Not part of protocol layer; raw string passed through |
| GAP-002 | RFC 7540 §6.1 | DATA frame PADDED flag | SHOULD | ⚠ Partial | Padding stripped at frame level; not unit-tested explicitly |
| GAP-003 | RFC 7540 §6.2 | HEADERS frame PADDED/PRIORITY flags | SHOULD | ⚠ Partial | Frame-level handling; higher-level tests don't isolate |
| GAP-004 | RFC 7540 §5.1 | Push-promise stream reserved state | MUST | ⚠ Partial | PUSH_PROMISE decoded in RT-2-013; full state machine not verified |
| GAP-005 | Streams | StreamIdAllocator stage | SHOULD | ⚠ Partial | Covered indirectly via `ST_20_REQ_003`; no dedicated unit tests |
| GAP-006 | Streams | CorrelationHttp1XStage | SHOULD | ❌ | No dedicated tests; covered indirectly via pool routing |
| GAP-007 | Streams | CorrelationHttp20Stage | SHOULD | ❌ | No dedicated tests; covered indirectly via pool routing |
| GAP-008 | Streams | ExtractOptionsStage | SHOULD | ❌ | No dedicated tests |

---

## Test Execution Matrix

### Priority Levels

- **P0 (Critical):** Must pass before any release
- **P1 (High):** Should pass for production release
- **P2 (Medium):** Nice to have, can be addressed later
- **P3 (Low):** Optional features, future enhancement

### Test Execution Plan

| Phase | RFC Section | Test Count | Priority |
|-------|-------------|------------|----------|
| 1 | RFC 7230 §3.1 (Request-Line) | 8 | P0 |
| 1 | RFC 7230 §3.2 (Headers) | 8 | P0 |
| 1 | RFC 7230 §3.3 (Message Body) | 7 | P0 |
| 1 | RFC 7230 §4.1 (Chunked) | 8 | P0 |
| 1 | RFC 7231 §6.1 (Status Codes) | 8 | P0 |
| 2 | RFC 7233 (Range Requests) | 10 | P1 |
| 3 | RFC 7540 §4.1 (Frame Format) | 7 | P0 |
| 3 | RFC 7540 §5.1 (Stream States) | 8 | P0 |
| 3 | RFC 7540 §5.2 (Flow Control) | 8 | P0 |
| 3 | RFC 7540 §6.x (Frame Types) | 30+ | P0 |
| 3 | RFC 7541 (HPACK) | 20+ | P0 |
| 4 | RFC 9110 §8.4 (Content Encoding) | 41 | P0 |
| 4 | RFC 9111 (HTTP Caching) | 67 | P1 |
| 4 | RFC 7540 §8.2 (Server Push) | 5 | P2 |
| 5 | IO Layer (TcpOptions + ClientManager) | 18 | P0 |
| 5 | Streams Layer (Stages + Pool + Client) | 56 | P0 |

---

## Coverage Goals

### RFC Compliance

- **MUST requirements:** 100% coverage (excluding out-of-scope server-side requirements)
- **SHOULD requirements:** >= 90% coverage
- **MAY requirements:** >= 50% coverage

### Code Coverage

- **Line coverage:** >= 90%
- **Branch coverage:** >= 85%
- **Mutation coverage:** >= 75% (optional, for critical paths)

---

**Last updated:** 2026-03-10
**Total tests tracked in matrix:** 301 requirements across 8 RFCs + IO + Streams layers
**Total test methods in codebase:** ~2,660 (2,152 unit + 128 stream + ~380 integration)
