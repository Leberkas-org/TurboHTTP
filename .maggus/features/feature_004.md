# Feature 004: Version-aware ConnectionActor Architecture + SmokeTests

## Introduction

The current `ConnectionActor` (334 lines) is monolithic and uses `_isMultiStream = options is QuicOptions` to distinguish between HTTP/1.x, HTTP/2, and HTTP/3. Every handler method contains version branches (`if (_isMultiStream) { ... } else { ... }`), hurting maintainability and inviting bugs.

**Concrete problem:** The HTTP/2 h2c SmokeTest hangs because `HostPool.MarkBusy()` is called before stream allocation — a timing bug that only exists because version-specific logic is scattered across multiple classes instead of being encapsulated in a dedicated actor.

This feature refactors `ConnectionActor` into a class hierarchy with `ConnectionActorBase` and three version-specific subclasses. Afterwards, SmokeTests for all HTTP versions (1.0, 1.1, 2.0, 3.0, TLS) are implemented as a validation gate.

### Architecture Context

- **Components involved:**
  - `src/TurboHttp/Pooling/ConnectionActor.cs` — monolithic actor, to be split
  - `src/TurboHttp/Pooling/HostPool.cs` — `SpawnConnection()` switches to version-based Props
  - `src/TurboHttp/Pooling/ConnectionState.cs` — unchanged
  - `src/TurboHttp/Transport/ConnectionStage.cs` — unchanged (communicates only via `ConnectionHandle`)
  - `src/TurboHttp/Transport/ClientManager.cs` — unchanged
  - `src/TurboHttp.IntegrationTests/SmokeTests.cs` — extended for all versions
  - `src/TurboHttp.IntegrationTests/Shared/` — fixtures already exist
- **No new external frameworks required**
- **ConnectionHandle remains unchanged** — the interface between actor and stage is not affected

## Goals

- Split `ConnectionActor` into `ConnectionActorBase` + `Http1ConnectionActor` + `Http2ConnectionActor` + `Http3ConnectionActor`
- Eliminate all `_isMultiStream` branches — each subclass knows only its own logic
- HTTP/2 h2c SmokeTest passes (no hang due to MarkBusy/StreamAcquired timing)
- HTTP/3 QUIC logic (SharedProvider, MultiRunner) isolated in `Http3ConnectionActor`
- SmokeTests for all 5 variants (HTTP/1.0, 1.1, 2.0, 3.0, TLS) green
- No regression in existing unit and stream tests

## Tasks

### TASK-004-001: Extract ConnectionActorBase

**Description:** As a developer, I want to extract an abstract base class `ConnectionActorBase` so that shared logic (channel management, reconnect, message forwarding) lives centrally and subclasses only implement version-specific behaviour.

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-004-002, TASK-004-003, TASK-004-004
**Parallel:** no — foundation for all subsequent tasks
**Model:** opus

**Acceptance Criteria:**
- [x] `ConnectionActorBase` created in `src/TurboHttp/Pooling/ConnectionActorBase.cs`
- [x] Abstract class inheriting from `ReceiveActor`
- [x] Shared fields: `_options`, `_clientManager`, `_requestEndpoint`, `_config`, `_out`, `_in`, `_runner`, `_reconnectAttempt`, `_log`
- [x] Shared messages: `ConnectionReady`, `DoReconnect`, `DoClose` remain here
- [x] Shared logic: `Reconnect()`, `AttemptReconnect()`, channel reset, exponential backoff
- [x] Abstract/virtual methods: `Connect()`, `HandleConnected()`, `HandleDisconnected()`, `HandleTerminated()`, `OnPostStop()`
- [x] Message forwarding to parent: `MarkConnectionNoReuse`, `StreamCompleted`, `StreamAcquired`, `UpdateMaxConcurrentStreams`
- [x] Build green: `dotnet build --configuration Release src/TurboHttp.sln`

### TASK-004-002: Implement Http1ConnectionActor

**Description:** As a developer, I want an `Http1ConnectionActor` that encapsulates the simple TCP connect/disconnect logic for HTTP/1.0 and HTTP/1.1, without any multi-stream branching.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-005
**Parallel:** yes — can run alongside TASK-004-003 and TASK-004-004

**Acceptance Criteria:**
- [x] `Http1ConnectionActor` created in `src/TurboHttp/Pooling/Http1ConnectionActor.cs`
- [x] Inherits from `ConnectionActorBase`
- [x] `Connect()` — simple `ClientManager.CreateRunnerWithChannels` without SharedProvider
- [x] `HandleConnected()` — sets `_runner`, sends `ConnectionReady` to parent
- [x] `HandleDisconnected()` / `HandleTerminated()` — calls `Reconnect()` directly
- [x] `PostStop()` — stops single runner
- [x] No `_isMultiStream`, no `_sharedProvider`, no `_activeRunners`
- [x] Build green

### TASK-004-003: Implement Http2ConnectionActor

