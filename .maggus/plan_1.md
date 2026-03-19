# Plan: Test Suite Quality Analysis & Improvement

## Introduction

The TurboHttp test suite (~1,620 tests across two projects) follows consistent RFC-section naming
conventions and is well-organized overall. However, a detailed analysis reveals four classes of
problems that reduce test quality: exact duplicate tests, under-parametrized test methods, excessive
boilerplate in actor/stream tests, and multi-layer tests that obscure which component failed.

This plan defines the analysis output and the concrete remediation tasks derived from it.

**Scope:**
- `src/TurboHttp.Tests/` — RFC protocol unit tests (~1,080 tests)
- `src/TurboHttp.StreamTests/` — Akka Stage / Actor tests (~540 tests)

---

## Goals

- Eliminate every confirmed duplicate test (zero coverage loss)
- Reduce boilerplate in IO actor tests so setup does not dwarf assertions
- Parametrize repetitive `[Fact]` methods into `[Theory]` where the only difference is input data
- Split multi-layer tests in RFC9113 so a failing assertion points to exactly one protocol layer
- Flag Akka ActorSystem tests that exercise pure protocol logic (no actor behaviour) as candidates
  for lighter infrastructure
- Preserve all existing RFC DisplayName conventions and coverage

---

## Analysis Findings (Input to Tasks)

### Finding A — Exact Duplicate Tests (P0)
`RFC9112/11_DecoderChunkedTests.cs` contains 7 test pairs where two methods assert the exact same
input and expected output under different method names. The likely cause is a copy-paste merge from
an earlier unstructured file into the RFC-section layout.

| Duplicate pair | Scenario |
|---|---|
| Lines 23-33 & 109-120 | Chunk extensions silently ignored |
| Lines 36-43 & 145-154 | Incomplete chunked body → NeedMoreData |
| Lines 46-54 & 135-143 | Non-hex chunk size → ParseError |
| Lines 57-65 & 169-177 | Chunk size overflow → ParseError |
| Lines 84-94 & 157-167 | Single chunk decode |

### Finding B — IO Actor Test Boilerplate (P1)
Four `HostPoolActor*` test files each re-declare `FakeConnectionActor`, `CreatePool()`, and
`CreateHandle()`. Setup code is 60-70% of each file; the actual assertions are 5-10 lines.

Files affected:
- `IO/HostPoolActorTests.cs` (143 lines, 2 tests)
- `IO/HostPoolActorEnsureHostTests.cs` (190 lines, 3 tests)
- `IO/HostPoolActorSelectConnectionTests.cs` (154 lines, 7 tests)
- `IO/HostPoolActorStreamLifecycleTests.cs` (238 lines, 5 tests)

### Finding C — Under-Parametrized [Fact] Methods (P1)
Groups of 8-44 `[Fact]` methods that differ only in one input value (HTTP method, status code,
header name) and share identical logic. Converting these to `[Theory]` + `[InlineData]` reduces
line count 30-40% with no behaviour change and makes gaps in coverage immediately visible.

High-value targets:
- `RFC1945/01_EncoderRequestLineTests.cs` — 9 method tests → 1 `[Theory]`
- `RFC9113/22_EncoderPseudoHeaderTests.cs` — ~44 `[Fact]` → ~10 `[Theory]`
- `StreamTests/Streams/RedirectStageTests.cs` — non-redirect status code pairs → 1 `[Theory]`

### Finding D — Multi-Layer Tests in RFC9113 (P2)
~12-15 test methods in `RFC9113/01_ConnectionPrefaceTests.cs` and nearby files exercise:
1. HpackEncoder (RFC7541 layer)
2. Http2Frame serialization (RFC9113 layer)
3. Decoder reassembly (RFC9113 layer)
4. HPACK decoding (RFC7541 layer)

All in one method. When one fails, the error could originate from any of the four layers.

