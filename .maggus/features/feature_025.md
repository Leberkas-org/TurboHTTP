# Feature 025: HTTP/1.0 Redirect/Retry Reconnection Fix

## Introduction

Fix HTTP/1.0 redirect and retry functionality by enabling the pipeline to emit new connection signals (`ConnectItem`) when connections close. Currently, `ExtractOptionsStage` completes its signal outlet after the first `ConnectItem`, preventing HTTP/1.0 requests (which close connections after each response) from acquiring new connections for redirect chains or retries. The solution adds explicit feedback routing from `ConnectionReuseStage` back to `ExtractOptionsStage` so it can track connection reuse decisions and emit new `ConnectItem` when `ConnectionReuseDecision == Close`.

### Architecture Context

- **Vision alignment:** Enables full HTTP/1.0 protocol support (per RFC 1945) with automatic redirect following and retry handling — core TurboHttp feature
- **Components involved:**
  - `ExtractOptionsStage` (Streams/Stages/Routing/) — request routing and connection signaling
  - `ConnectionReuseStage` (Streams/Stages/Features/) — RFC 9112 §9 reuse evaluation
  - `ProtocolCoreGraphBuilder` (Streams/) — pipeline wiring and graph construction
  - `ConnectionStage` (Transport/) — connection lifecycle management
  - `RedirectBidiStage` and `RetryBidiStage` (Streams/Stages/Features/) — feedback loop producers
- **New patterns:** Feedback routing from reuse decision stages back to request coordinator; conditional control-flow emission based on protocol-specific connection semantics
- **Architectural update needed:** None — this fix completes the existing feedback loop design (comment at ConnectionStage:147 shows original intent)

## Goals

- Enable HTTP/1.0 clients to follow redirect chains (301/302/303/307/308) without blocking on connection acquisition
- Enable HTTP/1.0 clients to retry eligible requests without blocking on connection acquisition
- Preserve HTTP/1.1 keep-alive behavior (single `ConnectItem` per substream lifetime)
- Preserve HTTP/2 multiplexing behavior (single `ConnectItem` per substream lifetime)
- Pass all 23 existing HTTP/1.0 integration tests (`RedirectH10IntegrationTests`, `RetryH10IntegrationTests`)
- Add unit tests for `ExtractOptionsStage` reconnection logic

## Tasks

### TASK-025-001: Analyze Current ExtractOptionsStage and Document State Machine
**Description:** As an architect, I want to understand the exact current behavior of `ExtractOptionsStage` (state fields, completion timing, handler logic) so that I can design the reconnection feature without breaking HTTP/1.1/2 behavior.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-025-002
**Parallel:** yes — can read in parallel with other setup

**Acceptance Criteria:**
- [x] Document current state machine: First request → `ConnectItem` emitted → `_outSignal` completed → pending requests pass through
- [x] Identify exact lines where `Complete(_outSignal)` is called (ExtractOptionsStage.cs:47)
- [x] Trace request flow for HTTP/1.1 (single `ConnectItem` per substream) vs HTTP/1.0 (multiple `ConnectItem` per substream needed)
- [x] Document feedback loop expectation from ConnectionReuseStage
- [x] Create diagram or pseudocode showing desired state machine with reuse feedback inlet
- [x] Identify all test scenarios: HTTP/1.0 single redirect, HTTP/1.0 chain, HTTP/1.1 no change, HTTP/2 no change
- [x] Summary document saved to `.maggus/analysis/feature_025_state_machine.md`

### TASK-025-002: Modify ExtractOptionsStage — Add Feedback Inlet and Reuse Tracking
**Description:** As a stream engineer, I want to add a feedback inlet to `ExtractOptionsStage` that receives `ConnectionReuseItem` signals and tracks reuse decisions so that I can emit new `ConnectItem` when connections must be closed.

**Token Estimate:** ~45k tokens
**Predecessors:** TASK-025-001
**Successors:** TASK-025-003, TASK-025-004
**Parallel:** no — foundational change required