**Description:** As a developer, I want an `Http2ConnectionActor` that handles HTTP/2-specific stream accounting correctly, particularly the StreamAcquired/StreamCompleted signalling without premature MarkBusy.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-005
**Parallel:** yes — can run alongside TASK-004-002 and TASK-004-004

**Acceptance Criteria:**
- [x] `Http2ConnectionActor` created in `src/TurboHttp/Pooling/Http2ConnectionActor.cs`
- [x] Inherits from `ConnectionActorBase`
- [x] `Connect()` — simple `ClientManager.CreateRunnerWithChannels` (like Http1, no SharedProvider)
- [x] `HandleConnected()` — sets `_runner`, sends `ConnectionReady` to parent
- [x] Stream lifecycle: `StreamAcquired`/`StreamCompleted` messages correctly forwarded to parent
- [x] No `MarkBusy()` in HostPool for HTTP/2 — stream accounting exclusively via stage signals
- [x] `HandleDisconnected()` / `HandleTerminated()` — calls `Reconnect()`
- [x] `PostStop()` — stops runner
- [x] Build green

### TASK-004-004: Implement Http3ConnectionActor

**Description:** As a developer, I want an `Http3ConnectionActor` that isolates the QUIC-specific SharedProvider and multi-runner logic so that QUIC streams are managed cleanly.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-004-001
**Successors:** TASK-004-005
**Parallel:** yes — can run alongside TASK-004-002 and TASK-004-003

**Acceptance Criteria:**
- [x] `Http3ConnectionActor` created in `src/TurboHttp/Pooling/Http3ConnectionActor.cs`
- [x] Inherits from `ConnectionActorBase`
- [x] Own fields: `_sharedProvider`, `_activeRunners`, `_pendingStreamRequesters`
- [x] `OpenNewStream` message handler lives here (not in base)
- [x] `Connect()` — creates `QuicClientProvider`, uses SharedProvider with `CreateRunnerWithChannels`
- [x] `HandleConnected()` — tracks runner in `_activeRunners`, sends `ConnectionReady`, flushes pending queue
- [x] `SpawnStreamRunner()` — BecomeStacked pattern for new QUIC streams
- [x] `HandleDisconnected()` / `HandleTerminated()` — only reconnect when all runners are gone
- [x] `Reconnect()` override — disposes SharedProvider, clears runner list
- [x] `PostStop()` — stops all runners, disposes SharedProvider
- [x] Build green

### TASK-004-005: Migrate HostPool + remove old ConnectionActor

**Description:** As a developer, I want `HostPool.SpawnConnection()` to create the correct actor type based on HTTP version, and remove the old monolithic `ConnectionActor`.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-004-002, TASK-004-003, TASK-004-004
**Successors:** TASK-004-006
**Parallel:** no — requires all three subclasses

**Acceptance Criteria:**
- [x] `HostPool.SpawnConnection()` contains version switch:
  - HTTP/1.x -> `Http1ConnectionActor`
  - HTTP/2 -> `Http2ConnectionActor`
  - HTTP/3 -> `Http3ConnectionActor`
- [x] `ConnectionReady` message type remains compatible (either in base or re-exported)
- [x] `IsQuic` and `IsMultiStream` properties in HostPool remain as routing helpers, but branching in message handlers reduced where possible
- [x] Old monolithic `ConnectionActor.cs` deleted (logic distributed across base + 3 subclasses)
- [x] Build green: `dotnet build --configuration Release src/TurboHttp.sln`
- [x] All existing tests green: `dotnet test src/TurboHttp.sln`

### TASK-004-006: Migrate StreamTests and IO tests

**Description:** As a developer, I want to ensure all existing tests that reference `ConnectionActor` are migrated to the new hierarchy.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-004-005
**Successors:** TASK-004-007
**Parallel:** no — requires the new architecture

**Acceptance Criteria:**
- [ ] All references to `ConnectionActor` in tests reviewed and migrated to `Http1ConnectionActor` / `Http2ConnectionActor` / `Http3ConnectionActor` as appropriate
- [ ] `ConnectionState` tests unchanged (ConnectionState is actor-agnostic)
- [ ] `HostPoolActorStreamLifecycleTests` work with new version-switch logic
- [ ] All tests green: `dotnet test src/TurboHttp.sln`
- [ ] No `ConnectionActor` imports remaining in test files (only base or specific subclasses)

### TASK-004-007: SmokeTests for all HTTP versions

**Description:** As a developer, I want SmokeTests for HTTP/1.0, HTTP/1.1, HTTP/2, HTTP/3, and TLS that verify basic connectivity for all protocol versions after every build.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-004-006
**Successors:** none
**Parallel:** no — validation gate, requires stable architecture

