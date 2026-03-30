<!-- maggus-id: 34cecb1e-f832-4688-af56-088aa65078e1 -->

# Feature 033: Clean Protocol Core — Single GroupByRequestKey

## Introduction

`ProtocolCoreGraphBuilder` inverts the natural execution order of the protocol engine. It partitions requests by HTTP version first and then runs a separate `GroupByRequestKey` inside each version lane — instantiating the stage four times. Because `RequestEndpoint` already encodes the HTTP version in its key, this is redundant: the partitioning and the grouping are doing overlapping work at different levels of the graph.

This feature inverts the topology: group by endpoint once at the top level, then route by version inside each substream. The result is a simpler, flatter graph that is easier to reason about and monitor.

`ProtocolCoreGraphBuilder` is deleted; its logic moves into `Engine.cs` as two private methods (`BuildProtocolCore`, `BuildVersionRouter`).

### Architecture Context

- **Components involved:**
  - `src/TurboHttp/Streams/Engine.cs` — rewritten to own the protocol core graph inline
  - `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` — deleted
  - `src/TurboHttp/TurboClientOptions.cs` — extended with `MaxEndpointSubstreams`
  - `src/TurboHttp.StreamTests/Streams/10_EngineVersionRoutingTests.cs` — existing coverage, kept unchanged
- **Architecture alignment:** The ARCHITECTURE.md Protocol Engine Core section currently documents the old topology. It is updated as the final task.
- **New pattern introduced:** `MaxEndpointSubstreams` on `TurboClientOptions` makes the shared substream ceiling configurable, aligning with the existing `TurboClientOptions` pattern for per-client tuning.

**Prerequisite:** The `feature/better-graph` branch (PipelineDescriptor changes, currently in progress) must be merged to `main` before this feature starts. Tasks in this plan assume that branch's state as the baseline.

---

## Goals

- Replace four per-version `GroupByRequestKey` calls with a single shared instance
- Delete `ProtocolCoreGraphBuilder.cs` — its sole caller (`Engine.cs`) absorbs the logic
- Expose `MaxEndpointSubstreams` on `TurboClientOptions` (default: 256) so callers can tune the ceiling
- Keep `RequestEndpoint = (Scheme, Host, Port, Version)` semantics unchanged
- All existing stream tests and integration tests pass without modification

---

## Tasks

### TASK-033-001: Add `MaxEndpointSubstreams` to `TurboClientOptions`

**Description:** As a library user, I want to configure the maximum number of concurrent endpoint substreams so that I can tune the ceiling for my workload without recompiling.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-033-002
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [x] `TurboClientOptions` gains a property `public int MaxEndpointSubstreams { get; init; } = 256;`
- [x] XML doc comment explains the property: max number of distinct `(scheme, host, port, version)` substreams active at one time
- [x] Validation: value must be ≥ 1; throw `ArgumentOutOfRangeException` if violated (validate in constructor or `IValidateOptions` if already wired)
- [x] No existing public API is removed or changed — extend-only
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` passes with zero errors

---

### TASK-033-002: Rewrite `Engine.cs` — inline protocol core with single `GroupByRequestKey`

**Description:** As a maintainer, I want the protocol engine graph to group by endpoint first and route by version inside each substream, so the topology matches the logical execution order and reduces redundant grouping stages.

**Token Estimate:** ~120k tokens
**Predecessors:** TASK-033-001
**Successors:** TASK-033-003
**Parallel:** no
**Model:** opus

**Acceptance Criteria:**
- [x] `Engine.BuildExtendedPipeline` (or equivalent entry point) no longer calls `ProtocolCoreGraphBuilder.Build(...)`
- [x] A new private static method `BuildProtocolCore(ConnectionPool, TurboClientOptions, ...)` is added to `Engine.cs`; it wires the new topology:
  ```
  Flow.Create<HttpRequestMessage>()
      .GroupByRequestKey(RequestEndpoint.FromRequest, maxSubstreams: clientOptions.MaxEndpointSubstreams)
      .ViaSubFlow(versionRouter)
      .MergeSubstreams()
      .WithAttributes(highThroughputBuffer)
  ```
- [x] A new private static method `BuildVersionRouter(http10, http11, http20, http30)` constructs a `GraphDsl` graph with `Partition<HttpRequestMessage>(4, ...)` + `Merge<HttpResponseMessage>(4)` routing by `msg.Version`
- [x] `BuildConnectionFlow<TEngine>` is moved verbatim from `ProtocolCoreGraphBuilder` into `Engine.cs` (no logic change)
- [x] The version switch in `BuildVersionRouter` covers `{ Major: 1, Minor: 0 }`, `{ Major: 1, Minor: 1 }`, `{ Major: 2, Minor: 0 }`, `{ Major: 3, Minor: 0 }` and throws `ArgumentOutOfRangeException` for unknown versions
- [x] `highThroughputBuffer` uses `Attributes.CreateInputBuffer(16, 64)` (matching the existing buffer settings)
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` passes with zero errors
- [x] `dotnet test ./src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` passes (all stream tests green)
- [x] `dotnet test ./src/TurboHttp.sln` passes (full suite green)

---

