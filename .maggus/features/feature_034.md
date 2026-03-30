<!-- maggus-id: bc4fe51d-e7f8-49a5-9fae-4c2b2aa1e9e4 -->
# Feature 034: Options Structure Cleanup — Eliminate Property Duplication

## Introduction

The TurboHttp configuration types have grown organically, resulting in significant property duplication across multiple types. The same values appear in 2–3 different places, policy types are buried inside protocol-internal namespaces making them hard to discover, and two dead properties mislead readers about how the library actually works.

This feature cleans up the options structure in four focused changes: remove dead code, move user-facing policies to the root namespace, consolidate the descriptor-to-pipeline translation, and make transport options internal.

### Architecture Context

- **Components involved:** `TurboClientOptions`, `TurboClientDescriptor`, `PipelineDescriptor`, `TurboHttpClientFactory`, transport layer (`TcpOptions`, `TlsOptions`, `QuicOptions`), and all six policy types
- **Architecture alignment:** The Client Layer owns all user-facing config. Currently, policy types leak into the Protocol Layer's namespace, which violates the "each layer depends only on the layer below" invariant from ARCHITECTURE.md.
- **No new components introduced** — pure cleanup/restructuring

## Goals

- Eliminate the 6 property copy-paste between `TurboClientDescriptor` and `PipelineDescriptor`
- Move all user-facing policy types to the `TurboHttp` root namespace for discoverability
- Remove `HandlerTypes` (dead code never read by production) from `TurboClientDescriptor`
- Remove `BaseAddress` from `TurboClientOptions` (silently ignored in DI path — misleading)
- Make `TcpOptions`/`TlsOptions`/`QuicOptions` internal (implementation detail, not user API)

## Tasks

### TASK-034-001: Remove Dead Properties

**Description:** As a library maintainer, I want to remove `HandlerTypes` and `TurboClientOptions.BaseAddress` so that the codebase has no misleading dead code.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-034-002, TASK-034-003
**Parallel:** yes — can run alongside TASK-034-004

**Acceptance Criteria:**
- [ ] `HandlerTypes` property removed from `TurboClientDescriptor.cs`
- [ ] `d.HandlerTypes.Add(typeof(T))` line removed from `TurboHttpClientBuilderExtensions.cs`
- [ ] Doc comment on `TurboClientDescriptor` updated to remove `HandlerTypes` reference
- [ ] 3 tests in `TurboHttpClientBuilderHandlerTests.cs` that assert on `HandlerTypes` deleted (`AddHandler_AddsTypeToHandlerTypes`, `AddHandler_PreservesFifoOrderInTypes`, assertion in `UseRequest_AddsOneFactoryWithNoTypeEntry`)
- [ ] `BaseAddress` property removed from `TurboClientOptions.cs`
- [ ] `BaseAddress: options.BaseAddress` in `Engine.BuildRequestOptions()` changed to `BaseAddress: null`
- [ ] `dotnet build` passes with 0 errors
- [ ] `dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj` passes

**Files:**
- `src/TurboHttp/TurboClientDescriptor.cs`
- `src/TurboHttp/TurboHttpClientBuilderExtensions.cs`
- `src/TurboHttp/TurboClientOptions.cs`
- `src/TurboHttp/Streams/Engine.cs`
- `src/TurboHttp.Tests/Hosting/TurboHttpClientBuilderHandlerTests.cs`

---

### TASK-034-002: Move Policy Types to Root Namespace

**Description:** As a library user, I want policy types like `RedirectPolicy` and `CachePolicy` in the `TurboHttp` namespace so I can find them without knowing the internal RFC folder structure.

**Token Estimate:** ~120k tokens
**Predecessors:** TASK-034-001
**Successors:** TASK-034-003
**Parallel:** no — must follow 001 so the using-cleanup accounts for the final state of all files
**Model:** opus