**Acceptance Criteria:**
- [ ] `SmokeTests.cs` contains 5 test classes:
  - `Http10SmokeTests` with `[Collection("Http1Integration")]`
  - `Http11SmokeTests` with `[Collection("Http1Integration")]`
  - `Http2SmokeTests` with `[Collection("Http2Integration")]`
  - `Http3SmokeTests` with `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]`
  - `TlsSmokeTests` with `[Collection("TlsIntegration")]`
- [ ] Each class: `IAsyncLifetime`, `ClientHelper.CreateClient()`, `CancellationTokenSource` with 30s timeout
- [ ] Each class: `[Fact]` GET /hello -> 200 "Hello World"
- [ ] HTTP/1.0: `ClientHelper.CreateClient(fixture.Port, new Version(1, 0))`
- [ ] HTTP/1.1: `ClientHelper.CreateClient(fixture.Port, new Version(1, 1))`
- [ ] HTTP/2: `ClientHelper.CreateClient(fixture.Port, new Version(2, 0))`
- [ ] HTTP/3: `ClientHelper.CreateClient(fixture.Port, new Version(3, 0), scheme: "https")`
- [ ] TLS: `ClientHelper.CreateClient(fixture.Port, new Version(1, 1), scheme: "https")`
- [ ] Existing `SmokeTests` class renamed to `Http11SmokeTests`
- [ ] **All 5 tests green — no skips**
- [ ] `dotnet test src/TurboHttp.IntegrationTests/ --filter "SmokeTests"` -> 5/5 passed

## Task Dependency Graph

```
TASK-004-001 (ConnectionActorBase)
    |---> TASK-004-002 (Http1ConnectionActor) --|
    |---> TASK-004-003 (Http2ConnectionActor) --|--> TASK-004-005 (HostPool Migration) --> TASK-004-006 (Test Migration) --> TASK-004-007 (SmokeTests)
    |---> TASK-004-004 (Http3ConnectionActor) --|
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-004-001 | ~75k | none | no | opus |
| TASK-004-002 | ~40k | 001 | yes (with 003, 004) | — |
| TASK-004-003 | ~50k | 001 | yes (with 002, 004) | — |
| TASK-004-004 | ~60k | 001 | yes (with 002, 003) | — |
| TASK-004-005 | ~50k | 002, 003, 004 | no | — |
| TASK-004-006 | ~40k | 005 | no | — |
| TASK-004-007 | ~60k | 006 | no | — |

**Total estimated tokens:** ~375k

## Functional Requirements

- FR-1: `ConnectionActorBase` contains all shared fields, channel management, reconnect with exponential backoff, and message forwarding
- FR-2: `Http1ConnectionActor` handles simple single-connection TCP lifecycle without multi-stream logic
- FR-3: `Http2ConnectionActor` handles HTTP/2 connections with stream accounting via `StreamAcquired`/`StreamCompleted` signals from `Http20ConnectionStage`
- FR-4: `Http3ConnectionActor` handles QUIC connections with SharedProvider, multi-runner tracking, and `OpenNewStream`
- FR-5: `HostPool.SpawnConnection()` creates the correct actor type based on `_key.Version`
- FR-6: `ConnectionHandle` interface remains fully compatible — no changes to `ConnectionStage` or layers above
- FR-7: All 5 SmokeTests (HTTP/1.0, 1.1, 2.0, 3.0, TLS) pass with GET /hello -> 200 "Hello World"
- FR-8: HTTP/3 tests carry `[Trait("Category", "Http3")]` for filtering

## Non-Goals

- No changes to `ConnectionHandle`, `ConnectionStage`, `ClientManager`, `ClientRunner`, or `ClientByteMover`
- No changes to the streams layer (Engine, Protocol Engines)
- No new fixtures or test infrastructure
- No performance optimisation — purely structural refactoring
- No HTTP/3 feature expansion (only isolate existing QUIC logic)

## Technical Considerations

- `ConnectionReady` will be defined as a nested record in `ConnectionActorBase` (or as a standalone record) so that `HostPool` does not need to reference a subclass
- `BecomeStacked` pattern in `Http3ConnectionActor.SpawnStreamRunner()` is preserved — it is QUIC-specific
- `HostPool.IsQuic` and `IsMultiStream` remain as routing helpers in `ServeQueuedRequesters()` and `HandleEnsureHost()`
- HTTP/2 h2c hang fix: `Http2ConnectionActor` must not trigger `MarkBusy()` before stream allocation — this is naturally solved by the clean separation in the subclass
- `#pragma warning disable CA1416` for `QuicClientProvider` stays only in `Http3ConnectionActor`
- `ConnectionFactory` in `HostPoolConfig` may need adjustment for test injection

## Success Metrics

- All existing 1800+ tests green after refactoring
- All 5 SmokeTests green (HTTP/1.0, 1.1, 2.0, 3.0, TLS)
- `ConnectionActor.cs` deleted — no monolithic class remaining
- Zero `_isMultiStream` branches in the entire codebase
- Total SmokeTest runtime < 30 seconds

## Open Questions

_None — scope and architecture have been clarified in discussion._
