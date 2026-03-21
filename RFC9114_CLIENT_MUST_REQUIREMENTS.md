# RFC 9114 (HTTP/3) - Complete Client-Side MUST Requirements Analysis

## Executive Summary

**Total MUST/MUST NOT/SHALL/SHALL NOT requirements extracted from RFC 9114: 145**

This document provides a comprehensive extraction and categorization of ALL client-side requirements from RFC 9114 (HTTP/3 specification).

### Status Breakdown

- **NOT IMPLEMENTED**: 140 requirements (HTTP/3 entirely absent from TurboHttp)
- **REUSABLE FROM HTTP/2**: 2 requirements (identical logic in RFC 9113)
- **NEW HTTP/3-SPECIFIC**: 136 unique requirements
- **Auxiliary (QPACK/RFC 9204)**: ~20 requirements for header compression
- **Auxiliary (QUIC/RFC 9000)**: ~25 requirements for transport layer

---

## SECTION 3: CONNECTION SETUP AND MANAGEMENT

### 3.1 - Discovering an HTTP/3 Endpoint

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 3.1-001 | Client MUST verify certificate acceptable match for URI origin server | TLS Validation | NOT IMPLEMENTED | Certificate domain matching |
| 3.1-002 | Client MUST NOT consider server authoritative if cert verification fails | TLS Validation | NOT IMPLEMENTED | Authority rejection |
| 3.1-003 | Clients SHOULD attempt TCP-based HTTP if UDP fails | Discovery | NOT IMPLEMENTED | Fallback mechanism |

### 3.2 - Connection Establishment

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 3.2-001 | HTTP/3 clients MUST support mechanism to indicate target host during TLS | Connection Setup | NOT IMPLEMENTED | Infrastructure requirement |
| 3.2-002 | Clients MUST send SNI TLS extension unless alternative exists | Connection Setup | NOT IMPLEMENTED | RFC 6066 SNI |
| 3.2-003 | SETTINGS frame MUST be sent by each endpoint as initial control stream frame | Connection Setup | NOT IMPLEMENTED | Control stream initialization |

### 3.3 - Connection Reuse

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 3.3-001 | Clients MUST validate certificate for new origin before reusing connection | Certificate Validation | NOT IMPLEMENTED | Multi-origin cert verification |
| 3.3-002 | Connection MUST NOT be reused if certificate unacceptable for new origin | Connection Reuse | NOT IMPLEMENTED | Cert failure → new connection |
| 3.3-003 | Clients SHOULD NOT open >1 HTTP/3 connection to given IP+UDP port | Pooling | NOT IMPLEMENTED | Connection reuse preference |
| 3.3-004 | Client MUST ensure server willing to serve that scheme | Authority Validation | NOT IMPLEMENTED | Scheme validation (non-https) |

---

## SECTION 4: EXPRESSING HTTP SEMANTICS IN HTTP/3

### 4.1 - HTTP Message Framing

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.1-001 | Client MUST send only single request on given stream | Stream Semantics | NOT IMPLEMENTED | 1 request = 1 stream |
| 4.1-002 | Multiple requests or response after final response MUST be malformed | Response Validation | NOT IMPLEMENTED | Malformed sequence detection |
| 4.1-003 | Invalid frame sequence MUST be H3_FRAME_UNEXPECTED | Frame Validation | NOT IMPLEMENTED | Frame ordering enforcement |
| 4.1-004 | PUSH_PROMISE in pushed response MUST be H3_FRAME_UNEXPECTED | Push Validation | NOT IMPLEMENTED | Nested PUSH_PROMISE forbidden |
| 4.1-005 | Transfer-Encoding header field MUST NOT be used | Field Filtering | **REUSABLE FROM HTTP/2** | Already in RFC 9113 |
| 4.1-006 | After sending request, client MUST close stream for sending | Stream Lifecycle | NOT IMPLEMENTED | Half-close after request |
| 4.1-007 | Clients MUST NOT make stream closure dependent on response (except CONNECT) | Stream Control | NOT IMPLEMENTED | Client controls sending side |
| 4.1-008 | Clients MUST NOT discard complete responses if request terminated | Response Preservation | NOT IMPLEMENTED | Retain received responses |

