<!-- maggus-id: 20260326-170000-feature-002 -->

# Feature 002: HTTP/1.0 Deadlock Fix & Error-H10-005 Regression

## Introduction

Fix two critical issues blocking 12 HTTP/1.0 integration tests (out of 515 total integration tests):

1. **GroupByHostKeyStage substream completion race** — deadlock occurs when GroupByHostKeyStage completes its outlet while feature BidiStages (Retry, Cache, Compression) still want to re-inject requests. Affects tests requiring 2+ sequential HTTP/1.0 requests with retries or revalidation.

2. **Error-H10-005 unknown Content-Encoding** — deterministic 6-7ms failure when server returns unknown `Content-Encoding` header value (e.g., `x-custom`). ContentEncodingBidiStage throws instead of gracefully passing through.

### Architecture Context

- **Vision alignment:** TurboHttp must support robust HTTP/1.0 client with full feature coverage (retry, cache, compression)
- **Components involved:**
  - `src/TurboHttp/Streams/Stages/Routing/GroupByHostKeyStage.cs` — routing per-host substreams; completes prematurely
  - `src/TurboHttp/Streams/Stages/Features/RetryBidiStage.cs` — re-injects retry requests; blocked by premature stage completion
  - `src/TurboHttp/Streams/Stages/Features/CacheBidiStage.cs` — re-injects revalidation requests; same deadlock pattern
  - `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs` — decompression; throws on unknown encodings
  - `src/TurboHttp.IntegrationTests/H10/` — 12 affected tests across 5 test classes
- **New patterns:** Two-phase completion semantics (signal upstream done via `Queue.Complete()`, defer actor completion until all substreams dead)
- **Architecture updates needed:** CLAUDE.md should document GroupByHostKeyStage completion protocol and substream lifecycle

## Goals

1. **Fix GroupByHostKeyStage race:** Defer stage completion until all substreams independently close
2. **Fix Error-H10-005:** Handle unknown Content-Encoding gracefully (pass-through without exception)
3. **Eliminate deadlocks:** All 12 affected tests pass consistently (zero hangs)
4. **Add regression tests:** Three new test cases with 10-second explicit timeouts
5. **Validate full test suite:** All 515 integration tests pass, zero flakes

## Tasks

### TASK-002-001: Analyze GroupByHostKeyStage Completion Logic
**Description:** As a developer, I want to understand the current completion flow so that the race condition is clear before implementing the fix.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-002-002
**Parallel:** yes — can run alongside TASK-002-003

**Acceptance Criteria:**
- [x] Read `src/TurboHttp/Streams/Stages/Routing/GroupByHostKeyStage.cs` lines 141–160 (`TryFinish()` method)
- [x] Document current flow: `TryFinish()` → check all subflows drained → call `CompleteStage()` immediately
- [x] Identify race condition: stage actor scope killed while downstream BidiStages still pushing to outlet
- [x] Trace RetryBidiStage flow: `_readyRetries.Count > 0` → `TryEmitRetry()` → `Push(_outRequest)` races with stage death
- [x] Identify fix location: replace `CompleteStage()` with deferred `TryCompleteStage()` that checks all substreams dead
- [x] Document: create brief analysis doc `DEADLOCK_ANALYSIS.md` in project root (200 words max)

---

### TASK-002-002: Implement GroupByHostKeyStage Two-Phase Completion
**Description:** As a developer, I want to defer stage completion until all substreams are dead so that feature BidiStages can emit re-injections.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-002-001
**Successors:** TASK-002-005, TASK-002-006
**Parallel:** no — requires analysis from TASK-002-001

**Acceptance Criteria:**
- [x] Modify `src/TurboHttp/Streams/Stages/Routing/GroupByHostKeyStage.cs`:
  - [x] Create new private method `TryCompleteStage()` that:
    - Checks `if (_subflows.Values.All(state => state.IsDead))`
    - Only calls `CompleteStage()` when true
    - Logs debug message "GroupByHostKeyStage: all substreams dead, completing stage" when completed
    - Logs "GroupByHostKeyStage: deferring completion, N substreams still alive" when deferred
  - [x] Modify `TryFinish()` method (line 159): change `CompleteStage()` to `TryCompleteStage()`
  - [x] Ensure `TryCompleteStage()` is called from all completion paths:
    - [x] End of `TryFinish()` method (existing flow)
    - [x] End of `_onOfferComplete` callback when `_upstreamFinished` and offer succeeds (line 130)
    - [x] End of `ReplaceSubstream()` when new substream created (line 251)
    - [x] End of `ReplaceSubstream()` when no pending items (line 243)
