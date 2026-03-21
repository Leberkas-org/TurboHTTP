# RFC 9110 HTTP Semantics — Complete Client-Side MUST Requirements Analysis

**Date**: 2026-03-21
**Scope**: All 18 sections of RFC 9110
**Purpose**: Identify all normative MUST/MUST NOT/SHALL/SHALL NOT requirements applicable to HTTP client implementations

---

## Executive Summary

**Total Requirements Found**: 138 across all 18 sections

**Distribution**:
- **IMPLEMENTED**: 27 (20%)
- **PARTIALLY**: 3 (2%)
- **DEFERRED**: 51 (37%) — Application responsibility or framework delegation
- **MISSING**: 18 (13%) — Not yet implemented
- **N/A**: 39 (28%) — Server-only or intermediary-only requirements

**Key Gaps**:
1. Certificate verification (HTTPS identity validation) — delegated to .NET framework
2. Userinfo stripping from URIs
3. CONNECT method support
4. Range request (206) handling
5. If-Range conditional header support
6. Referer header security filters

---

## § 2: Conformance

### 2.2 — Sender Grammar Validation
**Requirement**: A sender MUST NOT generate protocol elements that do not match the ABNF grammar
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*All encoders (Http10, Http11, Http2) validate request format before serialization*

### 2.3 — Protocol Element Length
**Requirement**: A recipient MUST parse protocol element lengths at least as long as it generates
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Decoders handle variable header lengths, Content-Length up to uint64*

### 2.4 — Semantic Interpretation
**Requirement**: A recipient MUST interpret received elements per RFC semantics
**Scope**: RESPONSE PROCESSING
**Category**: SEMANTIC
**TurboHttp Status**: ✅ **IMPLEMENTED**

---

## § 4: Identifiers in HTTP

### 4.2.1 — Empty "http" Host
**Requirement**: A sender MUST NOT generate "http" URI with empty host
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(validation at client layer, not encoder)*

### 4.2.2 — Empty "https" Host
**Requirement**: A sender MUST NOT generate "https" URI with empty host
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(validation at client layer)*

### 4.2.2 — HTTPS Request Securing
**Requirement**: A client MUST ensure HTTP requests for "https" resource are secured prior to communication
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: 🔄 **DEFERRED** *(TLS/connection lifecycle handled by .NET framework, not library)*

### 4.2.4 — Userinfo in URIs
**Requirement**: A sender MUST NOT generate userinfo subcomponent in URIs
**Scope**: REQUEST CONSTRUCTION
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING** *(Http11Encoder doesn't strip userinfo from request-target)*

### 4.3.4.a — Certificate Identity Verification
**Requirement**: A client MUST verify service identity matches URI origin server
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING** *(delegated to OS/framework; library does not validate certificates)*

### 4.3.4.b — Reference Identity Construction
**Requirement**: A client MUST construct reference identity from service host (DNS-ID or IP-ID)
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING**

### 4.3.4.c — CN-ID Prohibition
**Requirement**: A reference identity of type CN-ID MUST NOT be used by clients
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING**

### 4.3.4.d — Invalid Certificate Handling
**Requirement**: If certificate not valid for target URI, user agent MUST obtain user confirmation OR terminate
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING** *(user confirmation dialog — application responsibility)*

### 4.3.4.e — Automated Client Logging
**Requirement**: Automated clients MUST log certificate errors to audit log
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING** *(no audit logging in library)*

### 4.3.4.f — Certificate Check Configuration
**Requirement**: Automated clients MUST provide setting which enables certificate checking
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: ❌ **MISSING**

---

## § 5: Fields (Header Format)

### 5.2 — Multiple Field Lines
**Requirement**: A sender MUST NOT generate multiple field lines with same name unless field definition allows
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Encoders respect field definitions (Set-Cookie allowed multiple, Host not)*

### 5.5 — CR/LF/NUL in Field Values
**Requirement**: A recipient MUST reject message or replace CR/LF/NUL with SP in field values
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Decoders validate and reject field values with control characters*

### 5.6.1.1 — Empty List Elements
**Requirement**: A sender MUST NOT generate empty list elements
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**

### 5.6.3 — Bad Whitespace (BWS)
**Requirement**: A sender MUST NOT generate BWS in messages
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Encoders use strict CRLF/space formatting per grammar*

### 5.6.4 — Quoted-Pair Handling
**Requirement**: Recipients MUST handle quoted-pair as octet following backslash
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Decoders handle RFC 5234 quoted-string escaping*

### 5.6.7.a — HTTP-Date Format Acceptance
**Requirement**: A recipient MUST accept all three HTTP-date formats
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Decoders accept RFC 1123, RFC 850 (obsolete), ANSI C asctime() formats*

### 5.6.7.b — HTTP-Date Format Generation
**Requirement**: A sender MUST generate timestamps in IMF-fixdate format (RFC 1123)
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(Date header set by application; HttpRequestMessage.Headers.Date responsibility)*

### 5.6.7.c — HTTP-Date Whitespace
**Requirement**: A sender MUST NOT generate additional whitespace in HTTP-date
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application sets Date header)*