**What changes:** Only the `namespace` declaration in 6 files. The `.cs` files stay in their current RFC subdirectories. All `using` statements that imported these types solely from the old namespace must be removed (enforced by `TreatWarningsAsErrors`).

**Namespace changes (6 files):**
- `src/TurboHttp/Protocol/RFC9110/RedirectPolicy.cs` → `namespace TurboHttp;`
- `src/TurboHttp/Protocol/RFC9110/RetryPolicy.cs` → `namespace TurboHttp;`
- `src/TurboHttp/Protocol/RFC9110/Expect100Policy.cs` → `namespace TurboHttp;`
- `src/TurboHttp/Protocol/RFC9110/CompressionPolicy.cs` → `namespace TurboHttp;`
- `src/TurboHttp/Protocol/RFC9111/CachePolicy.cs` → `namespace TurboHttp;`
- `src/TurboHttp/Protocol/RFC9112/ConnectionPolicy.cs` → `namespace TurboHttp;`

**Using cleanup strategy:**
- Run `dotnet build` after namespace changes — `TreatWarningsAsErrors` will flag every now-unused `using` directive
- Files that only used the RFC namespace for policy types: remove the `using`
- Files that also use other types from those namespaces (e.g. `CacheStore`, `RedirectHandler`): keep the `using`
- Files inside `TurboHttp.Protocol.RFC9110` namespace (like `RedirectHandler.cs`, `RetryEvaluator.cs`): resolve moved types through namespace ancestry — no new `using TurboHttp;` needed
- `TurboClientOptions.cs`: remove `using TurboHttp.Protocol.RFC9112;` (ConnectionPolicy was its only reason)
- `PipelineDescriptor.cs`: remove `using TurboHttp.Protocol.RFC9110;` (keep RFC9111 for `CacheStore`)
- `TurboClientDescriptor.cs`: remove both RFC9110 and RFC9111 usings (after 001, only RFC6265 for `CookieJar` and RFC9111 for `CacheStore` were left; CachePolicy moving means RFC9111 stays for `CacheStore`)

**Scope:** ~30 test files + ~15 production files need using updates.

**Acceptance Criteria:**
- [ ] All 6 policy files declare `namespace TurboHttp;`
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes with 0 errors and 0 warnings
- [ ] `dotnet test ./src/TurboHttp.sln` passes (all ~2100 tests)
- [ ] No file contains a now-redundant `using TurboHttp.Protocol.RFC9110/RFC9111/RFC9112` that was only there for a policy type

---

### TASK-034-003: Consolidate Descriptor → Pipeline Translation

**Description:** As a library maintainer, I want the `TurboClientDescriptor → PipelineDescriptor` conversion logic in one place so the factory is not duplicating descriptor properties.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-034-002
**Successors:** none
**Parallel:** no

**What changes:**
- Add `internal PipelineDescriptor ToPipelineDescriptor(IServiceProvider provider)` method to `TurboClientDescriptor`
- The method contains the cookie/cache/handler resolution logic currently in `TurboHttpClientFactory.CreateClient()`
- Simplify `TurboHttpClientFactory.CreateClient()` to 3 lines

**Acceptance Criteria:**
- [ ] `TurboClientDescriptor` has `ToPipelineDescriptor(IServiceProvider)` method with full resolution logic
- [ ] `TurboHttpClientFactory.CreateClient()` calls `descriptor.ToPipelineDescriptor(provider)` — no manual property copying
- [ ] `TurboHttpClientFactory` has `using TurboHttp.Streams;` removed/updated to match new minimal imports
- [ ] Behavior is identical to before (all integration + unit tests pass)
- [ ] `dotnet build` passes with 0 errors

**Files:**
- `src/TurboHttp/TurboClientDescriptor.cs`
- `src/TurboHttp/TurboHttpClientFactory.cs`

---

### TASK-034-004: Make Transport Options Internal

**Description:** As a library maintainer, I want `TcpOptions`, `TlsOptions`, and `QuicOptions` to be `internal` so that only the public `TurboClientOptions` is the user-facing configuration surface for transport.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-034-001 (touches completely different files)
**Model:** haiku