- [x] Ensure no other code calls `CompleteStage()` directly (grep to verify)
- [x] Compile with zero warnings
- [x] No behavior change for normal cases — two-phase completion is transparent to upstream/downstream

---

### TASK-002-003: Analyze ContentEncodingBidiStage Unknown Encoding Handling
**Description:** As a developer, I want to identify how ContentEncodingBidiStage handles unknown Content-Encoding values and where the exception is thrown.

**Token Estimate:** ~12k tokens
**Predecessors:** none
**Successors:** TASK-002-004
**Parallel:** yes — can run alongside TASK-002-001

**Acceptance Criteria:**
- [x] Locate `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs` (file path from CLAUDE.md)
- [x] Find where Content-Encoding header is parsed (likely in response handler)
- [x] Identify the switch/if statement that dispatches based on encoding type (gzip, deflate, br, etc.)
- [x] Find where unknown encodings cause exception (`InvalidOperationException` or similar)
- [x] Check test route `/edge/unknown-encoding` at `src/TurboHttp.IntegrationTests/Shared/ServerFixture.cs` line 540
- [x] Document: current behavior (throws), expected behavior (pass-through), required change
- [x] Compile existing code (no changes yet) to establish baseline

---

### TASK-002-004: Implement ContentEncodingBidiStage Unknown Encoding Pass-Through
**Description:** As a developer, I want ContentEncodingBidiStage to gracefully handle unknown encodings instead of throwing.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-002-003
**Successors:** TASK-002-005, TASK-002-006
**Parallel:** no — requires analysis from TASK-002-003

**Acceptance Criteria:**
- [ ] Modify `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs`:
  - [ ] Find the encoding type dispatch logic (if/switch statement)
  - [ ] Add `default` case (or extend existing fallback) for unknown encodings:
    - Log `Log.Debug("ContentEncodingBidiStage: unknown encoding '{0}', passing through unchanged", encodingName)`
    - Create identity decompressor that returns body unchanged (no decompression)
    - Route response through identity decompressor
  - [ ] Remove `throw InvalidOperationException` for unknown encodings
  - [ ] Ensure response is passed to `Out2` with unchanged body
- [ ] Error-H10-005 test case: Server returns `Content-Encoding: x-custom`, test expects `StatusCode.OK` (no exception)
- [ ] Verify test passes: `dotnet test src/TurboHttp.IntegrationTests/H10/ErrorHandlingIntegrationTests.cs -- --filter "*H10005*"`
- [ ] Compile with zero warnings

---

### TASK-002-005: Create HTTP/1.0 Deadlock Regression Test File
**Description:** As a developer, I want regression tests for the deadlock fix so that future changes don't re-introduce the bug.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-002-002, TASK-002-004
**Successors:** TASK-002-007
**Parallel:** no — requires both fixes complete

**Acceptance Criteria:**
- [ ] Create new test file `src/TurboHttp.IntegrationTests/H10/Http10DeadlockRegressionTests.cs`
- [ ] Add three test methods with explicit `[Fact(Timeout = 10000)]` (10 second timeout):
  - [ ] **Deadlock-H10-001: Retry after 503** — Send GET, server returns 503, BidiStage retries, new request succeeds
    - Send initial GET request
    - Server first response: 503 Service Unavailable
    - Feature BidiStage evaluates as retryable (503 is idempotent-safe)
    - Re-injects same request on outlet
    - Server second response: 200 OK
    - Test verifies final response is 200
    - Timeout 10 seconds (must not hang)
  - [ ] **Deadlock-H10-002: Cache Revalidation** — Send GET with cache headers, server returns 200 without ETag, test must not hang
    - Send initial GET to cached resource
    - Server returns 200 with Cache-Control header
    - BidiStage stores in cache
    - Send second GET (cache hit expected)
    - Server returns 200 (or 304)
    - Test verifies response received without hang
    - Timeout 10 seconds
  - [ ] **Deadlock-H10-003: Compression Negotiation** — Send GET with Accept-Encoding, server returns gzip response, then unknown encoding
    - Send GET with `Accept-Encoding: gzip`
    - Server returns 200 with `Content-Encoding: gzip`
    - ContentEncodingBidiStage decompresses
    - Send second GET
    - Server returns 200 with `Content-Encoding: x-unknown`
    - ContentEncodingBidiStage passes through (via TASK-002-004 fix)
    - Test verifies response received without hang
    - Timeout 10 seconds