### TASK-033-003: Delete `ProtocolCoreGraphBuilder.cs`

**Description:** As a maintainer, I want to remove the now-unused `ProtocolCoreGraphBuilder` file so there is no dead code left in the codebase.

**Token Estimate:** ~10k tokens
**Predecessors:** TASK-033-002
**Successors:** TASK-033-004
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [x] `src/TurboHttp/Streams/ProtocolCoreGraphBuilder.cs` is deleted
- [x] No remaining `using` or reference to `ProtocolCoreGraphBuilder` anywhere in `src/` (verified via grep)
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` passes with zero errors after deletion
- [x] `dotnet test ./src/TurboHttp.sln` passes

---

### TASK-033-004: Update `ARCHITECTURE.md` — Protocol Engine Core section

**Description:** As a contributor reading the architecture docs, I want the documented topology to match the actual implementation so I understand how the graph is wired.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-033-003
**Successors:** none
**Parallel:** no
**Model:** haiku

**Acceptance Criteria:**
- [x] The **Protocol Engine Core** section in `ARCHITECTURE.md` is updated to show the new topology:
  ```
  RequestEnricherStage
      → GroupByRequestKey(RequestEndpoint, max=MaxEndpointSubstreams)
          → per-endpoint substream:
              Partition(version) → [H10|H11|H20|H30] ConnectionFlow
              → Merge(4)
      → MergeSubstreams
  ```
- [x] The old description of four per-version `GroupByRequestKey` instances is removed
- [x] `maxSubstreams` note is updated: single shared ceiling, configurable via `TurboClientOptions.MaxEndpointSubstreams`, default 256
- [x] The note "An async boundary separates the feature chain from the engine core" is preserved if still accurate
- [x] No other sections of `ARCHITECTURE.md` are modified

---

## Task Dependency Graph

```
TASK-033-001 → TASK-033-002 → TASK-033-003 → TASK-033-004
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-033-001 | ~15k | none | no | haiku |
| TASK-033-002 | ~120k | 001 | no | opus |
| TASK-033-003 | ~10k | 002 | no | haiku |
| TASK-033-004 | ~15k | 003 | no | haiku |

**Total estimated tokens:** ~160k

---

## Functional Requirements

- FR-1: `GroupByRequestKey` must be instantiated exactly once in the protocol engine core graph
- FR-2: The grouping key remains `RequestEndpoint = (Scheme, Host, Port, Version)` — no semantic change
- FR-3: `TurboClientOptions.MaxEndpointSubstreams` controls the `maxSubstreams` ceiling; default is 256
- FR-4: HTTP version routing inside each substream uses a `Partition<HttpRequestMessage>(4, ...)` switching on `HttpRequestMessage.Version`
- FR-5: Unknown HTTP versions (not 1.0 / 1.1 / 2.0 / 3.0) throw `ArgumentOutOfRangeException` with a descriptive message
- FR-6: `ProtocolCoreGraphBuilder` is fully removed — no class, no file, no reference remains
- FR-7: All existing stream tests and integration tests pass without modification
- FR-8: The `highThroughputBuffer` attribute (`InputBuffer(16, 64)`) is applied to the outer `MergeSubstreams` flow, matching current behaviour

---

## Non-Goals

- No change to `RequestEndpoint` semantics or key composition
- No change to per-version engine internals (`Http10Engine`, `Http11Engine`, etc.)
- No change to `ConnectionPool`, `HostConnections`, or transport layer
- No new stream tests — `10_EngineVersionRoutingTests.cs` is sufficient coverage for this refactor
- No performance benchmarks — structural equivalence, not a performance feature
- No change to HTTP/2 or HTTP/3 `maxConcurrentStreams` limits (those are connection-level, unrelated to `MaxEndpointSubstreams`)

---

## Technical Considerations

- **Branch prerequisite:** This feature branches from `main` after the `feature/better-graph` PR (PipelineDescriptor changes) is merged. Do not start on an earlier baseline.
- **`ProtocolCoreGraphBuilder` has no test file** — grep confirmed it is only referenced from `Engine.cs`. Deletion in TASK-033-003 requires no test migration.
- **`ViaSubFlow`** is the correct Akka.Streams API to wire a `Flow` into a `SubFlow` context (returned by `GroupByRequestKey`). Verify the extension method signature in `HostKeyGroupByExtensions.cs` before writing.
- **`ARCHITECTURE.md`** currently documents the old topology explicitly in the Protocol Engine Core section. The update in TASK-033-004 must replace that block precisely.
- **Extend-only rule:** `MaxEndpointSubstreams` must be added as a new `init`-only property with a default value. No existing `TurboClientOptions` properties may be changed or removed.
- **`TreatWarningsAsErrors` is enabled globally** — ensure no new warnings are introduced (e.g., unused variables, missing XML doc on public members).

---

## Success Metrics

- `ProtocolCoreGraphBuilder.cs` no longer exists in the repository
- `GroupByRequestKey` appears exactly once in `Engine.cs`
- Full test suite (`dotnet test ./src/TurboHttp.sln`) green
- `ARCHITECTURE.md` Protocol Engine Core diagram matches the actual code

---

## Open Questions

— none