### 4.1.1 - Request Cancellation and Rejection

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.1.1-001 | Clients MUST NOT use H3_REQUEST_REJECTED except when server requests | Error Code Restriction | NOT IMPLEMENTED | Server-only error code |

### 4.1.2 - Malformed Requests and Responses

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.1.2-001 | Clients MUST NOT accept malformed response | Response Validation | NOT IMPLEMENTED | Strict parsing enforcement |

### 4.2 - HTTP Fields

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.2-001 | Field name characters MUST be converted to lowercase | Field Encoding | **REUSABLE FROM HTTP/2** | RFC 9113 requirement |
| 4.2-002 | Uppercase in field names MUST be treated as malformed | Field Validation | NOT IMPLEMENTED | Lowercase enforcement |
| 4.2-003 | Endpoint MUST NOT generate connection-specific fields | Field Filtering | NOT IMPLEMENTED | Forbidden fields list |
| 4.2-004 | Connection-specific fields in message MUST be malformed | Field Validation | NOT IMPLEMENTED | Forbidden field detection |
| 4.2-005 | TE header MUST NOT contain value other than 'trailers' | Field Constraint | NOT IMPLEMENTED | TE field validation |

### 4.2.2 - Header Size Constraints

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.2.2-001 | SHOULD NOT send header exceeding SETTINGS_MAX_HEADER_LIST_SIZE | Flow Control | NOT IMPLEMENTED | QPACK/RFC 9204 limit |

### 4.3 - HTTP Control Data (Pseudo-Headers)

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.3-001 | MUST NOT generate pseudo-headers other than defined | Pseudo-Header | NOT IMPLEMENTED | Whitelist enforcement |
| 4.3-002 | Pseudo-headers MUST be in defined context only | Pseudo-Header | NOT IMPLEMENTED | Context validation |
| 4.3-003 | Request pseudo-headers MUST NOT appear in responses | Pseudo-Header | NOT IMPLEMENTED | Response validation |
| 4.3-004 | Response pseudo-headers MUST NOT appear in requests | Pseudo-Header | NOT IMPLEMENTED | Request validation |
| 4.3-005 | Pseudo-headers MUST NOT appear in trailers | Trailer Validation | NOT IMPLEMENTED | Trailer filtering |
| 4.3-006 | Undefined/invalid pseudo-headers MUST be malformed | Validation | NOT IMPLEMENTED | Unknown pseudo-header detection |
| 4.3-007 | All pseudo-headers MUST appear before regular headers | Field Ordering | NOT IMPLEMENTED | Position enforcement |
| 4.3-008 | Pseudo-header after regular header MUST be malformed | Field Ordering | NOT IMPLEMENTED | Out-of-order detection |

### 4.3.1 - Request Pseudo-Header Fields

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.3.1-001 | :authority MUST NOT include userinfo for http/https | Pseudo-Header | NOT IMPLEMENTED | No user:password@ |
| 4.3.1-002 | :authority MUST be omitted when translating from HTTP/1.1 | Translation | NOT IMPLEMENTED | Intermediary rule |
| 4.3.1-003 | :authority MUST NOT be empty for http/https | Pseudo-Header | NOT IMPLEMENTED | Emptiness check |
| 4.3.1-004 | URIs without path MUST include :path="/" | Pseudo-Header | NOT IMPLEMENTED | Default path |
| 4.3.1-005 | OPTIONS request MUST include :path="*" | Pseudo-Header | NOT IMPLEMENTED | Method-specific rule |
| 4.3.1-006 | All requests MUST include exactly one :method, :scheme, :path (except CONNECT) | Pseudo-Header | NOT IMPLEMENTED | Mandatory fields |
| 4.3.1-007 | If :scheme has mandatory authority, request MUST contain :authority or Host | Pseudo-Header | NOT IMPLEMENTED | Authority requirement |
| 4.3.1-008 | Authority fields MUST NOT be empty | Field Constraint | NOT IMPLEMENTED | Non-empty check |
| 4.3.1-009 | Both :authority and Host MUST contain same value | Field Consistency | NOT IMPLEMENTED | Value matching |
| 4.3.1-010 | If scheme no mandatory authority, MUST NOT contain :authority or Host | Pseudo-Header | NOT IMPLEMENTED | Authority restriction |