**Acceptance Criteria:**
- [x] File: `src/TurboHttp/Streams/Stages/Routing/ExtractOptionsStage.cs`
- [x] Add new inlet field: `private readonly Inlet<IControlItem> _inReuse = new("ExtractOptions.In.Reuse")`
- [x] Add shape field to track reuse inlet in constructor
- [x] Remove `Complete(_outSignal)` call from line 47 — outlet remains open
- [x] Add state field: `private ConnectionReuseDecision _lastReuse = ConnectionReuseDecision.Reuse` (default to no-close)
- [x] Add handler for `_inReuse`: Grab incoming `ConnectionReuseItem`, extract decision, update `_lastReuse`, then emit new `ConnectItem` if decision is `Close`
- [x] Modify push handler for `_outRequest` inlet:
  - If first request (no `ConnectItem` sent yet): emit `ConnectItem` to `_outSignal`, then emit request to `_outRequest`
  - If not first request AND `_lastReuse == ConnectionReuseDecision.Close`: emit new `ConnectItem` to `_outSignal`, then emit request to `_outRequest`
  - Otherwise: emit request to `_outRequest` only
- [x] Ensure shape definition includes new `_inReuse` inlet (FanInShape or custom multi-port shape)
- [x] Typecheck/lint passes with zero warnings
- [x] No changes to outlet names or structure — preserve backward compatibility with downstream stages

### TASK-025-003: Update ProtocolCoreGraphBuilder — Wire Feedback Path
**Description:** As a stream architect, I want to wire the feedback path from `ConnectionReuseStage` to `ExtractOptionsStage` in the pipeline graph so that reuse decisions are routed back to the request coordinator.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-025-002
**Successors:** TASK-025-005
**Parallel:** no — depends on ExtractOptionsStage API changes

**Acceptance Criteria:**
- [x] File: `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` method `BuildConnectionFlow<TEngine>`
- [x] Locate `connReuse` stage (line ~109) and `extract` stage (line ~103)
- [x] Connect signal outlet of `connReuse` to new feedback inlet `_inReuse` of `extract`:
  - `builder.From(connReuse.Out1).To(extract._inReuse)` or equivalent
- [x] Verify graph connectivity: no dangling inlets, all outlets routed
- [x] Document the feedback loop in a comment: "ConnectionReuseItem signals fed back to ExtractOptionsStage to trigger reconnect for HTTP/1.0"
- [x] Typecheck/lint passes
- [x] No changes to transport merge or downstream paths

### TASK-025-004: Write Unit Tests for ExtractOptionsStage Reconnection Logic
**Description:** As a test engineer, I want to write focused unit tests for `ExtractOptionsStage` that verify the reconnection state machine works correctly for HTTP/1.0, HTTP/1.1, and HTTP/2 scenarios.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-025-002
**Successors:** TASK-025-005
**Parallel:** yes — can write tests in parallel with TASK-025-003

**Model:** opus — state machine testing is intricate

