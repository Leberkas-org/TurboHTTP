# Plan 13: Adapt Http20Engine Tests to Current Architecture

## Introduction

`Http20Engine` was recently updated to wire `PrependPrefaceStage` inline in the outbound path
(between `MergePreferred` and the `BidiFlow` outlet) and to emit `IOutputItem`/`IInputItem`
instead of raw `(IMemoryOwner<byte>, int)` tuples. The stream-test infrastructure in
`EngineTestBase.cs` has partially kept up, but three problems remain:

1. **Dead code** — `H2FakeConnectionStage` and the `SendH2Async`/`SendH2ManyAsync` helpers are
   tuple-based leftovers. No code outside `EngineTestBase.cs` uses them.
2. **Race condition in `H2EngineFakeConnectionStage`** — it serves pre-queued server frames on
   every downstream pull, completely independently of the inbound (outbound-engine) path. For POST
   requests this means the server response can be received — and the test `TaskCompletionSource`
   resolved — *before* the client DATA frame has been written to `OutboundChannel`. The result is
   non-deterministic failures in ENG-002, ENG-004, and ENG-005.
3. **Missing engine-level preface tests** — there are no tests that exercise
   `PrependPrefaceStage` *inside* `Http20Engine` (i.e., via a `ConnectItem` flowing through the
   engine graph). The existing `PrependPrefaceStageTests` and `Http20ConnectionPrefaceRfcTests`
   test the stages in isolation; that isolation must be preserved.

---

## Goals

- Remove all dead tuple-based test infrastructure.
- Make `H2EngineFakeConnectionStage` deterministic: it handles `ConnectItem` itself, strips the
  preface magic bytes from the outbound channel, and couples server-frame serving to inbound
  DataItem reception so that no response is emitted before its corresponding request frame is
  captured.
- Add two new engine-level tests: one that verifies the preface is emitted on the first
  `ConnectItem`, and one that verifies it is *not* emitted on a second `ConnectItem` to the same
  host.
- All six existing ENG tests pass reliably on repeated runs (no non-determinism).

---

## User Stories

### TASK-001: Remove dead tuple-based test infrastructure
**Description:** As a developer, I want the dead `H2FakeConnectionStage` class and the
`SendH2Async`/`SendH2ManyAsync` methods removed from `EngineTestBase.cs` so that the file only
contains active infrastructure.

**Acceptance Criteria:**
- [x] `H2FakeConnectionStage` class (lines 101–161 in current file) is deleted.
- [x] `SendH2Async` method is deleted.
- [x] `SendH2ManyAsync` method is deleted.
- [x] No compilation errors remain after deletion (confirm with `csharp-lsp`).
- [x] `dotnet build` produces 0 errors.

---

### TASK-002: Fix `H2EngineFakeConnectionStage` — ConnectItem handling + race-free server-frame serving
**Description:** As a test author, I want the fake TCP stage to handle `ConnectItem`, strip the
preface magic bytes it may receive, and serve server frames only after receiving the corresponding
client DataItem, so that `OutboundChannel` is always fully populated before the response arrives.

#### Background — why the race exists
`H2EngineFakeConnectionStage` serves pre-queued server frames on any downstream pull, completely
independently of what arrives on the inbound (engine-output) port. In a fully fused Akka graph the
decoder can pull — and fully process — the server SETTINGS + response HEADERS frames before the
encoder has even consumed the client HEADERS frame. When the response is emitted the test's
`TaskCompletionSource` resolves; `SendH2EngineAsync` immediately reads `OutboundChannel`, but a
trailing DATA frame (for POST) may not yet have been written.

#### New behaviour required (per user answer 4B)
The fake stage **itself** must synchronise the two paths:

- `onPull(Out)` — if the next server frame is "unlocked", push it; otherwise set
  `_downstreamWaiting = true` and return (do not push yet).