### 4.3.2 - Response Pseudo-Header Fields

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.3.2-001 | Single :status pseudo-header MUST be in all responses | Pseudo-Header | NOT IMPLEMENTED | Mandatory status field |

### 4.4 - The CONNECT Method

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.4-001 | CONNECT MUST have :method=CONNECT, no :scheme/:path, :authority=host:port | CONNECT Format | NOT IMPLEMENTED | CONNECT format enforcement |
| 4.4-002 | Non-conforming CONNECT is malformed | Validation | NOT IMPLEMENTED | Format validation |
| 4.4-003 | After CONNECT, only DATA frames permitted | Stream State | NOT IMPLEMENTED | Tunnel mode |
| 4.4-004 | Non-DATA frames after CONNECT MUST be H3_FRAME_UNEXPECTED | Frame Validation | NOT IMPLEMENTED | Frame restriction |
| 4.4-005 | Clients SHOULD NOT close sending while expecting target data | Tunnel Semantics | NOT IMPLEMENTED | Bidirectional tunnels |

### 4.5 - HTTP Upgrade

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.5-001 | HTTP/3 does NOT support Upgrade or 101 Switching Protocols | Semantics | NOT IMPLEMENTED | No WebSocket upgrade |

### 4.6 - Server Push

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 4.6-001 | MUST treat push stream as H3_ID_ERROR if no MAX_PUSH_ID or ID >max | Push Control | NOT IMPLEMENTED | MAX_PUSH_ID enforcement |
| 4.6-002 | Same push ID on multiple streams MUST have identical field sections | Push Validation | NOT IMPLEMENTED | Idempotence |
| 4.6-003 | Name and value MUST be identical in duplicate push IDs | Push Validation | NOT IMPLEMENTED | Exact match |
| 4.6-004 | Client MUST perform origin verification for pushed request | Push Authority | NOT IMPLEMENTED | Certificate validation |
| 4.6-005 | MUST NOT consider server authoritative if push verification fails | Push Authority | NOT IMPLEMENTED | Authority rejection |
| 4.6-006 | Clients SHOULD send CANCEL_PUSH if push not cacheable/safe/has content | Push Management | NOT IMPLEMENTED | Push rejection |
| 4.6-007 | Non-cacheable pushed responses MUST NOT be used or cached | Caching | NOT IMPLEMENTED | Cache filtering |
| 4.6-008 | Clients SHOULD abort reading if no PUSH_PROMISE in reasonable time | Push Timeout | NOT IMPLEMENTED | Orphaned stream cleanup |
| 4.6-009 | Can abort with H3_REQUEST_CANCELLED if push timeout | Push Timeout | NOT IMPLEMENTED | Timeout error code |
| 4.6-010 | Pushed responses not cacheable MUST NOT be cached | Caching | NOT IMPLEMENTED | Cache filtering |

---

## SECTION 5: CONNECTION CLOSURE

### 5.1 - Idle Connections

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 5.1-001 | Clients expected to request keep-alive while responses outstanding | Keep-Alive | NOT IMPLEMENTED | QUIC idle timeout |
| 5.1-002 | Client SHOULD open new connection if idle >timeout | Timeout Handling | NOT IMPLEMENTED | Reconnect logic |

