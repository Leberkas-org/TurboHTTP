# RFC Compliance Overview

TurboHttp implements HTTP/1.0, HTTP/1.1, HTTP/2, HPACK header compression, HTTP semantics, caching, and cookies with full RFC compliance on the client side.

**Scope:** Client-side only. Encoders: `HttpRequestMessage → bytes`. Decoders: `bytes → HttpResponseMessage`.

## Compliance Summary

| RFC | Standard | Sections Covered | Coverage | Unit Tests | Stream Tests |
|-----|----------|-----------------|----------|------------|--------------|
| [RFC 1945](/rfc/rfc1945) | HTTP/1.0 | §4–§10 | 100% | 233 | 41 |
| [RFC 9112](/rfc/rfc9112) | HTTP/1.1 Message Framing | §2–§9 | 100% | 374 | 97 |
| [RFC 9113](/rfc/rfc9113) | HTTP/2 | §3–§8 | 100% | 545 | 180 |
| [RFC 7541](/rfc/rfc7541) | HPACK Header Compression | §2–§6, Appendix C | 100% | 419 | 8 |
| [RFC 9110](/rfc/rfc9110) | HTTP Semantics | §8.4, §9.2, §12.5, §15.4 | 100% | 123 | 55 |
| [RFC 9111](/rfc/rfc9111) | HTTP Caching | §3–§5 | 100% | 75 | 28 |
| [RFC 6265](/rfc/rfc6265) | HTTP Cookies | §4–§5 | 100% | 66 | 12 |
| — | IO Layer | TcpOptions, ClientManager, Actors | 100% | — | 47 |
| — | Stage Infrastructure | Connection, Engine, Enricher, buffers | 100% | — | 132 |
| **Total** | | | | **1835** | **600** |

> **Note:** Integration tests (`src/TurboHttp.IntegrationTests/`) have Kestrel fixtures with 60+ routes but no end-to-end test classes yet.

## Remaining Gaps

| Gap ID | RFC Section | Requirement | Priority | Status |
|--------|-------------|-------------|----------|--------|
| GAP-001 | RFC 7231 §7.1.1 | Date/time format parsing (IMF-fixdate, RFC 850, asctime) | MUST | Deferred — out of scope for protocol layer; `HttpResponseMessage` exposes raw `Date` header string, consistent with .NET `HttpClient` behaviour. |

All other original gaps have been closed:

- **GAP-002/003** (DATA/HEADERS PADDED flag) → closed by `16_DecoderPaddingTests.cs`
- **GAP-004** (Push-Promise state machine) → closed by `15_DecoderPushPromiseTests.cs`
- **GAP-005** (StreamIdAllocatorStage) → closed by `StreamIdAllocatorStageTests.cs`
- **GAP-006/007** (Correlation stages) → closed by `Http1XCorrelationStageTests.cs` and `Http20CorrelationStageTests.cs`
- **GAP-008** (ExtractOptionsStage) → closed by `ExtractOptionsStageTests.cs`

## Test Conventions

### DisplayName Format

```
"RFC<number>-<section>-<category>-<sequence>: <description>"
```

Examples:
- `"RFC9113-5.1.1-PP-001: PUSH_PROMISE moves stream to reserved(remote) state"`
- `"RFC9112-7.1-CH-003: Chunked body with multiple chunks"`
- `"RFC9111-§4.2: max-age=60 freshness lifetime"`

### Pre-Existing Test Failures

| Test | Project | Issue |
|------|---------|-------|
| 6 RFC9113 tests | TurboHttp.Tests | H2 concurrency timing issues — known, pre-existing |
| RFC9110-15.4-RH-015 | TurboHttp.Tests | Relative Location URI resolution — known, pre-existing |
