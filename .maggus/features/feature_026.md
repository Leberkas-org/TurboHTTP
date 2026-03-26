<!-- maggus-id: 20260326-163636-feature-026 -->

# Feature 026: Phase 1 — Stability & Security (RFC Compliance Gaps)

## Introduction

Close three critical security and stability gaps identified in the RFC_STATUS_MATRIX (86/100 compliance):

1. **Header DoS Protection** — Add size/count limits to prevent memory exhaustion attacks
2. **Redirect Security** — Prevent HTTPS→HTTP downgrades and infinite redirect loops
3. **MAX_CONCURRENT_STREAMS Enforcement** — Respect server-advertised stream limits in HTTP/2

These fixes are prerequisites for production use. They address OWASP-relevant attack vectors and ensure RFC compliance.

### Architecture Context

- **Vision alignment:** TurboHttp aims to be a "production-ready, RFC-compliant HTTP client"
- **Components involved:**
  - `TurboHttp/Protocol/RFC1945/Http10Decoder` — HTTP/1.0 decoder
  - `TurboHttp/Protocol/RFC9112/Http11Decoder` — HTTP/1.1 decoder
  - `TurboHttp/Protocol/RFC9113/Http2Decoder` — HTTP/2 frame decoder
  - `TurboHttp/Protocol/RFC9110/RedirectHandler` — redirect following logic
  - `TurboHttp/Streams/Http20Engine` — HTTP/2 stream multiplexing engine
- **New patterns:** Introduces configurable limits (via `HttpDecoderOptions` or similar config class)
- **Architecture updates needed:** CLAUDE.md should document header limit defaults and override mechanisms

## Goals

1. **Prevent Header Exhaustion:** Reject requests with individual headers >16KB or total headers >64KB
2. **Prevent Redirect Loops:** Track redirect chain depth; reject chains >5 redirects
3. **Prevent HTTPS→HTTP Downgrades:** Block redirects from HTTPS to HTTP unless explicitly allowed
4. **Enforce HTTP/2 Concurrency:** Client respects `MAX_CONCURRENT_STREAMS` setting from server
5. **Comprehensive Testing:** Unit + integration + edge-case coverage for all three features

## Tasks

### TASK-026-001: Add Header Size Validation to Http10Decoder
**Description:** As a security-conscious user, I want the HTTP/1.0 decoder to reject oversized headers so that malicious servers cannot exhaust client memory.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-026-004
**Parallel:** yes — can run alongside TASK-026-002, TASK-026-003, TASK-026-006, TASK-026-009

**Acceptance Criteria:**
- [ ] Http10Decoder accepts a `HttpDecoderOptions` (or similar config) with `MaxHeaderSize` (default 16KB) and `MaxTotalHeaderSize` (default 64KB)
- [ ] When parsing headers, track current header name+value size
- [ ] Reject with `HttpDecoderException` if single header exceeds `MaxHeaderSize`
- [ ] Reject with `HttpDecoderException` if total headers exceed `MaxTotalHeaderSize`
- [ ] Error message clearly indicates which limit was violated
- [ ] Existing tests still pass (no breaking changes to public API)
- [ ] Compile with zero warnings

---

### TASK-026-002: Add Header Size Validation to Http11Decoder
**Description:** As a security-conscious user, I want the HTTP/1.1 decoder to reject oversized headers so that malicious servers cannot exhaust client memory.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-026-004
**Parallel:** yes — can run alongside TASK-026-001, TASK-026-003, TASK-026-006, TASK-026-009

**Acceptance Criteria:**
- [ ] Http11Decoder accepts `HttpDecoderOptions` with same limits as Http10Decoder
- [ ] Header validation happens in `Http11DecoderPipeline` before headers are accumulated
- [ ] Chunked Transfer-Encoding bodies are NOT subject to header limits (only headers counted)
- [ ] Trailer headers (if supported) are also subject to `MaxHeaderSize` limit per trailer
- [ ] Reject with `HttpDecoderException` when limits exceeded
- [ ] Edge case: folded headers (multi-line via obs-fold) are counted as single header correctly
- [ ] Compile with zero warnings

---