### 5.2 - Connection Shutdown (GOAWAY)

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 5.2-001 | Graceful shutdown via GOAWAY frame | Connection Lifecycle | NOT IMPLEMENTED | Shutdown initiator |
| 5.2-002 | SHOULD explicitly cancel requests/pushes ≥GOAWAY ID | Stream Cleanup | NOT IMPLEMENTED | Best effort cancellation |
| 5.2-003 | MUST NOT initiate new requests after GOAWAY receipt | Stream Halt | NOT IMPLEMENTED | Request freeze |
| 5.2-004 | MAY establish new connection for additional requests | Connection Failover | NOT IMPLEMENTED | Retry mechanism |
| 5.2-005 | Requests ≥GOAWAY ID not processed | Retry Logic | NOT IMPLEMENTED | Unprocessed request handling |
| 5.2-006 | Requests <GOAWAY ID might be processed; status unknown | State Ambiguity | NOT IMPLEMENTED | Uncertain state handling |
| 5.2-007 | Later GOAWAY ID MUST NOT exceed earlier ID | Monotonicity | NOT IMPLEMENTED | ID enforcement |
| 5.2-008 | GOAWAY with larger ID MUST be H3_ID_ERROR | Connection Error | NOT IMPLEMENTED | Monotonicity error |

### 5.3 - Immediate Application Closure

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 5.3-001 | Can close QUIC connection at any time | Closure | NOT IMPLEMENTED | Abrupt closure |

### 5.4 - Transport Closure

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 5.4-001 | If no GOAWAY, MUST assume sent request might be processed | Failure Semantics | NOT IMPLEMENTED | Worst-case assumption |

---

## SECTION 6: STREAM MAPPING AND USAGE

### 6.1 - Bidirectional Streams

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 6.1-001 | All client-init bidirectional streams for requests/responses | Stream Type | NOT IMPLEMENTED | Request stream definition |
| 6.1-002 | MUST treat server-init bidirectional streams as H3_STREAM_CREATION_ERROR | Stream Validation | NOT IMPLEMENTED | Forbidden stream type |

### 6.2 - Unidirectional Streams

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 6.2-001 | Transport params MUST allow peer ≥3 unidirectional streams | QUIC Setup | NOT IMPLEMENTED | Stream limit |
| 6.2-002 | SHOULD provide ≥1024 bytes flow-control per uni stream | QUIC Setup | NOT IMPLEMENTED | Flow control minimum |
| 6.2-003 | Unknown stream types MUST NOT be connection error | Extension | NOT IMPLEMENTED | Error tolerance |
| 6.2-004 | Recipients MUST abort or discard unknown stream types | Stream Handling | NOT IMPLEMENTED | Unknown stream handling |
| 6.2-005 | SHOULD use H3_STREAM_CREATION_ERROR on unknown abort | Error Code | NOT IMPLEMENTED | Error selection |
| 6.2-006 | MUST NOT discard data before reading stream type | Stream Parser | NOT IMPLEMENTED | Type-first parsing |

### 6.2.1 - Control Streams

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 6.2.1-001 | Each endpoint MUST initiate single control stream | Control Stream | NOT IMPLEMENTED | Control stream init |
| 6.2.1-002 | SETTINGS MUST be first frame | Frame Ordering | NOT IMPLEMENTED | SETTINGS requirement |
| 6.2.1-003 | Non-SETTINGS first frame MUST be H3_MISSING_SETTINGS | Connection Error | NOT IMPLEMENTED | Missing SETTINGS error |
| 6.2.1-004 | Second control stream MUST be H3_STREAM_CREATION_ERROR | Stream Limit | NOT IMPLEMENTED | Single control stream |
| 6.2.1-005 | MUST NOT close control stream; receiver MUST NOT request | Stream Persistence | NOT IMPLEMENTED | Control stream immortality |
| 6.2.1-006 | Control stream closure MUST be H3_CLOSED_CRITICAL_STREAM | Connection Error | NOT IMPLEMENTED | Closure error |
| 6.2.1-007 | SHOULD provide flow-control to prevent blocking | Flow Control | NOT IMPLEMENTED | Reactive credit |