- `onPush(In)` — three cases:
  1. `ConnectItem` — do **not** write to `OutboundChannel`; instead unlock and push the next
     server frame if downstream is waiting. This handles the preface gate (first `ConnectItem`
     starts the "serve frames" sequence).
  2. `DataItem` whose bytes start with the HTTP/2 magic (`"PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"`,
     24 bytes) — strip those 24 bytes and write only the remainder to `OutboundChannel` (the
     remainder is the client SETTINGS frame, a valid H2 frame). Then unlock the next server frame.
  3. Any other `DataItem` — write bytes to `OutboundChannel`; unlock the next server frame.
  - After every case, call `Pull(In)` to keep the inbound path moving.

"Unlock" means: increment an internal `_unlockedFrames` counter; if `_downstreamWaiting` is true
and the next server frame exists, clear the flag and push it immediately.

This guarantees that server frame N is only emitted after the N-th significant inbound item
(ConnectItem or DataItem) has been fully processed and written to `OutboundChannel`. Therefore,
when the response eventually arrives and `tcs.SetResult` is called, all corresponding outbound
bytes are already in `OutboundChannel`.

**Acceptance Criteria:**
- [ ] `H2EngineFakeConnectionStage.onPush(In)` handles `ConnectItem`, preface-magic `DataItem`,
  and ordinary `DataItem` as described above.
- [ ] `H2EngineFakeConnectionStage.onPull(Out)` only pushes a server frame when `_unlockedFrames > 0`.
- [ ] `H2EngineFakeConnectionStage` constructor signature is unchanged (`params byte[][] serverFrames`).
- [ ] `SendH2EngineAsync` and `SendH2EngineAsyncMany` require **no** additional delays or retries
  to read complete outbound frames.
- [ ] `dotnet build` produces 0 errors.

---

### TASK-003: Verify all six existing ENG tests pass reliably
**Description:** As a developer, I want ENG-001 through ENG-006 to pass on every run after the
race fix, so that the test suite is stable.

**Acceptance Criteria:**
- [ ] Run `dotnet test --filter "FullyQualifiedName~Http20EngineRfcRoundTripTests"` **five times**
  consecutively; all six tests pass every time.
- [ ] ENG-002 asserts both `HeadersFrame` and `DataFrame` in `outboundFrames`.
- [ ] ENG-004 asserts a `SettingsFrame` with `IsAck = true` in `outboundFrames`.
- [ ] ENG-005 asserts three `HeadersFrame`s with stream IDs 1, 3, 5 in `outboundFrames`.

---

### TASK-004: Add engine-level preface tests
**Description:** As a developer, I want two new tests in `Http20EngineRfcRoundTripTests` that
exercise `PrependPrefaceStage` through the full engine graph via `ConnectItem`, so that the
preface emission path is covered at the engine level without touching the isolated stage tests.

> **Scope boundary (user answer 3A):** `PrependPrefaceStageTests` and
> `Http20ConnectionPrefaceRfcTests` are **not** modified; they continue to test the stages in
> isolation.

#### Test ENG-007 — preface emitted on first ConnectItem
Push a `ConnectItem` *before* the request into the engine's inbound port (by materializing a
`Source` that emits `ConnectItem` then the `HttpRequestMessage`). Assert that `OutboundChannel`
contains bytes starting with the HTTP/2 magic (`"PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"`).

> Hint: the engine's inbound type is `HttpRequestMessage`; `ConnectItem` must enter via the
> *transport* (outbound) port, not the request port. The correct approach is to insert a
> `ConnectItem` into the graph on the `IOutputItem` side, e.g., by creating a custom
> `Source<IOutputItem>` that first emits a `ConnectItem` then calls `Pull` on the real engine
> output. Alternatively, materialise the `BidiFlow` with a custom source/sink pair.
>
> If threading through a `ConnectItem` from the request side is not straightforward with the
> current `BidiFlow` shape, test the preface via `PrependPrefaceStage` directly wired into
> `Http20Engine.CreateFlow()`'s output — a small custom graph that merges a `ConnectItem` source
> with the engine's output and asserts the preface in the combined output.