### Finding E — ActorSystem Overhead for Pure Protocol Tests (P2)
Several `StreamTests` files spin up a full Akka ActorSystem + Materializer to test what is
effectively pure encoding/decoding logic (no actor messaging, no backpressure). Examples:
`Http10/Http10EncoderStageTests.cs`, `Http11/Http11EncoderStageTests.cs`. These tests run
10-50× slower than equivalent RFC unit tests for the same assertions.

---

## User Stories

### TASK-001: Remove Duplicate Tests in RFC9112 ChunkedDecoder
**Description:** As a developer, I want duplicate tests removed so that the test run does not
mislead me about coverage and does not waste CI time.

**Acceptance Criteria:**
- [x] Read `RFC9112/11_DecoderChunkedTests.cs` and confirm all 5 duplicate pairs (Finding A)
- [x] For each pair: keep the version with the RFC DisplayName attribute; delete the other
- [x] After removal: `dotnet test --filter "FullyQualifiedName~DecoderChunked"` still passes
- [x] Total test count in that file decreases by exactly 5 (from current count minus duplicates)
- [x] No new assertions added — pure deletion only

---

### TASK-002: Extract IO Test Base Class
**Description:** As a developer, I want a shared `IoActorTestBase` so that setup boilerplate is
declared once and all four `HostPoolActor*` files contain only test-relevant logic.

**Acceptance Criteria:**
- [ ] Create `TurboHttp.StreamTests/IO/IoActorTestBase.cs` (or `IoTestFixture`) containing:
  - `FakeConnectionActor` (sealed inner class)
  - `CreatePool(TestProbe, TimeSpan)` helper
  - `CreateHandle(IActorRef)` helper
- [ ] All four `HostPoolActor*` test classes inherit from or use the shared type
- [ ] Duplicate declarations of the above helpers are removed from each file
- [ ] All 17 tests in the four files still pass
- [ ] Each test method body is ≤ 20 lines excluding comments

---

### TASK-003: Parametrize HTTP Method Tests in RFC1945 Encoder
**Description:** As a developer, I want method-varying tests expressed as `[Theory]` so that adding
a new HTTP method requires a single `[InlineData]` line rather than a new test method.

**Acceptance Criteria:**
- [ ] Identify all `[Fact]` methods in `RFC1945/01_EncoderRequestLineTests.cs` that differ only
  by HTTP method string
- [ ] Consolidate into one or more `[Theory]` methods with `[InlineData]` per method
- [ ] DisplayName format preserved: `"RFC-section-cat-nnn: description (METHOD)"`
- [ ] Test count for this file decreases; coverage does not
- [ ] `dotnet test --filter "FullyQualifiedName~RFC1945"` all pass

---

### TASK-004: Parametrize Pseudo-Header Tests in RFC9113 Encoder
**Description:** As a developer, I want the ~44 repetitive pseudo-header `[Fact]` tests compressed
into `[Theory]` groups so the file is readable and gaps are visible at a glance.

**Acceptance Criteria:**
- [ ] Read `RFC9113/22_EncoderPseudoHeaderTests.cs` fully before changing anything
- [ ] Group tests by shared assertion pattern (same assertion shape, different header value)
- [ ] Each group becomes one `[Theory]` with `[InlineData]` entries
- [ ] Method count in the file reduces by ≥ 60%
- [ ] All previously passing tests still pass (same number of `[InlineData]` rows as old `[Fact]`
  methods)
- [ ] `dotnet test --filter "FullyQualifiedName~RFC9113"` all pass

---

### TASK-005: Split Multi-Layer RFC9113 Tests into Single-Layer Tests
**Description:** As a developer, I want each RFC9113 test to exercise exactly one protocol layer so
that a test failure immediately identifies which component is broken.

**Acceptance Criteria:**
- [ ] Identify all tests in `RFC9113/` that combine ≥2 of: HpackEncoder, Http2Frame serialization,
  decoder reassembly, HPACK decoding