### TASK-026-003: Add Header Size Validation to Http2Decoder
**Description:** As a security-conscious user, I want the HTTP/2 decoder to reject oversized header blocks so that malicious servers cannot exhaust client memory.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-026-004
**Parallel:** yes — can run alongside TASK-026-001, TASK-026-002, TASK-026-006, TASK-026-009

**Acceptance Criteria:**
- [ ] Http2Decoder accepts `HttpDecoderOptions` with `MaxHeaderSize` (default 16KB) and `MaxTotalHeaderSize` (default 64KB)
- [ ] When decoding HEADERS frame, accumulate decompressed header block size
- [ ] If any single header exceeds `MaxHeaderSize` after HPACK decompression, emit `Http2Exception` with FRAME_SIZE_ERROR
- [ ] If total headers exceed `MaxTotalHeaderSize`, emit `Http2Exception` with FRAME_SIZE_ERROR
- [ ] Pseudo-headers (`:method`, `:scheme`, etc.) count toward limit
- [ ] CONTINUATION frames correctly accumulate decompressed size across frame boundaries
- [ ] Compile with zero warnings

---

### TASK-026-004: Unit Tests — Header DoS Protection (All Protocols)
**Description:** As a developer, I want comprehensive unit tests so that header limit enforcement is verified across HTTP/1.0, HTTP/1.1, and HTTP/2.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-026-001, TASK-026-002, TASK-026-003
**Successors:** TASK-026-005
**Parallel:** no — depends on decoders being implemented

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp.Tests/RFC1945/NN_Http10DecoderHeaderLimitsTests.cs` with 15+ test cases
  - [ ] Single header exceeds MaxHeaderSize → HttpDecoderException
  - [ ] Total headers exceed MaxTotalHeaderSize → HttpDecoderException
  - [ ] Boundary case: header exactly at limit → passes
  - [ ] Boundary case: headers just over limit → fails
  - [ ] Custom limits via HttpDecoderOptions respected
- [ ] Create `src/TurboHttp.Tests/RFC9112/NN_Http11DecoderHeaderLimitsTests.cs` with 20+ test cases
  - [ ] Single header exceeds MaxHeaderSize → HttpDecoderException
  - [ ] Folded (obs-fold) header counted correctly
  - [ ] Chunked body NOT subject to header limits
  - [ ] Custom limits respected
  - [ ] Error message is descriptive
- [ ] Create `src/TurboHttp.Tests/RFC9113/NN_Http2DecoderHeaderLimitsTests.cs` with 18+ test cases
  - [ ] Single header exceeds MaxHeaderSize (post-decompression) → Http2Exception
  - [ ] Total headers exceed MaxTotalHeaderSize → Http2Exception
  - [ ] CONTINUATION frame boundaries handled correctly
  - [ ] Pseudo-headers counted
  - [ ] Custom limits respected
- [ ] All tests use `[Fact(Timeout = 5000)]` or explicit `CancellationToken`
- [ ] All tests have clear `DisplayName` with RFC section reference
- [ ] Total test methods ≥ 50; all passing

---

### TASK-026-005: Integration Tests — Header DoS (Real TCP + Kestrel)
**Description:** As a developer, I want end-to-end tests so that header limits are verified with real Kestrel server responses.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-026-004
**Successors:** none
**Parallel:** yes — can run alongside TASK-026-007, TASK-026-010

**Acceptance Criteria:**
- [ ] Add Kestrel route(s) that return headers at/over limit thresholds
  - Route 1: Single header 20KB (over 16KB limit)
  - Route 2: 150 headers, each 1KB (over 64KB limit)
  - Route 3: Boundary case — headers exactly at limit
- [ ] Create integration test that:
  - [ ] Sends HTTP/1.1 request to oversized-header route, expects HttpDecoderException
  - [ ] Sends HTTP/2 request to oversized-header route, expects Http2Exception
  - [ ] Verifies error is caught by client (not connection crash)
  - [ ] Connection is properly closed or reset
- [ ] Run tests via `dotnet test src/TurboHttp.IntegrationTests` — all passing
- [ ] Verify no socket leaks (use WireShark or OS tool if needed)

---

### TASK-026-006: Implement HTTPS→HTTP Downgrade Protection
**Description:** As a security-conscious user, I want the client to block redirects from HTTPS to HTTP so that I'm protected from downgrade attacks.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-026-007
**Parallel:** yes — can run alongside TASK-026-001, TASK-026-002, TASK-026-003, TASK-026-009

**Acceptance Criteria:**
- [ ] Modify `RedirectHandler` to check redirect URI scheme
- [ ] If current request is HTTPS and redirect target is HTTP:
  - [ ] By default, throw `RedirectException` with message: "Redirect from HTTPS to HTTP blocked"
  - [ ] Add configuration option to allow downgrade (for testing/special cases): `AllowHttpDowngrade` flag
- [ ] Preserve HTTPS→HTTPS and HTTP→HTTP redirects (no change)
- [ ] Preserve HTTP→HTTPS upgrades (encouraged)
- [ ] Log redirect decisions (if logging available)
- [ ] Unit tests document the behavior
- [ ] Existing tests still pass
- [ ] Compile with zero warnings

---

### TASK-026-007: Implement Redirect Loop Detection
**Description:** As a security-conscious user, I want the client to detect and block infinite redirect loops so that I'm protected from denial-of-service attacks.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-026-006
**Successors:** TASK-026-008
**Parallel:** no — depends on downgrade protection being in place

**Acceptance Criteria:**
- [ ] Modify `RedirectHandler` to track visited URLs across redirect chain
- [ ] Maintain a `HashSet<Uri>` of visited URLs (case-insensitive for scheme/host, case-sensitive for path)
- [ ] Before following redirect:
  - [ ] Check if target URL already in visited set
  - [ ] If yes, throw `RedirectException` with message: "Redirect loop detected: {uri}"
  - [ ] If no, add current URL to set and follow redirect
- [ ] Configurable max redirect depth (default 5) — after N redirects, reject even if URLs differ
  - [ ] Check: `if (visitedUrls.Count >= MaxRedirectDepth)`
  - [ ] Throw `RedirectException` with message: "Max redirect depth exceeded ({n})"
- [ ] Edge cases:
  - [ ] Query strings + fragments: handle correctly (e.g., `?v=1` vs `?v=2` are different)
  - [ ] Case-insensitive scheme/host matching
  - [ ] Relative URLs resolved correctly before comparison
- [ ] Unit tests cover loop detection and depth limit
- [ ] Compile with zero warnings

---

### TASK-026-008: Unit & Integration Tests — Redirect Security
**Description:** As a developer, I want comprehensive redirect security tests so that downgrade protection and loop detection are verified.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-026-007
**Successors:** none
**Parallel:** yes — can run alongside TASK-026-005, TASK-026-011

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp.Tests/RFC9110/NN_RedirectSecurityTests.cs` with 25+ test cases
  - **Downgrade Protection:**
    - [ ] HTTPS → HTTP: rejected by default
    - [ ] HTTPS → HTTP: allowed with `AllowHttpDowngrade = true`
    - [ ] HTTPS → HTTPS: allowed (no change)
    - [ ] HTTP → HTTP: allowed (no change)
    - [ ] HTTP → HTTPS: allowed (upgrade encouraged)
  - **Loop Detection:**
    - [ ] URL A → URL A: detected and rejected
    - [ ] URL A → URL B → URL A: detected and rejected (cycle)
    - [ ] URL A → URL B → URL C → URL B: detected and rejected (re-visit)
    - [ ] URL A → URL B → URL C → ... (5 redirects): accepted
    - [ ] URL A → URL B → ... (6 redirects): rejected (depth exceeded)
    - [ ] Query string variations treated as different URLs
    - [ ] Fragment ignored in URL comparison
    - [ ] Case-insensitive host matching
