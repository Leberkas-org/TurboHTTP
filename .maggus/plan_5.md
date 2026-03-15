# Plan 5: Gap Analysis — What Is Missing for a Production-Ready TurboHttp Client?

## Introduction

What is unclear right now:
- Which architecture is correct for production?
- Which Plan 5 tasks are still relevant, and which are obsolete?
- What exactly is missing for a production-ready HTTP client with real connection reuse?

**This plan has three phases:**
1. **Audit** — systematically capture the current state
2. **Decision** — choose the architecture
3. **Roadmap** — define the concrete gap tasks

---

## Goals

- Complete picture of the current implementation state (what exists, what does what)
- Clear architectural decision: ConnectionStage-direct vs. Actor Pool vs. Hybrid
- Prioritised list of missing features for production-readiness
- Identify connection reuse (Keep-Alive + HTTP/2 Multiplexing) as the critical path and plan for it
- Formally assess Plan 5a/5b: relevant, partially relevant, or obsolete

---

## User Stories

---

### TASK-AUD-001: Read and Document the Full Engine Wiring

**Description:** As a developer, I want to know exactly which stages are wired in `Engine.cs` and which only exist but are never used.

**Acceptance Criteria:**
- [x] `Engine.cs` fully read — all paths (BasicPipeline, ExtendedPipeline, BuildConnectionFlow)
- [x] Table created: which stage is wired where? (ConnectionStage, ConnectionPoolStage, ConnectionReuseStage, etc.)
- [x] Clear answer: does `Engine.cs` use `ConnectionPoolStage` (Plan 4 Actor Pool)? Yes/No
- [x] Clear answer: is `ConnectionReuseStage` actively wired? Yes/No
- [x] Result documented in `.maggus/PROGRESS_7.md`

---

### TASK-AUD-002: Plan 4 Actor Pool — Integration Status Check

**Description:** As a developer, I want to know whether `PoolRouterActor` / `HostPoolActor` / `ConnectionPoolStage` are used anywhere in production code, or whether they are only tested but never wired.

**Acceptance Criteria:**
- [ ] Grep for `ConnectionPoolStage` and `PoolRouterActor` in non-test code
- [ ] Clear answer: is the Actor Pool used in `Engine.cs` or `TurboHttpClient`? Yes/No
- [ ] If no: assessment of what would need to happen to integrate it
- [ ] Result documented in `.maggus/PROGRESS_7.md`

---

### TASK-AUD-003: Connection Reuse — Behavioural Check

**Description:** As a developer, I want to know empirically whether HTTP Keep-Alive works — whether the client actually reuses TCP connections across requests or reconnects every time.

**Acceptance Criteria:**
- [ ] Existing integration tests searched for HTTP/1.1 connection reuse (Kestrel `/conn/keep-alive`, `/conn/close`)
- [ ] Clear answer: are existing connections reused across multiple requests?
- [ ] Clear answer: does HTTP/2 multiplexing work (multiple requests over one TCP connection)?
- [ ] If no test covers this: trace through `Http11ConnectionTests` or `Http11BasicTests` manually
- [ ] Result (keep-alive: ✅/❌, http2-multiplex: ✅/❌) documented in `.maggus/PROGRESS_7.md`

---

### TASK-AUD-004: Error Tolerance — What Happens on TCP Connection Drop?

**Description:** As a developer, I want to know whether the stream keeps running when a single TCP connection drops.

**Acceptance Criteria:**
- [ ] `ConnectionStage` checked for reconnect logic (does `TryConnect()` fire on `ClientDisconnected`?)
- [ ] Clear answer: does the full `Engine` flow survive a TCP drop?
- [ ] Clear answer: does a failure in one connection propagate failures to all parallel connections?
- [ ] Clear answer: is there exponential backoff / retry on reconnect?
- [ ] Result documented in `.maggus/PROGRESS_7.md`


### TASK-AUD-005: TurboHttpClient.SendAsync — End-to-End Status

**Description:** As a developer, I want to know whether `TurboHttpClient.SendAsync()` works end-to-end, and if so, which features (Cookies, Cache, Retry, Redirect) actually flow through the pipeline.

**Acceptance Criteria:**
- [ ] `TurboHttpClient.cs` and `TurboClientStreamManager.cs` fully read
- [ ] Clear answer: is `SendAsync()` fully wired? Are there still commented-out sections?
- [ ] Clear answer: which `TurboClientOptions` features are covered by integration tests?
- [ ] Clear answer: which features exist as a Stage but have no integration test?
- [ ] Result documented in `.maggus/PROGRESS_7.md`

---

### TASK-DEC-001: Document and Make the Architecture Decision

**Description:** As a developer, we want to jointly fix the architecture: should `Engine.cs` use the Actor Pool (Plan 4), keep the current `ConnectionStage`, or use a hybrid solution?