**Acceptance Criteria:**
- [ ] New file: `src/TurboHttp.StreamTests/Streams/Routing/ExtractOptionsStageReconnectTests.cs`
- [ ] Test class: `ExtractOptionsStageReconnectTests : StreamTestBase`
- [ ] Test: `HTTP10_FirstRequest_EmitsConnectItem()` — verify first request triggers `ConnectItem` emission
- [ ] Test: `HTTP10_SecondRequest_AfterReuseFalse_EmitsNewConnectItem()` — verify `ConnectionReuseItem(Close)` triggers new `ConnectItem`
- [ ] Test: `HTTP10_ThirdRequest_AfterReuseTrue_SkipsConnectItem()` — verify `ConnectionReuseItem(Reuse)` does NOT emit new `ConnectItem`
- [ ] Test: `HTTP11_FirstRequest_EmitsConnectItem()` — verify single `ConnectItem` for HTTP/1.1
- [ ] Test: `HTTP11_SecondRequest_WithReuseTrue_SkipsConnectItem()` — verify keep-alive doesn't re-emit
- [ ] Test: `HTTP20_FirstRequest_EmitsConnectItem()` — verify HTTP/2 gets single `ConnectItem`
- [ ] Test: `HTTP20_SecondRequest_WithReuseFalse_SkipsConnectItem()` — verify HTTP/2 doesn't reconnect even on close signal (multiplexing)
- [ ] Test: `MultipleRedirects_EmitsConnectItemPerClose()` — simulate 3-hop redirect chain: emit ConnectItem → request → reuse-close → emit ConnectItem → request → etc.
- [ ] All tests use `Source`, `Sink`, `RunnableGraph`, and materializer (StreamTestBase pattern)
- [ ] All tests have explicit timeout via `[Fact(Timeout = 5000)]`
- [ ] Build and tests pass: `dotnet test src/TurboHttp.StreamTests.csproj --filter "*ExtractOptionsStageReconnect*"`

### TASK-025-005: Run Integration Tests — Redirect and Retry (HTTP/1.0)
**Description:** As a QA engineer, I want to run the full HTTP/1.0 redirect and retry integration test suites to verify end-to-end functionality works after the reconnection fix.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-025-002, TASK-025-003, TASK-025-004
**Successors:** TASK-025-006
**Parallel:** no — requires all prior changes

**Acceptance Criteria:**
- [ ] Run: `dotnet test src/TurboHttp.IntegrationTests.csproj --filter "FullyQualifiedName~RedirectH10IntegrationTests"`
- [ ] Verify all tests pass (expected: 14 tests, all pass)
  - `Get_301_Redirect_Follows_To_Hello`, `Get_302_Redirect_Follows_To_Hello`, `Get_307_Redirect_Follows_To_Hello`, `Get_308_Redirect_Follows_To_Hello`
  - `Redirect_Chain_Follows_N_Hops_To_Hello` (N=1, 3, 5)
  - `Infinite_Redirect_Loop_Returns_Final_Redirect_Response`
  - `Relative_Location_Header_Resolved_To_Hello`
  - `Cross_Scheme_Downgrade_Blocked_By_Default`
  - `Post_307_Preserves_Method_And_Body`, `Post_303_Rewrites_To_Get`, `Post_302_Rewrites_To_Get`, `Post_308_Preserves_Method_And_Body`
  - `Cross_Origin_Redirect_Follows_To_Headers_Echo`
  - `Cross_Origin_Redirect_Strips_Authorization`
- [ ] Run: `dotnet test src/TurboHttp.IntegrationTests.csproj --filter "FullyQualifiedName~RetryH10IntegrationTests"` (if exists)
- [ ] Verify all tests pass
- [ ] No timeout failures (all tests complete within 60 seconds per test)
- [ ] No connection acquisition errors in logs
- [ ] Document any test failures or unexpected behavior

### TASK-025-006: Regression Testing — Verify HTTP/1.1 and HTTP/2 Unaffected
**Description:** As a test engineer, I want to run the full test suite (stream tests and integration tests) to verify that the reconnection changes do not break HTTP/1.1 or HTTP/2 existing functionality.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-025-005
**Successors:** TASK-025-007
**Parallel:** no — full test suite run

**Acceptance Criteria:**
- [ ] Run: `dotnet test src/TurboHttp.StreamTests.csproj --filter "*" -v normal`
  - Verify: All RFC9112 stream tests pass (HTTP/1.1 encoder/decoder/chunked/correlation/pipeline)
  - Verify: All RFC9113 stream tests pass (HTTP/2 encoder/decoder/connection/stream)
  - Expected: Zero failures, zero regressions
- [ ] Run: `dotnet test src/TurboHttp.Tests.csproj --filter "*" -v normal`
  - Verify: All RFC9112 unit tests pass (HTTP/1.1 protocol)
  - Verify: All RFC9113 unit tests pass (HTTP/2 protocol)
  - Expected: Zero failures