- [ ] Create Kestrel integration tests:
  - [ ] Route that returns 301 HTTPS → HTTP (verify exception thrown)
  - [ ] Route that loops back to itself (verify exception thrown)
  - [ ] Route that chains 4 redirects (verify success)
  - [ ] Route that chains 6 redirects (verify rejection)
- [ ] All tests use `[Fact(Timeout = 5000)]`
- [ ] All tests have clear `DisplayName` with RFC 9110 §15.4 reference
- [ ] Run via `dotnet test src/TurboHttp.Tests` — all passing

---

### TASK-026-009: Implement MAX_CONCURRENT_STREAMS Tracking in Http20Engine
**Description:** As a developer, I want the HTTP/2 engine to track the server's MAX_CONCURRENT_STREAMS setting so that stream limits are enforced.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-026-010
**Parallel:** yes — can run alongside TASK-026-001, TASK-026-002, TASK-026-003, TASK-026-006

**Acceptance Criteria:**
- [ ] Modify `Http20Engine` to maintain `_maxConcurrentStreams: int` (initialized to large value or 0 meaning "unlimited")
- [ ] When server sends SETTINGS frame with MAX_CONCURRENT_STREAMS:
  - [ ] Extract the value via `SettingsFrame.Parameters`
  - [ ] Store in `_maxConcurrentStreams`
  - [ ] Log the update (if logging available)