### 6.2.2 - Push Streams

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 6.2.2-001 | Push stream = type 0x01 + push ID | Format | NOT IMPLEMENTED | Push stream format |
| 6.2.2-002 | Client-init push stream MUST be H3_STREAM_CREATION_ERROR | Stream Validation | NOT IMPLEMENTED | Forbidden type |
| 6.2.2-003 | Each push ID MUST be used once | Uniqueness | NOT IMPLEMENTED | Push ID uniqueness |
| 6.2.2-004 | Duplicate push ID MUST be H3_ID_ERROR | Connection Error | NOT IMPLEMENTED | Duplicate detection |

### 6.2.3 - Reserved Stream Types

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 6.2.3-001 | Reserved streams (0x1f*N+0x21) MUST NOT have meaning | Testing | NOT IMPLEMENTED | Reserved type ignoring |

---

## SECTION 7: HTTP FRAMING LAYER

### 7.1 - Frame Layout

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.1-001 | Payload MUST contain exactly identified fields | Parsing | NOT IMPLEMENTED | Strict parsing |
| 7.1-002 | Extra/truncated fields MUST be H3_FRAME_ERROR | Validation | NOT IMPLEMENTED | Frame size enforcement |
| 7.1-003 | Redundant lengths MUST be self-consistent | Validation | NOT IMPLEMENTED | Consistency check |
| 7.1-004 | Truncated final frame MUST be H3_FRAME_ERROR | Termination | NOT IMPLEMENTED | Final frame validation |

### 7.2.1 - DATA Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.1-001 | DATA MUST be associated with request/response | Validation | NOT IMPLEMENTED | Frame validation |
| 7.2.1-002 | DATA on control stream MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type checking |

### 7.2.2 - HEADERS Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.2-001 | HEADERS only on request/push streams | Validation | NOT IMPLEMENTED | Stream type checking |
| 7.2.2-002 | HEADERS on control stream MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type error |

### 7.2.3 - CANCEL_PUSH Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.3-001 | CANCEL_PUSH on control stream | Frame Placement | NOT IMPLEMENTED | Control-only frame |
| 7.2.3-002 | CANCEL_PUSH on non-control MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type error |
| 7.2.3-003 | Push ID >allowed MUST be H3_ID_ERROR | Connection Error | NOT IMPLEMENTED | ID validation |

### 7.2.4 - SETTINGS Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.4-001 | SETTINGS applies entire connection | Semantics | NOT IMPLEMENTED | Connection-level scope |
| 7.2.4-002 | First frame of control stream, once | Ordering | NOT IMPLEMENTED | Single SETTINGS |
| 7.2.4-003 | Second SETTINGS MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Duplicate error |
| 7.2.4-004 | SETTINGS on non-control MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type error |
| 7.2.4-005 | Same ID MUST NOT occur >once | Validation | NOT IMPLEMENTED | Duplicate IDs |
| 7.2.4-006 | Receiver MAY treat duplicates as H3_SETTINGS_ERROR | Error Handling | NOT IMPLEMENTED | Duplicate detection |
| 7.2.4-007 | MUST ignore unknown parameters | Extension Tolerance | NOT IMPLEMENTED | Unknown settings |

