# Feature 008: Integration Tests — TLS

## Introduction

Add end-to-end integration tests for TLS transport. Validates that TurboHttp correctly handles HTTPS connections, certificate validation, and TLS-specific behaviors using KestrelTlsFixture.

### Architecture Context

- **Components involved:** TlsClientProvider (Transport), ConnectionActorBase (Pooling), KestrelTlsFixture
- **Existing infrastructure:** KestrelTlsFixture with self-signed cert (HTTP/1.1 over HTTPS), all standard routes registered

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify basic HTTPS GET/POST works with TLS
- Verify body echo over encrypted connection
- Verify header roundtrip over TLS
- Verify cookie handling over TLS (Secure flag)
- Verify compression over TLS

## Tasks

### TASK-008-001: TLS Integration Tests
**Description:** As a library consumer, I want HTTPS connections to work transparently so that my application can communicate securely.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — independent feature

**Acceptance Criteria:**
- [x] Test file `TlsIntegrationTests.cs` in `src/TurboHttp.IntegrationTests/`
- [x] Collection: `"TlsIntegration"`, uses `KestrelTlsFixture`
- [x] ClientHelper configured with `scheme: "https"` and custom certificate validation (accept self-signed)
- [x] Tests cover:
  - `GET /hello` — basic HTTPS request/response
  - `POST /echo` — body echo over TLS
  - `GET /headers/echo` — header roundtrip over TLS
  - `GET /cookie/set/{name}/{value}` + `GET /cookie/echo` — cookie roundtrip over TLS
  - `GET /cookie/set-secure/{name}/{value}` + `GET /cookie/echo` — Secure cookie IS sent over HTTPS
  - `GET /compress/gzip/{kb}` — decompression over TLS
  - `GET /redirect/302/hello` — redirect following over TLS
  - `GET /large/{kb}` — large body transfer over TLS (64KB, 256KB)
  - `GET /chunked/{kb}` — chunked transfer over TLS
- [x] All tests pass

## Task Dependency Graph

```
TASK-008-001 (standalone)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-008-001 | ~40k | none | yes | — |

**Total estimated tokens:** ~40k

## Functional Requirements

- FR-1: HTTPS GET must return correct status code and body
- FR-2: HTTPS POST with body must echo correctly
- FR-3: Secure cookies must be sent over HTTPS connections
- FR-4: Compression must work identically over TLS
- FR-5: Redirects must work over TLS
- FR-6: Large bodies (256KB+) must transfer correctly over TLS

## Non-Goals

- No client certificate authentication testing (not configured in fixture)
- No TLS version negotiation testing
- No certificate chain validation testing (self-signed bypass needed)
- No hostname mismatch testing

## Technical Considerations

- KestrelTlsFixture uses a self-signed certificate — ClientHelper must configure `ServerCertificateValidationCallback` to accept it
- TLS adds overhead — use generous timeouts (30s)
- The Secure cookie test is meaningful here: over HTTPS, Secure cookies SHOULD be sent (unlike HTTP/1.1 fixture where they should NOT)

## Success Metrics

- All 9+ TLS integration tests pass consistently
- Secure cookie test proves HTTPS-only behavior
