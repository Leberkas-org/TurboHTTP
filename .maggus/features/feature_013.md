# Feature 013: Security Audit — Comprehensive Test Coverage

## Introduction

Systematically audit and test all security-relevant code paths in TurboHttp. While some security tests already exist (HTTP/1.1 security tests, HTTP/2 security tests, HTTP/3 field validation), this feature fills the gaps and ensures comprehensive coverage across all attack vectors relevant to an HTTP client library.

### Architecture Context

- **Existing security tests:**
  - `RFC9112/26_Http11SecurityTests.cs` — 14 tests: header count/size limits, body size limits, CRLF injection, NUL bytes, TE+CL conflict
  - `RFC9112/25_Http11NegativePathTests.cs` — 22 tests: invalid status lines, chunked validation, smuggling vectors
  - `RFC9113/26_SecurityTests.cs` — 6 tests: CONTINUATION flood, RST_STREAM rapid reset (CVE-2023-44487), DATA flood, SETTINGS abuse
  - `Protocol/RFC9114/Http3FieldValidator.cs` — field name/value validation, connection-specific header blocking
  - `Protocol/RFC9110/UriSanitizer.cs` — userinfo stripping, IPv6 handling, fragment removal
- **Gaps to fill:** TLS validation tests, cookie security edge cases, URI path traversal, HPACK bomb, HTTP/1.1 encoder-side injection, cross-protocol header smuggling

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Achieve comprehensive security test coverage across all protocol layers
- Document each test's threat model (what attack it prevents)
- Verify existing security measures work correctly
- Add missing security validations where gaps are found

## Tasks

### TASK-013-001: Header Injection & Request Smuggling Tests
**Description:** As a security auditor, I want to verify that header injection and request smuggling attacks are prevented.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other 013 tasks

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/Security/HeaderInjectionTests.cs`
- [x] Tests cover:
  - CRLF injection in request header names → rejected or sanitized
  - CRLF injection in request header values → rejected or sanitized
  - NUL byte injection in headers → rejected
  - Header name with spaces → rejected
  - Header value with bare CR (without LF) → rejected
  - HTTP/1.1 request smuggling via conflicting Content-Length + Transfer-Encoding
  - HTTP/1.1 request smuggling via duplicate Content-Length values
  - HTTP/1.1 encoder does not emit smugglable headers
- [x] Each test documents the attack vector in its DisplayName
- [x] All tests pass

### TASK-013-002: TLS & Transport Security Tests
**Description:** As a security auditor, I want to verify TLS certificate validation and downgrade protection.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other 013 tasks

**Acceptance Criteria:**
- [ ] Test file `src/TurboHttp.Tests/Security/TlsSecurityTests.cs`
- [ ] Tests cover:
  - Default certificate validation rejects self-signed certs
  - Custom validation callback is invoked
  - HTTPS→HTTP redirect is blocked or flagged (cross-scheme protection)
  - Sensitive headers (Authorization, Cookie) stripped on scheme downgrade
  - TLS options (TargetHost, ClientCertificates) are passed to SslStream
- [ ] All tests pass

### TASK-013-003: Cookie Security Tests
**Description:** As a security auditor, I want to verify cookie security attribute enforcement.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other 013 tasks

**Acceptance Criteria:**
- [ ] Test file `src/TurboHttp.Tests/Security/CookieSecurityTests.cs`
- [ ] Tests cover:
  - Secure cookies NOT sent over HTTP
  - Secure cookies sent over HTTPS
  - HttpOnly flag stored correctly (informational — HttpOnly is server-enforced)
  - SameSite=Strict cookies not sent on cross-site requests
  - SameSite=Lax cookies sent on safe top-level navigations
  - Cookie domain scoping: subdomain cookie not sent to parent domain
  - Cookie path scoping: /foo cookie not sent to /bar
  - Cookie path traversal: /foo/.. should not match /
  - Cookie Max-Age=0 immediately deletes
  - Overlong cookie values handled (not crash/OOM)
- [ ] All tests pass

### TASK-013-004: URI Sanitization & Path Traversal Tests
**Description:** As a security auditor, I want to verify URI handling prevents path traversal and injection attacks.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other 013 tasks

**Acceptance Criteria:**
- [ ] Test file `src/TurboHttp.Tests/Security/UriSecurityTests.cs`
- [ ] Tests cover:
  - Path traversal in redirect Location (/../../../etc/passwd)
  - Fragment injection in URIs (stripped before sending)
  - Userinfo stripping (user:pass@host → host)
  - Unicode normalization attacks (homograph detection is out-of-scope, but encoding must be correct)
  - Double-encoding (%252F) passthrough (no double-decode)
  - Null bytes in URI components → rejected
  - Backslash in path (\\ vs /) handling
  - Extremely long URIs (>8KB) → handled gracefully
- [ ] All tests pass

### TASK-013-005: HPACK Bomb & Protocol Abuse Tests
**Description:** As a security auditor, I want to verify HPACK/QPACK implementations resist resource exhaustion attacks.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside all other 013 tasks

**Acceptance Criteria:**
- [ ] Test file `src/TurboHttp.Tests/Security/HpackBombTests.cs`
- [ ] Tests cover:
  - HPACK dynamic table size update to maximum (SETTINGS_HEADER_TABLE_SIZE) → bounded memory
  - HPACK bomb: small compressed input expanding to huge headers → size limit enforced
  - Huffman decoding of adversarial input → no infinite loop, bounded output
  - HPACK decoder with >100 entries in dynamic table → correct eviction
  - HPACK indexed header referencing out-of-bounds → HpackException
  - Zero-length header name via HPACK → rejected
  - QPACK: encoder instruction flooding → bounded table growth
  - QPACK: blocked stream limit enforcement
- [ ] All tests include memory assertions (no allocation > configured limit)
- [ ] All tests pass

## Task Dependency Graph

```
TASK-013-001 (standalone)
TASK-013-002 (standalone)
TASK-013-003 (standalone)
TASK-013-004 (standalone)
TASK-013-005 (standalone)
```

All tasks are fully parallel — no dependencies between them.

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-013-001 | ~40k | none | yes (all) | — |
| TASK-013-002 | ~35k | none | yes (all) | — |
| TASK-013-003 | ~35k | none | yes (all) | — |
| TASK-013-004 | ~30k | none | yes (all) | — |
| TASK-013-005 | ~40k | none | yes (all) | opus |

**Total estimated tokens:** ~180k

## Functional Requirements

- FR-1: No CRLF injection possible through header APIs
- FR-2: Content-Length/Transfer-Encoding conflicts must be detected and rejected
- FR-3: Secure cookies must never be transmitted over unencrypted connections
- FR-4: URI path traversal in redirect targets must not escape intended scope
- FR-5: HPACK/QPACK dynamic tables must be bounded by configured limits
- FR-6: Adversarial compressed headers must not cause OOM or excessive allocation

## Non-Goals

- No penetration testing against running server
- No fuzzing (covered in Features 014/015)
- No CORS policy enforcement (server-side concern)
- No content security policy enforcement

## Technical Considerations

- Security tests should be in a new `Security/` folder under `src/TurboHttp.Tests/`
- Each test's DisplayName should clearly state the attack vector being tested
- Tests should verify both prevention (attack is blocked) and correctness (legitimate input works)
- Some tests may require access to internal types via `InternalsVisibleTo`

## Success Metrics

- 40+ security-focused tests covering all documented attack vectors
- Each test documents its threat model
- No security regression when running full test suite