- [ ] Track active stream count:
  - [ ] Increment when stream transitions to "open" state
  - [ ] Decrement when stream closes (after all data sent and received)
- [ ] Expose via public property: `int MaxConcurrentStreams { get; }`
- [ ] Unit tests verify tracking behavior
- [ ] Compile with zero warnings

---

### TASK-026-010: Enforce MAX_CONCURRENT_STREAMS Limit in Stream Creation
**Description:** As a developer, I want the HTTP/2 engine to enforce the MAX_CONCURRENT_STREAMS limit so that the client doesn't violate server policy.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-026-009
**Successors:** TASK-026-011
**Parallel:** no — depends on tracking being implemented

**Acceptance Criteria:**
- [ ] Modify `Http20Engine` stream creation logic (in correlation/routing stages):
  - [ ] Before creating new stream, check: `if (activeStreamCount >= _maxConcurrentStreams)`
  - [ ] If limit reached:
    - [ ] Queue request locally (in a bounded queue, max 100 pending)
    - [ ] Wait for active streams to close before advancing
    - [ ] Send queued requests as streams complete
- [ ] Handle edge cases:
  - [ ] Server sends MAX_CONCURRENT_STREAMS = 1 → only 1 stream open at a time
  - [ ] Server updates MAX_CONCURRENT_STREAMS mid-connection → adjust queue/backpressure
  - [ ] Request timeout while waiting in queue → reject request with timeout error
- [ ] Backpressure:
  - [ ] If queue reaches max (100), reject new requests with "Connection limit exceeded"
- [ ] Unit tests verify enforcement and queueing
- [ ] Compile with zero warnings

---

### TASK-026-011: Unit & Integration Tests — MAX_CONCURRENT_STREAMS Enforcement
**Description:** As a developer, I want comprehensive tests so that MAX_CONCURRENT_STREAMS enforcement is verified.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-026-010
**Successors:** none
**Parallel:** yes — can run alongside TASK-026-005, TASK-026-008

**Acceptance Criteria:**
- [ ] Create `src/TurboHttp.Tests/RFC9113/NN_Http2MaxConcurrentStreamsTests.cs` with 20+ test cases
  - [ ] Tracking: SETTINGS frame with MAX_CONCURRENT_STREAMS = 5 is stored
  - [ ] Tracking: MAX_CONCURRENT_STREAMS = 0 means unlimited
  - [ ] Enforcement: Can create up to MAX_CONCURRENT_STREAMS streams
  - [ ] Enforcement: 6th stream request is queued (not sent immediately)
  - [ ] Enforcement: As stream closes, queued stream is sent
  - [ ] Enforcement: Multiple streams closing → multiple queued streams sent
  - [ ] Enforcement: Request timeout while queued → rejected with timeout
  - [ ] Enforcement: Queue fills (100 pending) → new requests rejected
  - [ ] Server updates MAX_CONCURRENT_STREAMS mid-connection → adjust behavior
- [ ] Create Kestrel H2 integration test:
  - [ ] Server advertises MAX_CONCURRENT_STREAMS = 3
  - [ ] Send 5 concurrent requests
  - [ ] Verify first 3 sent immediately, 4-5 queued
  - [ ] Verify as responses arrive, queued requests are sent
  - [ ] Verify all 5 complete successfully