- [ ] Run: `dotnet test src/TurboHttp.IntegrationTests.csproj --filter "*" -v normal`
  - Verify: All HTTP/1.1 integration tests pass (if any exist beyond redirects)
  - Verify: All HTTP/2 integration tests pass (if any exist beyond redirects)
  - Expected: Zero failures
- [ ] Build: `dotnet build --configuration Release src/TurboHttp.sln`
  - Expected: Zero errors, zero warnings
- [ ] Document test results in a summary table (passed/failed counts per RFC section)

### TASK-025-007: Verification Gate — Full Build and Test Suite
**Description:** As a build engineer, I want to run the full build and test suite to validate that all changes compile cleanly, all tests pass, and there are no regressions before marking the feature complete.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-025-006
**Successors:** none
**Parallel:** no — final validation step

**Acceptance Criteria:**
- [ ] Run: `dotnet build --configuration Release src/TurboHttp.sln`
  - Expected: Build succeeds, zero errors, zero warnings
  - Verify: No new compiler warnings introduced
- [ ] Run: `dotnet test src/TurboHttp.sln --configuration Release -v normal --no-build`
  - Expected: All tests pass
  - Count: ~1500+ tests (RFC unit + stream + integration)
  - Verify: Zero failures, zero timeouts
- [ ] Smoke check: Run HTTP/1.0 single redirect manually via `dotnet run --configuration Release --project src/TurboHttp.Tests -- --filter "Redirect-H10-001"` to see it passes
- [ ] Create summary document: `.maggus/verification/feature_025_verification_report.md`
  - Sections: Build Status, Test Summary (passed/failed counts), Performance Baseline (if applicable), Open Issues (none expected)
  - Include: Full test output snippet showing all tests passed
- [ ] Verify: All acceptance criteria from TASK-025-002, 003, 004, 005, 006 complete
- [ ] Typecheck/lint: `dotnet format --verify-no-changes --verbosity diagnostic src/TurboHttp.sln` passes

## Task Dependency Graph