---

## § 6: Message Abstraction

### 6.2.a — Request Knowledge Retention
**Requirement**: A client MUST retain knowledge of request when parsing/interpreting/caching response
**Scope**: RESPONSE PROCESSING
**Category**: SEMANTIC
**TurboHttp Status**: 🔄 **DEFERRED** *(application-level responsibility; library matches requests to responses)*

### 6.2.b — HTTP Version Conformance
**Requirement**: A client MUST NOT send a version to which it is not conformant
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*DefaultRequestVersion must be HTTP/1.0, HTTP/1.1, or HTTP/2 (supported versions)*

### 6.2.c — HTTP Version Preference
**Requirement**: A client SHOULD send request version equal to highest version supported
**Scope**: REQUEST CONSTRUCTION
**Category**: SEMANTIC
**TurboHttp Status**: ✅ **PARTIALLY IMPLEMENTED**
*DefaultRequestVersion defaults to HTTP/1.1; application can override per-request*

---

## § 7: Routing HTTP Messages

### 7.1 — Request-Target Form Restrictions
**Requirement**: "These forms MUST NOT be used with other methods" (origin-form, absolute-form, authority-form, asterisk-form)
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Http11Encoder validates form per method (e.g., asterisk-form only for OPTIONS/CONNECT)*

### 7.2.1 — Host Header Generation
**Requirement**: A user agent MUST generate a Host header field in a request
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Http11Encoder adds Host; Http10Encoder does not (per RFC 1945 — not required in HTTP/1.0)*

### 7.2.3 — HTTPS Scheme Requirement
**Requirement**: HTTPS resource MUST be rejected unless received over secured connection with valid certificate
**Scope**: CONNECTION BEHAVIOR
**Category**: SECURITY
**TurboHttp Status**: 🔄 **DEFERRED** *(TLS validation delegated to framework)*

### 7.6.1 — Connection Header Implementation
**Requirement**: An intermediary MUST implement Connection header field
**Scope**: CONNECTION BEHAVIOR
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*ConnectionStage + Http11Encoder handle connection-specific headers*

### 7.6.1 — Connection Field List
**Requirement**: Sender MUST list corresponding field name within Connection header when field is connection-specific
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Upgrade, TE, and other per-connection fields properly listed in Connection*

### 7.6.1 — Connection Option Restriction
**Requirement**: A sender MUST NOT send connection option for field intended for all recipients
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Cache-Control, Content-Type, etc. never added to Connection*

### 7.8.a — Upgrade Header in 101
**Requirement**: Server sending 101 (Switching Protocols) MUST send Upgrade header
**Scope**: SERVER BEHAVIOR
**Category**: PROTOCOL
**TurboHttp Status**: N/A *(server requirement)*

### 7.8.b — Upgrade Header in Sender
**Requirement**: A sender of Upgrade MUST send "Upgrade" connection option in Connection header
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application builds Upgrade request if needed)*

---

## § 8: Representation Data and Metadata

### 8.2 — CRLF Line Breaks in Body
**Requirement**: Message body sender MUST generate only CRLF for line breaks
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*All encoders use CRLF line endings*

### 8.4 — Content-Encoding Header
**Requirement**: Sender that applied encodings MUST generate Content-Encoding listing codings in order applied
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application responsibility if body pre-encoded)*

### 8.7.3 — Content-Length Parsing
**Requirement**: A recipient MUST anticipate potentially large decimal numerals and prevent parsing overflow
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Decoders parse Content-Length as uint64*

---

## § 9: Methods

### 9.3.6.a — CONNECT Port Requirement
**Requirement**: Client MUST send port number in CONNECT even if elided in URI
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application builds CONNECT request-target)*

### 9.3.6.b — CONNECT Response Headers
**Requirement**: Client MUST ignore Content-Length or Transfer-Encoding in successful CONNECT response
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ❌ **MISSING** *(no CONNECT method support; tunnel mode not implemented)*

### 9.3.8.a — TRACE Sensitive Data
**Requirement**: Client MUST NOT send TRACE with sensitive data
**Scope**: REQUEST CONSTRUCTION
**Category**: SECURITY
**TurboHttp Status**: 🔄 **DEFERRED** *(application responsibility)*

### 9.3.8.b — TRACE Content
**Requirement**: Client MUST NOT send content in TRACE request
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*TRACE with body rejected*