- [ ] All three tests use HTTP/1.0 (set via default request version or explicit version in request)
- [ ] All three tests use `IAsyncLifetime` to set up and tear down test server
- [ ] Run tests: `dotnet test src/TurboHttp.IntegrationTests/H10/Http10DeadlockRegressionTests.cs` — all passing
- [ ] Compile with zero warnings

---

### TASK-002-006: Run Full HTTP/1.0 Integration Test Suite Validation
**Description:** As a developer, I want to run all H10 integration tests to verify no regressions and all 12 previously-failing tests now pass.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-002-002, TASK-002-004
**Successors:** TASK-002-007
**Parallel:** no — requires both fixes complete

**Acceptance Criteria:**
- [ ] Run H10 integration test suite: `dotnet test src/TurboHttp.IntegrationTests/H10/ --configuration Release`
- [ ] Verify all tests pass (no timeouts, no deadlocks)
- [ ] Specifically verify these 12 previously-failing tests now pass:
  - Retry tests (5 tests): all pass without hang
  - Cache tests (4 tests): all pass without hang
  - Compression tests (3 tests): all pass without hang
- [ ] Run Error-H10-005 test 10 times consecutively: `for i in {1..10}; do dotnet test ... -- --filter "*H10005*"; done`
  - All 10 runs pass (verify consistency)
  - No 6-7ms timeouts
  - Response status = 200 OK
- [ ] Log output shows zero `GroupByHostKeyStage: CompleteStage()` debug warnings (stage completes cleanly)
- [ ] Memory profiling: no leaks detected (can use `dotnet test --settings runsettings.xml` with memory instrumentation)
- [ ] Total test count: 515 integration tests, 100% pass rate

---

### TASK-002-007: Comprehensive Validation & Commit
**Description:** As a developer, I want to validate the entire test suite and commit the fixes so the changes are persisted.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-002-005, TASK-002-006
**Successors:** none
**Parallel:** no — requires all validation complete

**Acceptance Criteria:**
- [ ] Run full build: `dotnet build --configuration Release src/TurboHttp.sln`
  - Zero errors, zero warnings
- [ ] Run all tests: `dotnet test src/TurboHttp.sln`
  - Unit tests: all pass
  - Stream tests: all pass
  - Integration tests: all 515 pass (including new regression tests)
  - Total: 260+ unit + 300+ stream + 515 integration = 1000+ tests, 100% pass rate
- [ ] Code review:
  - [ ] Verify `GroupByHostKeyStage.TryCompleteStage()` method is correct (no logic errors)
  - [ ] Verify ContentEncodingBidiStage unknown encoding handler is idiomatic (matches codebase style)
  - [ ] Verify new tests follow project conventions (DisplayName, Timeout, assertions)
  - [ ] Check for any new compiler warnings (must be zero)
- [ ] Create git commit:
  - Title: `"Fix HTTP/1.0 deadlock & unknown Content-Encoding handling"`
  - Body includes:
    - Summary of GroupByHostKeyStage two-phase completion fix
    - Summary of ContentEncodingBidiStage pass-through for unknown encodings
    - References to Feature 002 (this plan)
    - Verification: "All 515 integration tests pass, including 3 new regression tests for deadlock scenarios"
- [ ] Commit is created locally (do NOT push unless explicitly requested)
- [ ] git status shows clean working directory

---

## Task Dependency Graph

```
TASK-002-001 ──→ TASK-002-002 ──→ TASK-002-005 ──→ TASK-002-007
                                        ↓              ↑
TASK-002-003 ──→ TASK-002-004 ──→ TASK-002-006 ────┘
```

### Dependency Table

| Task | Estimate | Predecessors | Successors | Parallel | Model |
|------|----------|--------------|-----------|----------|-------|
| TASK-002-001 | ~15k | none | 002 | yes (with 003) | — |
| TASK-002-002 | ~35k | 001 | 005, 006 | no | opus |
| TASK-002-003 | ~12k | none | 004 | yes (with 001) | — |
| TASK-002-004 | ~20k | 003 | 005, 006 | no | — |
| TASK-002-005 | ~40k | 002, 004 | 007 | no | — |
| TASK-002-006 | ~25k | 002, 004 | 007 | no | — |
| TASK-002-007 | ~20k | 005, 006 | none | no | — |

