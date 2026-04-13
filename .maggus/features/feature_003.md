<!-- maggus-id: 987b2859-7cd4-40de-b77a-bf86846eb40d -->
# Feature 003: HTTP/3 Architecture Parity & Advanced Features

## Introduction

HTTP/2's connection handling follows a clean architecture: `StateMachine` (all protocol logic), `ConnectionState` (connection-level state), `StreamTracker` (stream lifecycle), `IHttp2StageOperations` (callback interface), and `Http20ConnectionStage.Logic` (thin Akka adapter). HTTP/3's `Http30ConnectionStage` embeds all logic directly in the stage Logic class with no separation. This feature extracts the HTTP/3 StateMachine pattern to match HTTP/2, adds reconnection support, and implements HTTP/3-specific advanced features (server push control, 0-RTT, connection migration, Alt-Svc discovery).

### Architecture Context

- **Vision alignment:** Architectural consistency across protocol versions; production-grade HTTP/3 reliability
- **Components involved:** Protocol Layer (`Protocol/Http3/` — new StateMachine, StreamTracker, IHttp3StageOperations), Streams Layer (`Http30ConnectionStage` refactor, `Http30Engine`), Client Layer (`Http3Options` from Feature 002)
- **Existing pattern to follow:** `Protocol/Http2/StateMachine.cs` (35 lines config + 400+ lines logic), `Protocol/Http2/StreamTracker.cs` (72 lines), `Protocol/Http2/IHttp2StageOperations` (in StateMachine.cs)
- **Feature 001 precedent:** HTTP/1.x ConnectionStages were consolidated following this exact pattern
- **Prerequisite:** Feature 002 (Http3Options must be populated and wired)

## Goals

- Extract HTTP/3 protocol logic from `Http30ConnectionStage.Logic` into a testable `StateMachine` class
- Create `IHttp3StageOperations` callback interface matching `IHttp2StageOperations` pattern
- Create `Http3StreamTracker` for stream ID allocation and concurrency tracking
- Create `Http3ConnectionConfig` immutable configuration record
- Add reconnection support for QUIC connection drops (NAT rebinding, IP migration, server restarts)
- Add configurable server push rejection via `Http3Options.AllowServerPush`
- Add 0-RTT early data support for idempotent requests
- Add QUIC connection migration support
- Add Alt-Svc / HTTPS DNS discovery for automatic HTTP/3 upgrade

## Tasks

### TASK-003-001: Create Http3ConnectionConfig and IHttp3StageOperations
**Description:** As a protocol layer developer, I want an immutable config record and callback interface for HTTP/3 connection logic so that the StateMachine can be decoupled from the Akka stage.

**Token Estimate:** ~20k tokens
**Predecessors:** none (Feature 002 must be complete, but no task dependency within this feature)
**Successors:** TASK-003-003
**Parallel:** yes — can run alongside TASK-003-002

**Acceptance Criteria:**
- [ ] `Http3ConnectionConfig` record created in `src/TurboHTTP/Protocol/Http3/`:
  ```csharp
  public sealed record Http3ConnectionConfig(
      int MaxFieldSectionSize = 65536,
      int QpackMaxTableCapacity = 4096,
      int QpackBlockedStreams = 100,
      TimeSpan IdleTimeout = default,  // default = 30s in factory
      int MaxReconnectAttempts = 3,
      bool AllowServerPush = false);
  ```
- [ ] `IHttp3StageOperations` interface created in `src/TurboHTTP/Protocol/Http3/`:
  ```csharp
  public interface IHttp3StageOperations
  {
      void OnResponse(HttpResponseMessage response);
      void OnOutbound(Http3Frame frame);
      void OnWarning(string message);
      void OnReconnectFailed();
  }
  ```
- [ ] Follows `IHttp2StageOperations` naming and callback semantics exactly
- [ ] Typecheck passes

**Files to create:**
- `src/TurboHTTP/Protocol/Http3/Http3ConnectionConfig.cs`
- `src/TurboHTTP/Protocol/Http3/IHttp3StageOperations.cs`