- [ ] All tests use `[Fact(Timeout = 10000)]` (may need longer timeout for queueing tests)
- [ ] All tests have clear `DisplayName` with RFC 9113 §5.1.2 reference
- [ ] Run via `dotnet test src/TurboHttp.Tests` — all passing

---

## Task Dependency Graph

```
TASK-026-001 ──→ TASK-026-004 ──→ TASK-026-005
TASK-026-002 ──→↗
TASK-026-003 ──→↗

TASK-026-006 ──→ TASK-026-007 ──→ TASK-026-008

TASK-026-009 ──→ TASK-026-010 ──→ TASK-026-011
```

### Dependency Table

| Task | Estimate | Predecessors | Successors | Parallel | Model |
|------|----------|--------------|-----------|----------|-------|
| TASK-026-001 | ~40k | none | 004 | yes (with 002, 003, 006, 009) | — |
| TASK-026-002 | ~45k | none | 004 | yes (with 001, 003, 006, 009) | — |
| TASK-026-003 | ~35k | none | 004 | yes (with 001, 002, 006, 009) | — |
| TASK-026-004 | ~50k | 001, 002, 003 | 005 | no | — |
| TASK-026-005 | ~30k | 004 | none | yes (with 007, 010) | — |
| TASK-026-006 | ~35k | none | 007 | yes (with 001, 002, 003, 009) | — |
| TASK-026-007 | ~40k | 006 | 008 | no | — |
| TASK-026-008 | ~45k | 007 | none | yes (with 005, 011) | — |
| TASK-026-009 | ~30k | none | 010 | yes (with 001, 002, 003, 006) | — |
| TASK-026-010 | ~35k | 009 | 011 | no | — |
| TASK-026-011 | ~40k | 010 | none | yes (with 005, 008) | — |

**Total estimated tokens:** ~425k tokens (~1 week with two engineers, 10.5 days solo)

---

## Functional Requirements

1. **FR-1:** HTTP/1.0 decoder SHALL validate header size (single header ≤ 16KB, total ≤ 64KB) and reject with `HttpDecoderException` if exceeded
2. **FR-2:** HTTP/1.1 decoder SHALL validate header size with same limits as HTTP/1.0, excluding body data
3. **FR-3:** HTTP/2 decoder SHALL validate decompressed header block size (single ≤ 16KB, total ≤ 64KB) and reject with `Http2Exception` (FRAME_SIZE_ERROR) if exceeded
4. **FR-4:** Limits SHALL be configurable via `HttpDecoderOptions` with sensible defaults (16KB/64KB)
5. **FR-5:** Redirect handler SHALL block HTTPS→HTTP redirects by default and require explicit `AllowHttpDowngrade` flag to allow
6. **FR-6:** Redirect handler SHALL track visited URLs and reject chains that revisit the same URL or exceed 5 redirects
7. **FR-7:** HTTP/2 engine SHALL extract `MAX_CONCURRENT_STREAMS` from SETTINGS frame and store it
8. **FR-8:** HTTP/2 engine SHALL enforce `MAX_CONCURRENT_STREAMS` by queueing requests that exceed the limit, releasing as streams close
9. **FR-9:** All error conditions SHALL produce descriptive error messages identifying which limit was violated and (where applicable) what the limit was

---

## Non-Goals

- **No server-side implementation** — Client-side only
- **No custom header validation rules** — Only size/count limits; no content validation
- **No persistent redirect history** — Per-connection only; history is lost on new connection
- **No automatic retry on stream queue timeout** — Applications must implement retry logic
- **No changes to public API surface** — All additions are backward-compatible (new optional config parameters)
- **No deprecation of existing APIs** — All changes are additive only

---

## Technical Considerations