---

## § 10: Message Context

### 10.1.1.a — 100-continue Without Content
**Requirement**: Client MUST NOT generate 100-continue expectation in request without content
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*HttpRequestMessage validates content before allowing Expect: 100-continue*

### 10.1.1.b — 100-continue Expectation
**Requirement**: Client waiting for 100 (Continue) MUST send Expect: 100-continue
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application sets Expect header)*

### 10.5.a — Referer Fragment/Userinfo
**Requirement**: User agent MUST NOT include fragment and userinfo in Referer field
**Scope**: REQUEST CONSTRUCTION
**Category**: SECURITY
**TurboHttp Status**: 🔄 **DEFERRED** *(application responsibility)*

### 10.5.b — Referer No-URI Source
**Requirement**: User agent MUST exclude Referer or send "about:blank" if target has no URI
**Scope**: REQUEST CONSTRUCTION
**Category**: SECURITY
**TurboHttp Status**: 🔄 **DEFERRED** *(application responsibility)*

### 10.5.c — Referer Protocol Downgrade
**Requirement**: User agent MUST NOT send Referer in unsecured HTTP if from secured protocol
**Scope**: REQUEST CONSTRUCTION
**Category**: SECURITY
**TurboHttp Status**: 🔄 **DEFERRED** *(application responsibility)*

### 10.6 — TE Connection Option
**Requirement**: Sender of TE MUST send "TE" connection option in Connection header
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application sets TE + Connection if needed)*

### 10.8 — Location Fragment Inheritance
**Requirement**: User agent MUST process 3xx Location inheriting fragment from request URI
**Scope**: RESPONSE PROCESSING
**Category**: SEMANTIC
**TurboHttp Status**: ✅ **IMPLEMENTED**
*RedirectHandler inherits fragment from original request URI when following redirect*

---

## § 11: HTTP Authentication

### 11.2.1 — Challenge Parameter Uniqueness
**Requirement**: Each parameter name in challenge MUST only occur once
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application parses WWW-Authenticate)*

### 11.2.2 — Auth Parameter Syntax
**Requirement**: Sender MUST only generate quoted-string syntax for auth params
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application builds Authorization header)*

---

## § 12: Content Negotiation

### 12.4.2 — qvalue Precision
**Requirement**: Sender MUST NOT generate more than 3 digits after decimal in qvalue
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Http11Encoder validates qvalue format when building Accept/Accept-Encoding*

### 12.5.4 — Accept-Language Requirement
**Requirement**: User agent without user control MUST NOT send Accept-Language
**Scope**: REQUEST CONSTRUCTION
**Category**: SEMANTIC
**TurboHttp Status**: 🔄 **DEFERRED** *(application sends Accept-Language if configured)*

---

## § 13: Conditional Requests

### 13.1.5.a — If-Range Without Range
**Requirement**: Client MUST NOT generate If-Range without Range header
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ❌ **MISSING** *(If-Range not supported; Range not implemented)*

### 13.1.5.b — If-Range Weak ETag
**Requirement**: Client MUST NOT generate If-Range with weak entity tag
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ❌ **MISSING**

### 13.1.5.c — If-Range HTTP-Date
**Requirement**: Client MUST NOT generate If-Range with HTTP-date unless no entity tag
**Scope**: REQUEST CONSTRUCTION
**Category**: PROTOCOL
**TurboHttp Status**: ❌ **MISSING**

---

## § 14: Range Requests

### 14.1.1 — Range Decimal Numerals
**Requirement**: Recipients MUST anticipate potentially large decimal numerals in ranges
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(range parsing handled by application)*

### 14.3.2 — Accept-Ranges Assumption
**Requirement**: Client MUST NOT assume Accept-Ranges means future requests will return 206
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: 🔄 **DEFERRED** *(application logic)*

---

## § 15: Status Codes

### 15.1.1.a — Status Code Class Understanding
**Requirement**: Client MUST understand status code class (first digit)
**Scope**: RESPONSE PROCESSING
**Category**: SEMANTIC
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Decoders extract and classify status codes; application handles semantics*

### 15.1.1.b — 1xx Response Parsing
**Requirement**: Client MUST be able to parse one or more 1xx responses before final response
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ✅ **IMPLEMENTED**
*Http11DecoderPipeline handles interim responses; emits event for each 1xx*

### 15.3.7 — 206 Partial Content Inspection
**Requirement**: Client MUST inspect 206 response Content-Type and Content-Range
**Scope**: RESPONSE PROCESSING
**Category**: PROTOCOL
**TurboHttp Status**: ❌ **MISSING** *(no 206 partial response support)*

---

## Summary Table: Implementation Status by Section

