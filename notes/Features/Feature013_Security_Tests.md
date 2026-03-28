---
title: "Feature 013: Security Tests"
description: "Adversarial security test suite covering header injection, request smuggling, cookie security, URI traversal, and HPACK attacks"
tags: [features, history, security, testing, hpack, http-smuggling]
status: completed
---

# Feature 013: Security Tests

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Security / Testing |
| **Scope** | 5 tasks (TASK-013-001 through TASK-013-005) |
| **Maggus Plan** | Not available |

## Description

Added a comprehensive adversarial security test suite targeting HTTP protocol attack vectors. Tests verified that TurboHttp correctly rejects or handles malicious inputs across all protocol layers.

| Task | Coverage |
|------|----------|
| TASK-013-001 | Header injection and HTTP request smuggling (RFC 9112 §11.2) |
| TASK-013-002 | TLS transport security — weak ciphers, expired certs, MITM scenarios |
| TASK-013-003 | Cookie security — `HttpOnly`, `Secure`, `SameSite`, injection attempts |
| TASK-013-004 | URI sanitization and path traversal (`../` sequences, null bytes, encoded separators) |
| TASK-013-005 | HPACK bomb attacks (highly compressed headers), protocol abuse (oversized frames, invalid stream IDs) |

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp.Tests/Security/HeaderSecurityTests.cs` | Header injection and smuggling |
| `src/TurboHttp.Tests/Security/TlsSecurityTests.cs` | Transport security |
| `src/TurboHttp.Tests/Security/CookieSecurityTests.cs` | Cookie attack surface |
| `src/TurboHttp.Tests/Security/UriSecurityTests.cs` | URI sanitization |
| `src/TurboHttp.Tests/Security/HpackSecurityTests.cs` | HPACK bomb and protocol abuse |

## See Also

- [[Features/Feature015_H2_HPACK_Fuzzing\|Feature 015]] — related HPACK adversarial fuzzing
- [[Architecture/16-PROTOCOL_LAYER\|Protocol Layer]] — HPACK/QPACK internals