- [ ] For each identified test: split into separate test methods, one per layer
- [ ] Shared encode/decode helpers are kept private; each test calls only the layer it targets
- [ ] New tests follow RFC DisplayName convention
- [ ] `dotnet test --filter "FullyQualifiedName~RFC9113"` all pass
- [ ] No RFC7541 logic remains in RFC9113 test methods (and vice versa)

---

### TASK-006: Audit ActorSystem Tests That Exercise Pure Protocol Logic
**Description:** As a developer, I want a documented list of StreamTests that spin up a full
ActorSystem purely to test protocol encoding/decoding, so we can decide whether to convert them.

**Acceptance Criteria:**
- [ ] Read all files in `StreamTests/Http10/` and `StreamTests/Http11/`
- [ ] For each test file: note whether the test exercises actor messaging / backpressure OR only
  calls encode/decode and checks bytes
- [ ] Produce `docs/test-infrastructure-audit.md` with a table: File | Test | Uses Actor Behaviour?
  | Recommendation
- [ ] At least one concrete example of a StreamTest converted to a plain `[Fact]` without
  ActorSystem to demonstrate the pattern
- [ ] Converted test must produce the same assertion result and run faster (verified with
  `dotnet test --verbosity detailed`)

---

## Functional Requirements

- FR-1: Every removed test must have been confirmed as a true duplicate (same input, same assertion)
  before deletion
- FR-2: Every `[Theory]` conversion must have the same number of data rows as the `[Fact]` methods
  it replaces
- FR-3: RFC DisplayName format `"RFC-section-cat-nnn: description"` must be preserved in all
  converted tests
- FR-4: No test task may change the behaviour of the production code under test
- FR-5: The build must produce zero new warnings after each task
- FR-6: Run `dotnet test` (full suite) as the final acceptance gate for each task; 0 failures
  required

---

## Non-Goals

- Do NOT add new test coverage for features not yet tested — this plan is quality improvement only
- Do NOT refactor production code (Encoders, Decoders, Stages) as part of these tasks
- Do NOT change test infrastructure in `TurboHttp.IntegrationTests`
- Do NOT change the RFC-section folder layout or file naming scheme
- Do NOT optimise test execution time beyond what TASK-006 explicitly scopes

---

## Technical Considerations

- `StreamTestBase` inherits from `Akka.TestKit.Xunit2.TestKit` — any new base class for IO tests
  must also inherit from this chain or use `IClassFixture<>` to avoid multiple ActorSystem creation
- The `[Theory]` + `[InlineData]` conversion requires the test method signature to accept the
  parametrized value; ensure `DisplayName` templating still compiles with xUnit 2.9.3
- Splitting multi-layer RFC9113 tests may require exposing internal encode/decode helpers — prefer
  `internal` + `[InternalsVisibleTo]` over making them `public`
- Use `csharp-lsp` semantic validation after every `.cs` change (project requirement)

---

## Success Metrics

- Duplicate test count: 7 → 0
- `HostPoolActor*` setup boilerplate: ~300 lines → < 80 lines (shared base class)
- `RFC1945/01` + `RFC9113/22` method count: ~53 `[Fact]` → ≤ 20 `[Theory]` methods, same
  InlineData row count
- Multi-layer RFC9113 tests: 12-15 → 0 (all split into single-layer)
- Full suite `dotnet test`: 0 failures before and after every task

---

## Open Questions

- Should the IO base class be an abstract class (inheritance) or a helper struct/record (composition)?
  Inheritance is simpler but locks the test class hierarchy; composition is more flexible.
- For TASK-006: if a StreamTest that uses ActorSystem is converted to a plain `[Fact]`, should it
  move to `TurboHttp.Tests/` or stay in `TurboHttp.StreamTests/`?
- TASK-005 (multi-layer split) may reveal that some RFC9113 tests have no corresponding RFC7541
  single-layer coverage — should those gaps be filed as new test tasks or deferred?