```
TASK-025-001 ──→ TASK-025-002 ──→ TASK-025-003 ──┐
                                                  ├──→ TASK-025-005 ──→ TASK-025-006 ──→ TASK-025-007
                  TASK-025-002 ──→ TASK-025-004 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-025-001 | ~15k | none | yes | — |
| TASK-025-002 | ~45k | 001 | no | opus |
| TASK-025-003 | ~20k | 002 | no | — |
| TASK-025-004 | ~60k | 002 | yes (with 003) | opus |
| TASK-025-005 | ~10k | 002, 003, 004 | no | — |
| TASK-025-006 | ~15k | 005 | no | — |
| TASK-025-007 | ~20k | 006 | no | — |

**Total estimated tokens:** ~185k

## Functional Requirements

- FR-1: `ExtractOptionsStage` must emit a new `ConnectItem` when receiving `ConnectionReuseItem(Close)` signal
- FR-2: `ExtractOptionsStage` must not emit a new `ConnectItem` when receiving `ConnectionReuseItem(Reuse)` signal
- FR-3: First request in a substream must always emit `ConnectItem` (unchanged from current behavior)
- FR-4: HTTP/1.0 clients must successfully follow redirect chains (3+ hops) without connection acquisition errors
- FR-5: HTTP/1.0 clients must successfully retry eligible requests without connection acquisition errors
- FR-6: HTTP/1.1 clients must continue to receive exactly one `ConnectItem` per substream (keep-alive preserved)
- FR-7: HTTP/2 clients must continue to receive exactly one `ConnectItem` per substream (multiplexing preserved)
- FR-8: Feedback routing from `ConnectionReuseStage` to `ExtractOptionsStage` must be explicit and traceable in the graph
- FR-9: Stage inlet/outlet names must follow project naming convention (PascalCase, no duplicates)
- FR-10: All existing tests must pass; zero regressions

## Non-Goals

- No changes to `ConnectionStage` or `ConnectionReuseStage` logic — only wiring and `ExtractOptionsStage` modification
- No HTTP/3 (QUIC) reconnection — HTTP/3 uses different connection model (stream-oriented)
- No changes to redirect or retry evaluator logic — only the connection acquisition mechanism
- No performance optimization in this iteration — focus is correctness and completeness
- No documentation updates to CLAUDE.md — the fix completes existing design (no new patterns)

## Technical Considerations

- **GraphStage shape:** `ExtractOptionsStage` currently uses custom multi-port shape. Adding `_inReuse` inlet requires updating shape definition carefully to avoid breaking graph wiring
- **Backpressure:** Feedback inlet must respect backpressure — if pull from `_outRequest` is not available, `_inReuse` must buffer or wait (use standard Akka.Streams backpressure protocol)
- **Signal ordering:** `ConnectionReuseItem` from the response path must arrive before the next request tries to pass through the request path — Akka.Streams guarantees order within a substream
- **HTTP/1.0 semantics:** RFC 1945 states connections close by default unless `Connection: Keep-Alive` is explicitly set — TurboHttp's `ConnectionReuseEvaluator` handles this, so `ExtractOptionsStage` just consumes the decision
- **HTTP/1.1 semantics:** RFC 9112 §9 states connections persist by default (keep-alive) unless `Connection: close` header is present — `ExtractOptionsStage` should not see `Close` decision under normal circumstances for HTTP/1.1
- **HTTP/2 semantics:** RFC 9113 prohibits closing the connection after a single stream (multiplexing) — `ExtractOptionsStage` should never emit new `ConnectItem` for HTTP/2 even if `Close` signal arrives (may want to add assertion)
- **No architectural change:** The design (feedback loop) was anticipated in the original code (comment at ConnectionStage:147), so this fix is completing intended behavior, not introducing new patterns

## Success Metrics

- All 14 HTTP/1.0 redirect integration tests pass (Redirect-H10-001 through Redirect-H10-014)
- All HTTP/1.0 retry integration tests pass (if RetryH10IntegrationTests exists)
- All 1500+ existing unit/stream/integration tests pass (zero regressions)
- Build with zero errors and zero warnings
- `ExtractOptionsStage` unit tests achieve >90% code coverage for reconnection logic
- No performance regression compared to baseline (redirects may be slightly slower due to extra ConnectItem emissions, but should be < 1% impact)

## Open Questions

None — all design decisions clarified from previous session analysis.

---

## Summary for Execution

**Objective:** Enable HTTP/1.0 redirect and retry functionality by adding explicit feedback routing from `ConnectionReuseStage` back to `ExtractOptionsStage`, allowing per-hop connection acquisition.

**Key Deliverables:**
1. State machine analysis and documentation of current behavior
2. Modified `ExtractOptionsStage` with feedback inlet and reconnection logic
3. Updated `ProtocolCoreGraphBuilder` wiring the feedback path
4. Comprehensive unit tests for reconnection state machine (8+ test cases)
5. Integration tests passing (14+ HTTP/1.0 redirect tests)
6. Full regression testing (1500+ tests, zero failures)
7. Verification gate report

**Why this matters:** Completes HTTP/1.0 protocol support — a core requirement for TurboHttp to be production-grade. HTTP/1.0 clients can now automatically follow redirects and retry requests without manual connection management, matching the user experience of HTTP/1.1 and HTTP/2 clients.

**Risk assessment:** LOW
- Isolated to `ExtractOptionsStage` and one wiring change in `ProtocolCoreGraphBuilder`
- No changes to core protocol engines, encoders, decoders, or transport layer
- HTTP/1.1 and HTTP/2 unaffected (they don't trigger `Close` decisions under normal circumstances)
- Extensive test coverage validates behavior change