**Note:** `InternalsVisibleTo` is already configured in `TurboHttp.csproj` for all four test projects — test files that construct these types directly continue to compile unchanged.

**What changes:**
- `TcpOptions` → `internal record`
- `TlsOptions` → `internal record`
- `QuicOptions` → `internal record`
- `TcpClientProvider` → `internal class`
- `TlsClientProvider` → `internal class`
- `IClientProvider` → `internal interface` (only consumed internally)

**Acceptance Criteria:**
- [ ] All 5 types and the interface are `internal` in `IClientProvider.cs` and `QuicOptions.cs`
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` passes with 0 errors
- [ ] `dotnet test ./src/TurboHttp.sln` passes — all test files that use `TcpOptions`/`TlsOptions`/`QuicOptions` still compile via `InternalsVisibleTo`
- [ ] No public API surface exposes these types (verify with `get_public_api` Roslyn MCP tool)

**Files:**
- `src/TurboHttp/Transport/IClientProvider.cs`
- `src/TurboHttp/Transport/QuicOptions.cs`

## Task Dependency Graph

```
TASK-034-001 ──→ TASK-034-002 ──→ TASK-034-003
TASK-034-004 (independent)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-034-001 | ~20k | none | yes (with 004) | — |
| TASK-034-002 | ~120k | 001 | no | opus |
| TASK-034-003 | ~25k | 002 | no | — |
| TASK-034-004 | ~15k | none | yes (with 001) | haiku |

**Total estimated tokens:** ~180k

## Functional Requirements

- FR-1: After this feature, a user importing `TurboHttp` can access `RedirectPolicy`, `RetryPolicy`, `Expect100Policy`, `CompressionPolicy`, `CachePolicy`, and `ConnectionPolicy` without any additional `using` directive
- FR-2: `TurboClientOptions` must not have a `BaseAddress` property (it was silently ignored in the DI path)
- FR-3: `TurboClientDescriptor` must not have a `HandlerTypes` property (it was never read by production code)
- FR-4: `TurboHttpClientFactory.CreateClient()` must not manually copy properties from descriptor to pipeline — it delegates to `ToPipelineDescriptor()`
- FR-5: `TcpOptions`, `TlsOptions`, `QuicOptions`, `TcpClientProvider`, `TlsClientProvider`, and `IClientProvider` must be `internal`
- FR-6: Build produces 0 errors and 0 warnings (TreatWarningsAsErrors)
- FR-7: All existing tests (~2100) continue to pass after the change

## Non-Goals

- No changes to actual policy behavior or default values
- No renaming of policy types themselves (only namespace changes)
- No changes to `PipelineDescriptor`'s shape or position in `TurboHttp.Streams`
- No changes to `CacheStore`, `CookieJar`, or other non-policy types in the RFC namespaces
- No changes to test conventions or DisplayName format

## Technical Considerations

- **`TreatWarningsAsErrors` is the safety net for TASK-034-002** — every unused `using` becomes a build error, making it impossible to miss a cleanup
- **`InternalsVisibleTo` is already configured** in `TurboHttp.csproj` for all test projects — TASK-034-004 requires no test changes
- **Files in `TurboHttp.Protocol.RFC9110` namespace** (e.g. `RedirectHandler.cs`) auto-resolve moved types through namespace ancestry — they do NOT need `using TurboHttp;`
- **`BaseAddress` on `TurboHttpClient`** (the mutable property on the client instance) is the correct place for base address configuration and is unaffected
- **ARCHITECTURE.md** may need a minor update to the Client Layer table entry for `TurboClientOptions` after `BaseAddress` is removed

## Success Metrics

- Zero duplicate property definitions for the same conceptual value across option types
- All policy types accessible from `TurboHttp` namespace without RFC-specific usings
- `TurboHttpClientFactory.CreateClient()` fits in ~5 lines
- No public transport option types in the API surface
