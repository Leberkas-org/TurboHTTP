# Feature 006: Integration Tests — Protocol Edge Cases & Error Handling

## Introduction

The final integration test feature covers advanced protocol-specific scenarios, error handling, and cross-protocol consistency. This includes HTTP/2 multiplexing, GOAWAY, and stream abort; HTTP/3 QUIC-specific behavior; TLS certificate handling; timeouts; connection abort mid-response; large headers; and a cross-protocol test suite that verifies identical behavior across all HTTP versions.

### Architecture Context

- **Components involved:**
  - `Http20ConnectionStage` — SETTINGS/PING/GOAWAY handling, flow control, stream lifecycle
  - `Http20StreamStage` — response assembly, HPACK decode
  - `Http30ConnectionStage` — control stream, SETTINGS, GOAWAY
  - `Http30StreamStage` — QPACK decode, response assembly
  - `ConnectionStage` — TCP connection wrapper, error propagation
  - `HostPoolActor` — connection pool lifecycle, reconnect logic
  - `QuicClientProvider` — QUIC connection, ALPN negotiation
  - `TurboClientOptions.DangerousAcceptAnyServerCertificate` — cert bypass
  - `ITurboHttpClient.Timeout` — per-request timeout enforcement
- **Existing routes used:** `/delay/{ms}`, `/edge/*`, `/h2/*`, `/h3/*`, `/status/*`, all basic routes
- **Depends on:** Feature 002 (ClientHelper, Collections)

## Goals

- Verify HTTP/2 multiplexing (concurrent requests on single connection)
- Test HTTP/2 stream abort (RST_STREAM) detection
- Test HTTP/2 large headers (HPACK compression)
- Verify HTTP/3 protocol identification and QUIC transport
- Test request timeout enforcement
- Verify connection abort mid-response detection and recovery
- Test large response headers
- Test unknown/edge-case responses (empty body, unknown encoding)
- Cross-protocol consistency: identical behavior across HTTP/1.1, 2.0, 3.0
- Verify TLS certificate handling (self-signed cert bypass)

## Tasks

### TASK-006-001: HTTP/1.1 Error Handling & Edge Cases
**Description:** As a developer, I want error handling and edge case tests for HTTP/1.1, so that timeout enforcement, connection abort detection, and unusual responses are verified.

**Token Estimate:** ~60k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-006-002, TASK-006-003, TASK-006-004

**Acceptance Criteria:**
- [ ] `Http1ErrorTests.cs` with `[Collection("Http1Integration")]` (~12 tests):
  - GET /delay/100 → succeeds after ~100ms
  - GET /delay/5000 with client Timeout=1s → TimeoutException or TaskCanceledException
  - GET /edge/close-mid-response → connection reset detected, exception thrown
  - GET /edge/large-header/10 → 10 KB header value processed correctly
  - GET /edge/large-header/50 → 50 KB header value
  - GET /edge/empty-body → 200 without Content-Length, empty body
  - GET /edge/unknown-encoding → Content-Encoding: x-custom, body preserved raw
  - GET /status/500 → 500 status propagated, no exception
  - GET /status/204 → no body, no Content-Length
  - Two requests after a connection error → recovery works, second request succeeds
  - Sequential requests with alternating success/failure → client stays healthy
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http1ErrorTests"`

### TASK-006-002: HTTP/2 Advanced & Multiplexing Tests
**Description:** As a developer, I want HTTP/2 advanced tests covering multiplexing, stream abort, large headers, and concurrent request handling.

**Token Estimate:** ~80k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-006-001, TASK-006-003, TASK-006-004

**Acceptance Criteria:**
- [ ] `Http2AdvancedTests.cs` with `[Collection("Http2Integration")]` (~16 tests):
  - 5 concurrent GET /hello → all succeed, multiplexed on single connection
  - 10 concurrent GET /delay/50 → all complete, responses matched to correct requests
  - Responses can arrive out of order → correct correlation (stream ID matching)
  - GET /h2/abort → RST_STREAM detected, appropriate exception
  - GET /h2/delay/100 → succeeds after ~100ms
  - GET /h2/delay/5000 with Timeout=1s → timeout
  - GET /h2/large-headers/20 → 20 KB headers decoded via HPACK
  - GET /h2/many-headers → 20 custom headers all decoded
  - GET /h2/settings → server SETTINGS frame exchanged
  - Mix of sequential and concurrent requests → all succeed
  - POST /h2/echo-binary + 5 concurrent GET /hello → all multiplexed
  - Large body + concurrent small requests → no starvation
  - GET /h2/settings/max-concurrent → stream limit info available
  - Error recovery: after stream abort, new requests still work
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http2AdvancedTests"`

### TASK-006-003: HTTP/3 Advanced & TLS Security Tests
**Description:** As a developer, I want HTTP/3 advanced tests and TLS security tests covering QUIC behavior, protocol identification, and certificate handling.

**Token Estimate:** ~70k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-006-001, TASK-006-002, TASK-006-004

**Acceptance Criteria:**
- [ ] `Http3AdvancedTests.cs` with `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]` (~10 tests):
  - GET /h3/protocol → body contains "HTTP/3"
  - 5 concurrent GET /hello → multiplexed QUIC streams
  - GET /h3/delay/100 → succeeds
  - GET /h3/delay/5000 with Timeout=1s → timeout
  - GET /h3/many-headers → 20 custom headers via QPACK
  - POST /h3/echo-binary + concurrent GETs → all succeed
  - GET /h3/stream/500 → streaming response over QUIC
  - Error recovery after stream failure
