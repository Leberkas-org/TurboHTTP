---
title: "Feature 008: TLS Integration Tests"
description: "Integration test coverage for HTTPS/TLS connections using the Kestrel TLS fixture"
tags: [features, history, tls, https, testing, security]
status: completed
---

# Feature 008: TLS Integration Tests

## Summary

| Field | Value |
|-------|-------|
| **Status** | ✅ Completed |
| **Category** | Integration Tests |
| **Scope** | 1 task (TASK-008-001) |
| **Maggus Plan** | Not available |

## Description

Added integration tests for HTTPS/TLS connections using the `KestrelTlsFixture`. Tests verified:

- TLS handshake and certificate negotiation
- HTTPS request/response round-trips (HTTP/1.1 over TLS)
- HTTP/2 over TLS (ALPN negotiation)
- Basic cipher and protocol version behaviour

The `KestrelTlsFixture` spins up a Kestrel server with a self-signed dev certificate. Client-side TLS was configured through `TurboHttpClientBuilder` with certificate validation bypass for test environments.

## Key Source Files

| File | Role |
|------|------|
| `src/TurboHttp.IntegrationTests/Tls/TlsIntegrationTests.cs` | TLS integration tests |
| `src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs` | TLS server fixture |

## See Also

- [[Features/Feature013_Security_Tests\|Feature 013]] — security-focused adversarial tests
- [[Architecture/14-TRANSPORT_LAYER\|Transport Layer]] — TCP/TLS transport design