**Acceptance Criteria:**
- [ ] Test `ENG_007_Preface_Emitted_On_First_ConnectItem` exists and passes.
- [ ] Test asserts that outbound bytes to fake TCP begin with the 24-byte magic string.
- [ ] `DisplayName` follows project convention: `"RFC-9113-ENG-007: ..."`

#### Test ENG-008 — preface NOT emitted on second ConnectItem to same host
After the first `ConnectItem` triggers a preface, send a second `ConnectItem` for the same host
and assert that no *additional* preface magic bytes appear in `OutboundChannel`.

**Acceptance Criteria:**
- [ ] Test `ENG_008_Preface_Not_Emitted_On_Second_ConnectItem_Same_Host` exists and passes.
- [ ] `DisplayName` follows project convention: `"RFC-9113-ENG-008: ..."`
- [ ] Typecheck/lint passes.
- [ ] Unit tests for TASK-004 are written and successful.

---

## Functional Requirements

- **FR-1:** `H2FakeConnectionStage`, `SendH2Async`, and `SendH2ManyAsync` must not exist in the
  compiled output.
- **FR-2:** `H2EngineFakeConnectionStage.In` handler must process `ConnectItem` without writing to
  `OutboundChannel`, and must increment `_unlockedFrames`.
- **FR-3:** When a `DataItem` whose bytes begin with the 24-byte HTTP/2 magic arrives, the stage
  must strip those bytes before writing to `OutboundChannel`.
- **FR-4:** A server frame is pushed to `Out` only when `_unlockedFrames > 0`; decrement after
  pushing.
- **FR-5:** `SendH2EngineAsync` and `SendH2EngineAsyncMany` must read `OutboundChannel`
  synchronously (no `Task.Delay` or retry loops) after `tcs.Task` resolves.
- **FR-6:** Two new ENG tests (ENG-007, ENG-008) must cover `PrependPrefaceStage` behaviour at
  engine level.
- **FR-7:** The six existing ENG tests must remain unchanged in intent; only the infrastructure
  they rely on changes.

---

## Non-Goals

- Do not modify `PrependPrefaceStageTests.cs` or `Http20ConnectionPrefaceRfcTests.cs`.
- Do not change `Http20Engine.CreateFlow()` or any production stage code.
- Do not add integration tests (Kestrel-based) in this plan.
- Do not address other HTTP/2 stage tests outside `Http20EngineRfcRoundTripTests.cs`.

---

## Technical Considerations

- **Preface magic constant:** `"PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8` = 24 bytes. Define it as a
  `static ReadOnlySpan<byte>` or `static readonly byte[]` inside `H2EngineFakeConnectionStage`.
- **ConnectItem source:** `ConnectItem` requires `IConnectionOptions`. Use a minimal stub or
  `TcpOptions` with `Host = "example.com"`, `Port = 443` for tests.
- **Unlocked frame counter vs. boolean:** A counter (`int _unlockedFrames`) is more robust than a
  boolean when server frames outnumber DataItems temporarily (e.g., SETTINGS ACK arrives before
  DATA frame is encoded).
- **`csharp-lsp`:** Run semantic validation after every `.cs` change per project rules.

---

## Success Metrics

- `dotnet test --filter "FullyQualifiedName~Http20Engine"` passes 5/5 consecutive runs with 0
  failures.
- `EngineTestBase.cs` no longer references `H2FakeConnectionStage` or tuple-typed `BidiFlow`
  signatures.
- `OutboundChannel` read in `SendH2EngineAsync`/`SendH2EngineAsyncMany` never misses a trailing
  DataFrame.

---

## Open Questions

- Should `H2EngineFakeConnectionStage` expose the preface-stripped bytes separately (for
  assertions in ENG-007/ENG-008), or is it sufficient to assert the raw `OutboundChannel` bytes
  before the frame decoder strips the magic?
- For ENG-007, does `ConnectItem` need to arrive at the `IOutputItem` inlet of the fake stage
  (i.e., pre-engine), or is it injected via a custom source that wraps the engine's output? Clarify
  the exact graph topology before implementing.