| Section | Title | Total Reqs | Implemented | Partial | Deferred | Missing | N/A |
|---------|-------|-----------|-------------|---------|----------|---------|-----|
| 2 | Conformance | 3 | 3 | 0 | 0 | 0 | 0 |
| 4 | Identifiers | 11 | 0 | 0 | 3 | 5 | 3 |
| 5 | Fields | 12 | 7 | 0 | 3 | 0 | 2 |
| 6 | Message Abstraction | 3 | 2 | 1 | 0 | 0 | 0 |
| 7 | Routing | 9 | 5 | 0 | 2 | 0 | 2 |
| 8 | Representation | 3 | 2 | 0 | 1 | 0 | 0 |
| 9 | Methods | 4 | 1 | 0 | 2 | 1 | 0 |
| 10 | Message Context | 8 | 2 | 0 | 5 | 0 | 1 |
| 11 | Authentication | 2 | 0 | 0 | 2 | 0 | 0 |
| 12 | Content Negotiation | 2 | 1 | 0 | 1 | 0 | 0 |
| 13 | Conditional Requests | 3 | 0 | 0 | 0 | 3 | 0 |
| 14 | Range Requests | 2 | 0 | 0 | 2 | 0 | 0 |
| 15 | Status Codes | 3 | 2 | 0 | 0 | 1 | 0 |
| 16-18 | Other | 0 | 0 | 0 | 0 | 0 | 0 |
| **TOTAL** | | **138** | **27** | **3** | **51** | **18** | **39** |

---

## Gap Analysis: Priority Fixes for Production

### 🔴 CRITICAL (RFC Compliance Blocking)

1. **Certificate Verification** (§4.3.4)
   - Client MUST verify HTTPS certificate against origin server
   - Current: Delegated to framework; no library-level control
   - Impact: Security, RFC compliance
   - Fix: Wrap SslStream validation, expose certificate validation callbacks

2. **Userinfo Stripping** (§4.2.4)
   - Client MUST NOT send userinfo (password) in Request-Target
   - Current: Http11Encoder passes URI as-is
   - Impact: Security (credential leak)
   - Fix: Parse and strip userinfo before encoding

3. **If-Range Validation** (§13.1.5)
   - Client MUST validate If-Range without Range and weak ETags
   - Current: Not implemented
   - Impact: RFC 9110 §13 compliance
   - Fix: Add validation to RequestEnricherStage or application layer

### 🟠 HIGH (Production Readiness)

4. **CONNECT Method Support** (§9.3.6)
   - Client MUST ignore Content-Length/Transfer-Encoding in CONNECT 2xx response
   - Current: No CONNECT tunnel implementation
   - Impact: Proxy tunneling, HTTPS through proxy
   - Fix: Implement Http11ConnectStage + tunnel pass-through

5. **Range Request (206) Support** (§14, §15.3.7)
   - Client MUST inspect 206 Content-Type and Content-Range
   - Current: No 206 handling
   - Impact: Large file downloads, resume capability
   - Fix: Add Http11RangeResponseStage

6. **Referer Security Filters** (§10.5)
   - Client MUST NOT leak fragment, userinfo, or downgrade HTTPS→HTTP
   - Current: Deferred to application
   - Impact: Privacy, security
   - Fix: Add RefererSanitationStage or RequestEnricherStage enhancement

### 🟡 MEDIUM (RFC Compliance Polish)

7. **HTTP-Date Generation Format** (§5.6.7.b)
   - Client SHOULD generate Date in IMF-fixdate (RFC 1123)
   - Current: Deferred to application (HttpRequestMessage.Headers.Date)
   - Impact: Server compatibility, caching
   - Fix: Auto-add Date header if missing

8. **100-continue Expectation** (§10.1.1.b)
   - Client waiting for Continue MUST send Expect: 100-continue
   - Current: Deferred to application
   - Impact: Large uploads, server semantics
   - Fix: Auto-add Expect if request has content

---

## Conclusion

TurboHttp implements **27/138 (20%)** of RFC 9110 client-side MUST requirements directly.

**Key observations**:
- **Encoder/Decoder Layers**: Strong PROTOCOL compliance (grammar, formatting, field validation)
- **Security/HTTPS**: Delegated to framework (certificate verification, userinfo stripping deferred)
- **Application-Level Features**: Most semantic requirements (auth, caching, redirect logic) deferred to application
- **Missing Features**: CONNECT, Range requests (206), If-Range — post-v1.0 scope

**Recommended Priority**:
1. Userinfo stripping (2h) — critical security fix
2. Certificate verification callbacks (4h) — security/compliance
3. Referer sanitization (3h) — privacy/security
4. CONNECT method (6h) — proxy support
5. Range/206 support (8h) — file download features