**Total estimated tokens:** ~167k tokens (~5 hours with focused execution, ~8 hours with review)

---

## Functional Requirements

1. **FR-1:** GroupByHostKeyStage SHALL defer `CompleteStage()` until all substreams report dead (via `WatchTask.IsCompleted`)
2. **FR-2:** Feature BidiStages (Retry, Cache) SHALL be able to emit re-injections on outlet after upstream finishes
3. **FR-3:** ContentEncodingBidiStage SHALL handle unknown Content-Encoding values by passing response body through unchanged
4. **FR-4:** Error-H10-005 test (unknown encoding `/edge/unknown-encoding`) SHALL return 200 OK without exception
5. **FR-5:** All 12 previously-failing H10 integration tests SHALL pass with zero timeouts or deadlocks
6. **FR-6:** All 515 integration tests SHALL maintain 100% pass rate (zero regressions)

---

## Non-Goals

- **No changes to upstream ChannelSource completion logic** — KillSwitch remains responsible for holding pipeline alive
- **No changes to Akka.Streams framework code** — only application-layer stages modified
- **No new feature additions** — this is pure bug fix (zero new functionality)
- **No documentation site updates** — only code comments and inline docs
- **No performance optimization** — this fix may add minimal overhead (substream death checks) but prioritizes correctness

---

## Technical Considerations

### GroupByHostKeyStage Completion Race

**Current behavior:**
1. ChannelSource completes (or upstream finishes)
2. GroupByHostKeyStage `TryFinish()` sees `_upstreamFinished = true`
3. All substreams drained (`_subflows.Values.All(state => !state.Offering && state.Pending.Count == 0)`)
4. `CompleteStage()` called immediately → kills stage actor scope
5. RetryBidiStage pushes retry to outlet → outlet is dead → push fails → deadlock/hang

**New behavior:**
1. ChannelSource completes
2. GroupByHostKeyStage `TryFinish()` calls `TryCompleteStage()` (deferred)
3. `TryCompleteStage()` checks if all substreams are dead: `_subflows.Values.All(state => state.IsDead)`
4. If not all dead: log and defer completion
5. When `_onOfferComplete` callback fires (after substream queue processes items), call `TryCompleteStage()` again
6. When all substreams dead: `CompleteStage()` finally called
7. Feature BidiStages can emit re-injections safely before stage death

### ContentEncodingBidiStage Unknown Encoding

**Current behavior:**
- Switch on encoding name (gzip, deflate, br)
- Unknown encodings hit `default` case → throw `InvalidOperationException`

**New behavior:**
- Add handler for unknown encoding → create identity decompressor (body unchanged)
- Log debug: "unknown encoding 'x-custom', passing through unchanged"
- Response flows through unchanged

### Integration Points

- `GroupByHostKeyStage` is used by `ExtractOptionsStage` → part of routing pipeline
- `RetryBidiStage`, `CacheBidiStage` are cross-cutting features
- `ContentEncodingBidiStage` is part of feature pipeline
- All stages share Akka.Streams GraphStageLogic lifecycle

---

## Success Metrics

1. **Zero deadlocks:** All 12 previously-failing tests pass consistently (run 10 times each with no hangs)
2. **Error-H10-005 consistency:** Unknown encoding returns 200 OK every time (100 runs)
3. **No regressions:** All 515 integration tests maintain 100% pass rate
4. **Code quality:** Zero compiler warnings, zero code review issues
5. **Performance:** No performance regression (completion checks add <1µs per substream)

---

## Open Questions

**None — feature scope is well-defined.**

The problem is clearly understood (two distinct bugs with identified root causes and code locations), the fixes are straightforward (two-phase completion, pass-through handler), and success criteria are measurable (all tests pass, zero timeouts).

---

## Implementation Notes

- **Prerequisite:** All dependencies already exist (no external libraries needed)
- **Testing:** Uses existing Kestrel test server and routes
- **CI/CD:** All tests run in `dotnet test` — no external tools required
- **Risk:** Low — changes are localized to two stage classes, affecting only completion logic and error handling
- **Rollback:** Straightforward — revert two methods and one new method