### Architecture Fit
- **Decoder options:** All three decoders (`Http10Decoder`, `Http11Decoder`, `Http2Decoder`) need a shared `HttpDecoderOptions` config class (or three separate options classes). Consider shared base class or composition.
- **RedirectHandler:** Currently located at `TurboHttp/Protocol/RFC9110/RedirectHandler`. Ensure it has access to configuration (e.g., `AllowHttpDowngrade`, `MaxRedirectDepth`).
- **Http20Engine:** Located at `TurboHttp/Streams/Http20Engine`. Stream lifecycle and active-stream tracking must integrate with existing state machine.

### Configuration Management
- Decoders should accept `HttpDecoderOptions` in constructor or via `Configure()` method
- RedirectHandler should accept options in constructor or handler configuration
- Consider adding to `TurboHttpClientBuilder` fluent API for easy configuration:
  ```csharp
  new TurboHttpClientBuilder()
      .WithDecoderOptions(opts => opts.MaxHeaderSize = 32.Kb())
      .WithRedirectOptions(opts => opts.MaxRedirectDepth = 10)
      .Build()
  ```

### Backward Compatibility
- All configuration options SHALL have sensible defaults (16KB/64KB headers, 5 redirect limit, no downgrade allowed)
- Existing code without configuration SHALL work unchanged
- Decoder behavior change (rejecting oversized headers) is a **breaking change** semantically but NOT in API (same exceptions, just thrown in new cases)

### Error Handling
- `HttpDecoderException` — for decoder violations
- `Http2Exception` with `ErrorCode = FRAME_SIZE_ERROR` — for HTTP/2 violations
- `RedirectException` — for redirect policy violations (consider creating this if it doesn't exist)

### Testing Strategy
- **Unit tests:** Per-decoder, per-feature (doS, redirects, concurrent streams)
- **Integration tests:** Real Kestrel server with routes that trigger limits
- **Interop tests:** None required for Phase 1 (no HTTP/3 yet)
- **Performance impact:** Should be negligible (limit checks are O(1) or O(n) where n = header count)

---

## Success Metrics

1. **Security:** Zero DoS attacks possible via oversized headers, downgrade attacks, or redirect loops
2. **Compliance:** RFC 9113 §5.1.2 (MAX_CONCURRENT_STREAMS), RFC 9110 §15.4.6 (redirect security), RFC 9110 §5 (header limits) all satisfied
3. **Stability:** No memory leaks or connection hangs when limits are reached
4. **Usability:** Clear error messages when limits violated; easy configuration for non-default limits
5. **Testing:** ≥ 50 unit tests + 5+ integration tests, all passing
6. **Performance:** <1% latency impact from limit checking; measured before/after benchmarks

---

## Open Questions

1. **Header Limits — Configurability:**
   - Should limits be global (all decoders use same `HttpDecoderOptions`) or per-decoder?
   - Should limits be configurable per-request (via `HttpRequestMessage.Options`)?
   - **Current assumption:** Global defaults, configurable at client builder level

2. **Redirect Configuration — Scope:**
   - Should `AllowHttpDowngrade` be a global client setting
   - Should `MaxRedirectDepth` be configurable, or is 5 hard-coded?
   - **Current assumption:** Both are global client settings

3. **MAX_CONCURRENT_STREAMS — Backpressure:**
   - When queue reaches 100 pending requests, should we:
     - (A) Reject new requests with exception (current assumption)?
     - (B) Wait indefinitely (could cause deadlock)?
     - (C) Expose queue depth to application (let application decide)?
   - **Current assumption:** Option A (reject with clear error)

4. **Error Type — Redirect Violations:**
   - Should redirect policy violations throw `RedirectException` (new) or `HttpRequestException`?
   - **Current assumption:** New `RedirectException` type (more specific)

5. **Testing — Kestrel Routes:**
   - Kestrel has 60+ routes. Should we add new ones or reuse existing?
   - **Current assumption:** Add targeted new routes (oversized headers, loop redirects, max streams)

---

## Implementation Notes

- **Prerequisite:** Ensure `HttpDecoderOptions` or similar config class exists or is created first
- **Order:** Implement header limits (1-5) before redirect security (6-8) before concurrency (9-11), as they're independent branches
- **Testing:** Parallelize test writing (4, 8, 11) with implementation
- **Documentation:** Update CLAUDE.md with configuration examples and default limits once complete