**Acceptance Criteria:**
- [ ] Three options with pros/cons documented in `.maggus/ARCHITECTURE_DECISION.md`:
  - **Option A**: `ConnectionStage` direct (status quo) — simpler, no actor overhead
  - **Option B**: `ConnectionPoolStage` via Actor Pool (Plan 4) — real pool management, idle eviction, supervision
  - **Option C**: Hybrid — `ConnectionStage` + `ConnectionReuseStage` properly wired (no actor pool in the hot path)
- [ ] Each option evaluated for: effort, connection reuse, error tolerance, HTTP/2 multiplex
- [ ] Decision made and justified (user decides based on the document)
- [ ] Result documented in `.maggus/PROGRESS_7.md`

---

### TASK-GAP-001: Create the Production-Readiness Gap List

**Description:** As a developer, I want a prioritised list of all missing features for a production-ready client — based on the audit results and the architecture decision.

**Acceptance Criteria:**
- [ ] Gap list created in `.maggus/GAP_LIST.md`
- [ ] Each gap has: title, priority (Critical/High/Medium/Low), estimated complexity (S/M/L), dependencies
- [ ] At minimum, the following areas checked:
  - Connection Reuse / Keep-Alive (critical per user input)
  - HTTP/2 Multiplexing over a shared pool
  - Error tolerance / Auto-Reconnect
  - Graceful Shutdown (no hanging connections)
  - Per-Host Connection Limits (PerHostConnectionLimiter integration)
  - TurboHttpClient as a complete public API
  - Missing integration tests for extended pipeline (Cookies + Cache + Retry + Redirect together)
  - HTTPS / TLS fully working?
  - Plan 5a/5b tasks still marked OPEN
- [ ] List sorted by priority

---

### TASK-ROAD-001: Roadmap — Ordered Implementation Tasks

**Description:** As a developer, I want a clear next plan (`plan_8.md`) with concrete, atomic tasks based on the gap list and the architecture decision.

**Acceptance Criteria:**
- [ ] `plan_8.md` created with tasks from the Critical and High gaps
- [ ] Tasks are atomic (independently mergeable, independently testable)
- [ ] Task order respects dependencies
- [ ] Each task has acceptance criteria including `dotnet build` + `dotnet test` verification
- [ ] `.maggus/PROGRESS_7.md` final summary written

---

## Functional Requirements

- **FR-1**: After TASK-AUD-001 it is clear which stages are active in `Engine.cs`
- **FR-2**: After TASK-AUD-002 it is clear whether the Actor Pool (Plan 4) is used in production
- **FR-3**: After TASK-AUD-003 it is clear whether Connection Reuse (Keep-Alive / HTTP/2) works
- **FR-4**: After TASK-AUD-004 it is clear how the stack responds to TCP errors
- **FR-5**: After TASK-AUD-005 every Plan 5 task has a status (DONE/OPEN/OBSOLETE)
- **FR-6**: After TASK-AUD-006 it is clear which features work end-to-end
- **FR-7**: After TASK-DEC-001 an architecture decision is documented and made
- **FR-8**: After TASK-GAP-001 a prioritised gap list exists with complexity estimates
- **FR-9**: After TASK-ROAD-001 `plan_8.md` exists with concrete next implementation tasks
- **FR-10**: `dotnet build ./src/TurboHttp.sln` stays green throughout the analysis (no code changes during Audit/Decision phase)

---

## Non-Goals

- No code changes during the audit phase (TASK-AUD-*)
- No new features implemented before TASK-DEC-001 is complete
- No refactoring of the Plan 4 Actor Pool without an architecture decision
- Plan 5a/5b are **not** simply implemented 1:1 — assessment first, then decision

---

## Technical Considerations

### Suspected Gaps (already visible now, to be confirmed)

| Area | Suspicion | To be checked in |
|------|-----------|-----------------|
| Connection Reuse | `ConnectionReuseStage` exists but is not wired in `Engine.cs` | TASK-AUD-001 |
| Keep-Alive | `ConnectionStage` reconnects, but no idle pool → no true reuse | TASK-AUD-003 |
| Error Propagation | Balance-based multi-connection flow — one dead connection kills the whole branch | TASK-AUD-004 |
| Plan 5a SinkRef | Architecture never implemented, different path was taken | TASK-AUD-005 |

All audit tasks can run in parallel. TASK-DEC-001 requires all audit results.

---

## Success Metrics

- All 6 audit tasks: yes/no questions fully answered
- `ARCHITECTURE_DECISION.md` exists with a decision made
- `GAP_LIST.md` has ≥ 8 documented gaps with priority and complexity
- `plan_8.md` has ≥ 5 concrete, atomic implementation tasks
- No tests break during the analysis

---

## Open Questions

- Is the SinkRef/SourceRef idea (Plan 5a) still interesting for future distributed scenarios, or completely obsolete?
- Should `TurboHttpClient` offer an `IHttpClientFactory`-compatible API?
- Are there observability requirements (metrics, logging) that belong in the gap plan?