**Reference files:**
- `src/TurboHTTP/Protocol/Http2/StateMachine.cs` (lines 11-17 for interface, lines 22-27 for config record)

---

### TASK-003-002: Create Http3StreamTracker
**Description:** As a protocol layer developer, I want a stream tracker for HTTP/3 so that stream ID allocation and concurrency limits are managed consistently with HTTP/2.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-003-003
**Parallel:** yes — can run alongside TASK-003-001

**Acceptance Criteria:**
- [ ] `Http3StreamTracker` class created in `src/TurboHTTP/Protocol/Http3/`
- [ ] Tracks client-initiated bidirectional stream IDs (0, 4, 8, 12, ... — incremented by 4 per RFC 9114 §6.1)
- [ ] `AllocateStreamId()` returns next ID and advances counter by 4
- [ ] `CanOpenStream()` checks against concurrency limit
- [ ] `OnStreamOpened(long streamId)` / `OnStreamClosed(long streamId)` manage active set
- [ ] `Reset()` clears state for reconnection
- [ ] `ActiveStreamCount`, `MaxConcurrentStreams`, `NextStreamId` properties
- [ ] Uses `long` for stream IDs (QUIC uses 62-bit variable-length integers, unlike HTTP/2's 31-bit)
- [ ] Unit tests in `src/TurboHTTP.Tests/Http3/Connection/Http3StreamTrackerSpec.cs`
- [ ] Typecheck passes

**Files to create:**
- `src/TurboHTTP/Protocol/Http3/Http3StreamTracker.cs`
- `src/TurboHTTP.Tests/Http3/Connection/Http3StreamTrackerSpec.cs`

**Reference files:**
- `src/TurboHTTP/Protocol/Http2/StreamTracker.cs` (72 lines — adapt for HTTP/3 stream ID scheme)

---

### TASK-003-003: Create Http3StateMachine
**Description:** As a protocol layer developer, I want a StateMachine class that encapsulates all HTTP/3 connection protocol logic so that it can be tested in isolation without Akka stage overhead.

**Token Estimate:** ~100k tokens
**Predecessors:** TASK-003-001, TASK-003-002
**Successors:** TASK-003-004
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `Http3StateMachine` class created in `src/TurboHTTP/Protocol/Http3/StateMachine.cs`
- [ ] Constructor takes `Http3ConnectionConfig` + `IHttp3StageOperations`
- [ ] Owns `Http3StreamTracker`, `ConnectionState` (moved from Http30ConnectionStage inner class)
- [ ] `ConnectionState` class moved from `Http30ConnectionStage` to `StateMachine` (or extracted to own file)
- [ ] Key methods:
  - `ProcessFrame(Http3Frame frame)` — handles SETTINGS, GOAWAY, PUSH_PROMISE, CANCEL_PUSH, MAX_PUSH_ID, forwards DATA/HEADERS via callback
  - `SendRequest(Http3Frame frame)` — validates GOAWAY not received, tracks stream open, enqueues outbound
  - `bool CanAcceptRequest` — no GOAWAY + not reconnecting + concurrency budget
  - `bool IsReconnecting` / `int ReconnectBufferCount`
  - `OnConnectionLost()` — enters reconnect state, buffers in-flight requests
  - `OnConnectionRestored()` — replays buffered requests via callback
  - `OnReconnectFailed()` — signals callback after max attempts exhausted
- [ ] All existing `Http30ConnectionStage.Logic` protocol logic migrated to StateMachine
- [ ] Unit tests in `src/TurboHTTP.Tests/Http3/Connection/Http3StateMachineSpec.cs` covering:
  - SETTINGS processing
  - GOAWAY handling (single, decreasing stream IDs, invalid IDs)
  - Push promise rejection (AllowServerPush = false)
  - Push promise acceptance (AllowServerPush = true)
  - Stream lifecycle (open/close tracking)
  - Idle timeout expiry
  - Reconnection enter/replay/fail
- [ ] Typecheck passes

**Files to create:**
- `src/TurboHTTP/Protocol/Http3/StateMachine.cs`
- `src/TurboHTTP.Tests/Http3/Connection/Http3StateMachineSpec.cs`

**Reference files:**
- `src/TurboHTTP/Protocol/Http2/StateMachine.cs` (full pattern reference)
- `src/TurboHTTP/Streams/Stages/Decoding/Http30ConnectionStage.cs` (lines 130-350 — ConnectionState to extract; lines 352-601 — Logic to migrate)

---

### TASK-003-004: Refactor Http30ConnectionStage to delegate to StateMachine
**Description:** As a streams layer developer, I want Http30ConnectionStage.Logic to be a thin Akka adapter that delegates all protocol logic to Http3StateMachine, matching the Http20ConnectionStage pattern.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-003-003
**Successors:** TASK-003-005
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [ ] `Http30ConnectionStage` constructor takes `Http3ConnectionConfig` (not just `TimeSpan idleTimeout`)
- [ ] `Logic` class implements `IHttp3StageOperations`
- [ ] `Logic` creates `Http3StateMachine` in constructor
- [ ] All frame handling delegated: `HandleServerFrame` → `_sm.ProcessFrame(frame)`, outbound → `_sm.SendRequest(frame)`
- [ ] `ConnectionState` inner class removed (now lives in StateMachine)
- [ ] `IHttp3StageOperations` callbacks translate to Akka operations:
  - `OnResponse` → `Push(_outApp, frame)`
  - `OnOutbound` → `EnqueueOutbound(frame)`
  - `OnWarning` → `Log.Warning(...)`
  - `OnReconnectFailed` → `FailStage(...)`
- [ ] `Http30Engine` updated to pass `Http3ConnectionConfig` to stage
- [ ] All existing stream tests in `src/TurboHTTP.StreamTests/Http3/` pass unchanged
- [ ] All existing stream tests in `src/TurboHTTP.StreamTests/Http3/Connection/` pass unchanged
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Streams/Stages/Decoding/Http30ConnectionStage.cs` (major refactor)
- `src/TurboHTTP/Streams/Http30Engine.cs` (pass config to stage)

**Reference files:**
- `src/TurboHTTP/Streams/Stages/Http20ConnectionStage.cs` (lines 29-40 — Logic implements IHttp2StageOperations, creates StateMachine)

---

### TASK-003-005: Implement HTTP/3 reconnection support
**Description:** As a library consumer, I want HTTP/3 connections to automatically reconnect when QUIC connections drop so that in-flight requests are retried transparently.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-003-004
**Successors:** TASK-003-006
**Parallel:** no

**Acceptance Criteria:**
- [ ] `Http3StateMachine.OnConnectionLost()` enters reconnect state:
  - Sets `_reconnecting = true`
  - Buffers pending outbound frames in `_reconnectBuffer`
  - Increments `_reconnectAttempts`
- [ ] `Http3StateMachine.OnConnectionRestored()` replays buffer:
  - Resets stream tracker (`_tracker.Reset()`)
  - Replays buffered frames via `_ops.OnOutbound()`
  - Clears `_reconnecting` and `_reconnectBuffer`
- [ ] After `MaxReconnectAttempts` exhausted → `_ops.OnReconnectFailed()`
- [ ] `Http30ConnectionStage.Logic` handles upstream finish during reconnect (fails stage)
- [ ] Unit tests in StateMachine spec: reconnect enter, replay, max attempts
- [ ] Stream test: `Http30ConnectionStageReconnectSpec.cs` — simulates transport failure and recovery
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Protocol/Http3/StateMachine.cs` (add reconnect fields + methods)
- `src/TurboHTTP/Streams/Stages/Decoding/Http30ConnectionStage.cs` (wire reconnect into upstream failure handler)

**Files to create:**
- `src/TurboHTTP.StreamTests/Http3/Http30ConnectionStageReconnectSpec.cs`

**Reference files:**
- `src/TurboHTTP/Protocol/Http2/StateMachine.cs` (lines 54-56 — `_reconnectBuffer`, `_reconnecting`, `_reconnectAttempts`)
- `src/TurboHTTP.StreamTests/Http10/Http10ConnectionStageReconnectSpec.cs` (test pattern)

---

### TASK-003-006: Add configurable server push rejection
**Description:** As a library consumer, I want to control whether server push is accepted or rejected via Http3Options so that I can disable push for bandwidth-sensitive applications.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-003-005
**Successors:** none
**Parallel:** yes — can run alongside TASK-003-007, TASK-003-008, TASK-003-009

**Acceptance Criteria:**
- [ ] `Http3ConnectionConfig.AllowServerPush` (already added in TASK-003-001) flows through to StateMachine
- [ ] When `AllowServerPush = false`: PUSH_PROMISE triggers CANCEL_PUSH frame via `_ops.OnOutbound()`
- [ ] When `AllowServerPush = true`: PUSH_PROMISE is forwarded to app layer
- [ ] `MAX_PUSH_ID` frame sent with appropriate value based on config
- [ ] Unit test: push rejected when disabled, accepted when enabled
- [ ] Existing `Http30PushRejectionSpec` stream test still passes
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Protocol/Http3/StateMachine.cs` (ProcessFrame PUSH_PROMISE branch)

---

### TASK-003-007: Add 0-RTT early data support
**Description:** As a library consumer, I want idempotent requests sent via QUIC 0-RTT so that repeat connections have lower latency.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-003-005
**Successors:** none
**Parallel:** yes — can run alongside TASK-003-006, TASK-003-008, TASK-003-009

**Acceptance Criteria:**
- [ ] `Http3Options.AllowEarlyData` property added (bool, default false)
- [ ] `Http3ConnectionConfig` includes `AllowEarlyData`
- [ ] `Http30Request2FrameStage` checks if request method is idempotent (GET, HEAD, OPTIONS, TRACE, DELETE)
- [ ] Idempotent requests tagged with `EarlyData = true` when `AllowEarlyData` is enabled
- [ ] `QuicTransportStateMachine` / `QuicClientProvider` uses `QuicStream.CanWrite` for early data
- [ ] On 0-RTT rejection: request re-sent after full handshake
- [ ] Unit test: idempotent request marked for early data
- [ ] Unit test: non-idempotent request blocked from early data
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Http3Options.cs`
- `src/TurboHTTP/Protocol/Http3/Http3ConnectionConfig.cs`
- `src/TurboHTTP/Streams/Stages/Encoding/Http30Request2FrameStage.cs`
- `src/TurboHTTP/Transport/Quic/QuicClientProvider.cs`

---

### TASK-003-008: Add QUIC connection migration support
**Description:** As a library consumer on mobile networks, I want HTTP/3 connections to survive IP/port changes via QUIC connection migration so that requests continue without interruption.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-003-005
**Successors:** none
**Parallel:** yes — can run alongside TASK-003-006, TASK-003-007, TASK-003-009

**Acceptance Criteria:**
- [ ] `Http3Options.AllowConnectionMigration` property added (bool, default true)
- [ ] `QuicTransportStateMachine` detects address changes on the QUIC connection
- [ ] When migration detected and allowed: connection continues transparently
- [ ] When migration detected and disallowed: connection closed, new connection established (leverages reconnect from TASK-003-005)
- [ ] Unit test: migration allowed continues seamlessly
- [ ] Unit test: migration disallowed triggers reconnect
- [ ] Typecheck passes

**Files to modify:**
- `src/TurboHTTP/Http3Options.cs`
- `src/TurboHTTP/Transport/Quic/QuicTransportStateMachine.cs`
- `src/TurboHTTP/Transport/Quic/QuicConnectionStage.cs`

---

### TASK-003-009: Add Alt-Svc / HTTPS DNS discovery
**Description:** As a library consumer, I want automatic HTTP/3 discovery via Alt-Svc headers and HTTPS DNS records so that my application can upgrade to HTTP/3 without code changes.

**Token Estimate:** ~75k tokens
**Predecessors:** TASK-003-005
**Successors:** none
**Parallel:** yes — can run alongside TASK-003-006, TASK-003-007, TASK-003-008
**Model:** opus

**Acceptance Criteria:**
- [ ] `AltSvcCache` class created: per-host cache of Alt-Svc directives with TTL
- [ ] Alt-Svc header parsed from HTTP/1.1 and HTTP/2 responses (RFC 7838)
- [ ] `AltSvcEntry` record: protocol, host, port, maxAge, persist flag
- [ ] Integration point in `ProtocolCoreBuilder` or `RequestEnricher`: check cache before version selection
- [ ] If HTTP/3 advertised and QUIC available: upgrade endpoint version to 3.0
- [ ] `Http3Options.EnableAltSvcDiscovery` property (bool, default false — opt-in)
- [ ] Unit tests: Alt-Svc header parsing, cache TTL expiry, upgrade decision
- [ ] Typecheck passes

**Files to create:**
- `src/TurboHTTP/Protocol/AltSvc/AltSvcCache.cs`
- `src/TurboHTTP/Protocol/AltSvc/AltSvcParser.cs`
- `src/TurboHTTP.Tests/AltSvc/AltSvcParserSpec.cs`
- `src/TurboHTTP.Tests/AltSvc/AltSvcCacheSpec.cs`

**Files to modify:**
- `src/TurboHTTP/Http3Options.cs`
- `src/TurboHTTP/Streams/ProtocolCoreBuilder.cs` or `src/TurboHTTP/Streams/Stages/Internal/RequestEnricher.cs`

## Task Dependency Graph

```
TASK-003-001 ──┐
               ├──→ TASK-003-003 ──→ TASK-003-004 ──→ TASK-003-005 ──→ TASK-003-006
TASK-003-002 ──┘                                                   ├──→ TASK-003-007
                                                                   ├──→ TASK-003-008
                                                                   └──→ TASK-003-009
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-003-001 | ~20k | none | yes (with 002) | — |
| TASK-003-002 | ~25k | none | yes (with 001) | — |
| TASK-003-003 | ~100k | 001, 002 | no | opus |
| TASK-003-004 | ~75k | 003 | no | opus |
| TASK-003-005 | ~60k | 004 | no | — |
| TASK-003-006 | ~25k | 005 | yes (with 007, 008, 009) | — |
| TASK-003-007 | ~50k | 005 | yes (with 006, 008, 009) | — |
| TASK-003-008 | ~50k | 005 | yes (with 006, 007, 009) | — |
| TASK-003-009 | ~75k | 005 | yes (with 006, 007, 008) | opus |

**Total estimated tokens:** ~480k

## Functional Requirements

- FR-1: `Http3StateMachine` must encapsulate all HTTP/3 connection protocol logic, testable without Akka
- FR-2: `Http30ConnectionStage.Logic` must implement `IHttp3StageOperations` and delegate to StateMachine
- FR-3: Reconnection must buffer in-flight requests and replay on successful reconnect
- FR-4: Reconnection must fail after `MaxReconnectAttempts` with descriptive exception
- FR-5: Server push must be controllable via `AllowServerPush` option (default: rejected)
- FR-6: 0-RTT must only apply to idempotent HTTP methods
- FR-7: Connection migration must be transparent when enabled, trigger reconnect when disabled
- FR-8: Alt-Svc discovery must be opt-in and cache directives with TTL

## Non-Goals

- No HTTP/3 server implementation (client-only)
- No QPACK encoder/decoder changes (those work independently)
- No changes to shared feature BidiFlow stages (they're protocol-agnostic)
- No HTTP/2 changes — this feature only touches HTTP/3

## Technical Considerations

- `ConnectionState` inner class in `Http30ConnectionStage` (lines 130-350) is ~220 lines — needs careful extraction without breaking existing behavior
- HTTP/3 stream IDs use `long` (62-bit QUIC varint) vs HTTP/2's `int` (31-bit) — StreamTracker must use `long`
- QUIC connection migration is handled at the `System.Net.Quic` level — we expose/control it, not implement it
- Alt-Svc parsing should handle `h3=":443"` and `h3-29=":443"` formats
- Feature 002 MUST be completed before starting this feature

## Success Metrics

- StateMachine unit tests achieve >90% code coverage of protocol logic
- Reconnection succeeds within 3 attempts in stream tests
- All existing Http3 stream tests pass unchanged after refactor
- Server push configurable without code changes (options only)

## Open Questions

None — all questions resolved.