- [ ] `TlsSecurityTests.cs` with `[Collection("TlsIntegration")]` (~10 tests):
  - GET /hello via HTTPS → succeeds with DangerousAcceptAnyServerCertificate=true
  - POST /echo via HTTPS with body → echo works
  - GET /headers/echo via HTTPS → headers preserved
  - GET /large/50 via HTTPS → 50 KB over TLS
  - Self-signed certificate requires DangerousAcceptAnyServerCertificate
  - Multiple sequential HTTPS requests → connection reused
  - GET /delay/100 via HTTPS → succeeds
  - GET /status/500 via HTTPS → error propagated correctly
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http3AdvancedTests|TlsSecurityTests"`

### TASK-006-004: Cross-Protocol Consistency Tests
**Description:** As a developer, I want cross-protocol tests that verify identical behavior across HTTP/1.1, HTTP/2, and HTTP/3 for core scenarios, ensuring protocol transparency.

**Token Estimate:** ~80k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — can run alongside TASK-006-001, TASK-006-002, TASK-006-003

**Acceptance Criteria:**
- [ ] `CrossProtocolTests.cs` (uses all fixtures, multiple collections or custom setup) (~16 tests):
  - `[Theory]` parameterized by HTTP version (1.1, 2.0, 3.0):
    - GET /hello → 200 "Hello World"
    - POST /echo with text body → echo matches
    - POST /echo with 100 KB body → large body echo
    - GET /large/50 → 50 KB received
    - GET /headers/echo with X-Custom → header echo
    - GET /status/404 → correct status code
    - `[Theory]` GET /status/{code} for 200, 204, 400, 500 → consistent across versions
  - `[Theory]` parameterized by version, with features enabled:
    - GET /compress/gzip/10 with `.WithDecompression()` → decompressed body matches
    - GET /redirect/302/hello with `.WithRedirect()` → redirect followed
    - Cookie roundtrip with `.WithCookies()` → cookie persists
  - 5 concurrent requests per version → all succeed
  - Large headers per version → all decoded
- [ ] HTTP/3 tests marked with `[Trait("Category", "Http3")]`
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "CrossProtocolTests"`

## Task Dependency Graph

```
              ┌──→ TASK-006-001
              ├──→ TASK-006-002
Feature 002 ──┼──→ TASK-006-003
              └──→ TASK-006-004
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-006-001 | ~60k | Feature 002 | yes (with 002, 003, 004) | — |
| TASK-006-002 | ~80k | Feature 002 | yes (with 001, 003, 004) | — |
| TASK-006-003 | ~70k | Feature 002 | yes (with 001, 002, 004) | — |
| TASK-006-004 | ~80k | Feature 002 | yes (with 001, 002, 003) | — |

**Total estimated tokens:** ~290k

## Functional Requirements

- FR-1: Request timeout must be enforced reliably (±500ms tolerance)
- FR-2: Connection abort mid-response must raise an exception, not hang
- FR-3: After a connection error, subsequent requests must succeed (recovery)
- FR-4: HTTP/2 multiplexing must correctly correlate responses to requests via stream IDs
- FR-5: HTTP/2 RST_STREAM must propagate as a recognizable exception
- FR-6: HTTP/3 protocol must be identifiable from response metadata
- FR-7: Self-signed TLS certificates must work with DangerousAcceptAnyServerCertificate=true
- FR-8: Cross-protocol tests must produce identical results regardless of HTTP version
- FR-9: Large response headers (50+ KB) must be processed without truncation
- FR-10: Concurrent requests must not cause data corruption or response mixing

## Non-Goals

- No HTTP/2 server push testing (client-only, no push initiation route)
- No HTTP/2 priority/weight testing (deprecated in RFC 9113)
- No QUIC connection migration testing (not controllable from test)
- No mTLS / client certificate testing
- No HTTP/2 GOAWAY-initiated graceful shutdown testing (requires server-side control)

## Technical Considerations

- Timeout tests: Use `/delay/{ms}` route with client `Timeout` set shorter than delay
- Connection abort: `/edge/close-mid-response` starts writing then aborts — test must handle partial response
- HTTP/2 multiplexing: Use `Task.WhenAll` with multiple `SendAsync` calls to verify concurrent handling
- Cross-protocol tests: Need access to all 3 fixtures — may need custom xUnit setup or 3 separate test classes with shared test logic via helper methods
- HTTP/3 tests may flake on CI without QUIC support — `[Trait("Category", "Http3")]` enables filtering
- Large header tests: KestrelH2Fixture has MaxRequestHeadersTotalSize=512KB, but KestrelFixture uses defaults — test accordingly

## Success Metrics

- All 64 tests green on Windows 11 (including HTTP/3)
- All ~54 tests (excluding HTTP/3) green on systems without QUIC
- HTTP/2 multiplexing tests handle 10+ concurrent requests without timeout
- Cross-protocol tests produce bit-identical results across versions
- No flaky timeout or concurrency tests after 10 repeated runs
- Recovery tests prove client stays healthy after errors

## Open Questions

_None — all questions resolved._