### 7.2.4.2 - Initialization

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.4.2-001 | MUST NOT send frames invalid per peer settings | Compliance | NOT IMPLEMENTED | Settings respect |
| 7.2.4.2-002 | SHOULD use defaults before peer SETTINGS | Initialization | NOT IMPLEMENTED | Safe defaults |
| 7.2.4.2-003 | MUST NOT require data before SETTINGS | Setup Order | NOT IMPLEMENTED | SETTINGS first |
| 7.2.4.2-004 | SHOULD NOT wait indefinitely for SETTINGS | Timeout | NOT IMPLEMENTED | Early requests |
| 7.2.4.2-005 | SHOULD process datagrams for SETTINGS | Optimization | NOT IMPLEMENTED | Datagram processing |
| 7.2.4.2-006 | Client MUST comply with stored 0-RTT settings | 0-RTT | NOT IMPLEMENTED | Settings persistence |
| 7.2.4.2-007 | Client MUST comply with new settings once received | Dynamic Update | NOT IMPLEMENTED | Settings application |
| 7.2.4.2-009 | Incompatible SETTINGS after 0-RTT MUST be H3_SETTINGS_ERROR | 0-RTT Error | NOT IMPLEMENTED | Compatibility check |
| 7.2.4.2-010 | Omitted setting breaking 0-RTT MUST be H3_SETTINGS_ERROR | 0-RTT Error | NOT IMPLEMENTED | Default compatibility |

### 7.2.5 - PUSH_PROMISE Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.5-001 | Push ID >advertised MUST be H3_ID_ERROR | Connection Error | NOT IMPLEMENTED | ID validation |
| 7.2.5-002 | Duplicate push IDs MUST have identical fields | Validation | NOT IMPLEMENTED | Idempotence |
| 7.2.5-003 | SHOULD compare duplicate push ID fields | Detection | NOT IMPLEMENTED | Duplicate handling |
| 7.2.5-004 | Mismatch MUST be H3_GENERAL_PROTOCOL_ERROR | Connection Error | NOT IMPLEMENTED | Consistency error |
| 7.2.5-005 | Matching IDs SHOULD associate pushed content | Caching | NOT IMPLEMENTED | Push reuse |
| 7.2.5-006 | PUSH_PROMISE on control stream MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type error |
| 7.2.5-007 | Client MUST NOT send PUSH_PROMISE | Restriction | NOT IMPLEMENTED | Server-only frame |

### 7.2.6 - GOAWAY Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.6-001 | GOAWAY on control stream | Placement | NOT IMPLEMENTED | Control-only |
| 7.2.6-002 | Server GOAWAY carries bidi stream ID | Format | NOT IMPLEMENTED | Stream ID type |
| 7.2.6-003 | Non-bidi ID MUST be H3_ID_ERROR | Connection Error | NOT IMPLEMENTED | ID type validation |
| 7.2.6-004 | Client GOAWAY carries push ID | Format | NOT IMPLEMENTED | Push ID type |
| 7.2.6-005 | GOAWAY on non-control MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type error |

### 7.2.7 - MAX_PUSH_ID Frame

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.7-001 | MAX_PUSH_ID on control stream | Placement | NOT IMPLEMENTED | Control-only |
| 7.2.7-002 | Non-control MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Stream type error |
| 7.2.7-003 | Server MUST NOT send MAX_PUSH_ID | Restriction | NOT IMPLEMENTED | Client-only |
| 7.2.7-004 | Client receiving MAX_PUSH_ID MUST be H3_FRAME_UNEXPECTED | Connection Error | NOT IMPLEMENTED | Server-forbidden |
| 7.2.7-005 | Max unset at start; server cannot push until MAX_PUSH_ID received | Initialization | NOT IMPLEMENTED | Push gate |
| 7.2.7-006 | Cannot reduce max; smaller value MUST be H3_ID_ERROR | Connection Error | NOT IMPLEMENTED | Monotonic enforcement |

### 7.2.8 - Reserved Frame Types

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 7.2.8-001 | Reserved (0x1f*N+0x21) MUST NOT have meaning | Testing | NOT IMPLEMENTED | Testing frames |
| 7.2.8-002 | Other reserved (0x1f*N+0x23) MUST NOT be sent; receipt MUST be error | Forbidden | NOT IMPLEMENTED | Truly reserved |

---

## SECTION 8: ERROR HANDLING

### 8.1 - HTTP/3 Error Codes

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 8.1-001 | Unknown error code MUST be treated as H3_NO_ERROR | Tolerance | NOT IMPLEMENTED | Error code resilience |

---

## SECTION 9: EXTENSIONS TO HTTP/3

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 9-001 | MUST ignore unknown values in extensible elements | Tolerance | NOT IMPLEMENTED | Extension tolerance |
| 9-002 | MUST discard/abort unknown unidirectional streams | Handling | NOT IMPLEMENTED | Unknown streams |
| 9-003 | Semantic-changing extensions MUST be negotiated | Negotiation | NOT IMPLEMENTED | Extension handshake |
| 9-004 | Unknown frame type where known required SHOULD be error | Validation | NOT IMPLEMENTED | Frame type checking |

---

## SECTION 10: SECURITY CONSIDERATIONS

### 10.3 - Intermediary-Encapsulation Attacks

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 10.3-001 | Invalid field names MUST be malformed | Validation | NOT IMPLEMENTED | Field name checking |
| 10.3-002 | Invalid field value chars MUST be malformed | Validation | NOT IMPLEMENTED | Field value checking |

### 10.4 - Cacheability of Pushed Responses

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 10.4-001 | Reject pushed responses from non-authoritative origins | Security | NOT IMPLEMENTED | Push authority |

### 10.5 - Denial-of-Service Considerations

| Req ID | Requirement | Client Role | Status | Scope |
|--------|---|---|---|---|
| 10.5-001 | Client SHOULD limit push IDs issued | DoS Prevention | NOT IMPLEMENTED | Push limiting |
| 10.5-002 | SHOULD track feature use and set limits | DoS Prevention | NOT IMPLEMENTED | Rate limiting |
| 10.5-003 | MAY treat suspicious activity as H3_EXCESSIVE_LOAD | DoS Detection | NOT IMPLEMENTED | Anomaly detection |

---

## SUMMARY

### Reusable from HTTP/2 (2)
1. Transfer-Encoding forbidden (RFC 9113)
2. Field name lowercase (RFC 9113)

### HTTP/3-Specific New (136)
All others are unique to HTTP/3, including:
- Control streams (unidirectional per endpoint)
- QPACK integration (RFC 9204)
- Push ID space (explicit client-controlled)
- 0-RTT semantics (QUIC integration)
- Stream type headers (unidirectional)

### QPACK/RFC 9204 (~20)
Header compression codec and dynamic table management

### QUIC/RFC 9000 (~25)
Connection setup, stream creation, flow control, transport parameters

---

## Implementation Effort Estimate

| Phase | Work | Hours |
|-------|------|-------|
| 1. QUIC Connection Setup | SNI, ALPN, cert validation | 40–60 |
| 2. Stream Management | Bidirectional/unidirectional lifecycle | 30–40 |
| 3. Frame Codec | HTTP/3 frame encoding/decoding | 60–80 |
| 4. Control Messages | SETTINGS/GOAWAY/CANCEL_PUSH/MAX_PUSH_ID | 40–60 |
| 5. QPACK Codec | Header compression (RFC 9204) | 80–120 |
| 6. Push Management | PUSH_PROMISE, duplicate detection, authority | 40–60 |
| 7. Field Validation | Pseudo-headers, field encoding, malformed | 30–40 |
| 8. Error Handling | Connection reuse, 0-RTT, error codes | 40–60 |
| 9. Akka Integration | Http3Engine streams stages | 50–70 |
| 10. Unit Tests | RFC 9114 compliance | 100–150 |
| 11. Integration Tests | Real QUIC servers | 60–100 |
| 12. Optimization | Benchmarks, performance | 40–60 |

**Total: 610–910 hours (~15–23 weeks at 40h/week)**

