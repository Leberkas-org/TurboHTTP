# Plan: TurboHttp Namespace Reorganisation

## Introduction

The TurboHttp project has grown to ~100 production files across 18 namespaces.
Some namespace names are misleading (`Middleware`, `Lifecycle`), others are too thin
(`IO.Stages` with 1 file, `Hosting` with 1 file), and `Streams.Stages` is completely
flat with 23 files. The goal is a clean, consistent namespace hierarchy that reflects
the actual purpose of each component — without changing the public `TurboHttp.Client.*` API.

**Status (2026-03-21):** No tasks executed yet. All old namespaces remain unchanged.
File inventory updated to reflect BidiStage refactoring (MiddlewareBidiStage replaced
MiddlewareRequestStage/ResponseStage; cache/cookie/decompression/redirect/retry stages
consolidated into `*BidiStage` pattern).

**Decisions:**
- `TurboHttp.Client.*` remains untouched (breaking changes excluded)
- `TurboHttp.IO` → `TurboHttp.Transport`, `TurboHttp.Lifecycle` → `TurboHttp.Pooling`
- `TurboHttp.Middleware` + `TurboHttp.Hosting` → root namespace `TurboHttp` (BCL-style)
- `TurboHttp.Streams.Stages` split by function: `Encoding`, `Decoding`, `Routing`, `Features`
- `TurboHttp.Internal.Stages` dissolved into `TurboHttp.Streams.Stages.Routing`

---

## Target Structure (Before → After)

```
BEFORE                              AFTER
──────────────────────────────────────────────────────────────────
TurboHttp.Client.*                  TurboHttp.Client.*          (unchanged)
TurboHttp.Hosting                   TurboHttp                   (root namespace)
TurboHttp.Middleware                TurboHttp                   (root namespace)
TurboHttp.IO                        TurboHttp.Transport
TurboHttp.IO.Stages                 TurboHttp.Transport         (absorbed)
TurboHttp.Lifecycle                 TurboHttp.Pooling
TurboHttp.Internal                  TurboHttp.Internal          (unchanged)
TurboHttp.Internal.Stages           TurboHttp.Streams.Stages.Routing (absorbed)
TurboHttp.Protocol.*                TurboHttp.Protocol.*        (unchanged)
TurboHttp.Streams                   TurboHttp.Streams           (unchanged)
TurboHttp.Streams.Stages            split (see below)
  ├── Encoder stages                TurboHttp.Streams.Stages.Encoding
  ├── Decoder stages                TurboHttp.Streams.Stages.Decoding
  ├── Routing stages                TurboHttp.Streams.Stages.Routing
  └── Feature stages                TurboHttp.Streams.Stages.Features
```

---

## File Assignment: Streams.Stages

| File | New Sub-Namespace |
|------|-------------------|
| Http10EncoderStage.cs | Encoding |
| Http11EncoderStage.cs | Encoding |
| Http20EncoderStage.cs | Encoding |
| PrependPrefaceStage.cs | Encoding |
| Request2FrameStage.cs | Encoding |
| Http10DecoderStage.cs | Decoding |
| Http11DecoderStage.cs | Decoding |
| Http20DecoderStage.cs | Decoding |
| Http20StreamStage.cs | Decoding |
| ExtractOptionsStage.cs | Routing |
| Http1XCorrelationStage.cs | Routing |
| Http20CorrelationStage.cs | Routing |
| StreamIdAllocatorStage.cs | Routing |
| RequestEnricherStage.cs | Routing |
| GroupByHostKeyStage.cs | Routing (from Internal.Stages) |
| HostKeyGroupByExtensions.cs | Routing (from Internal.Stages) |
| HostKeyMergeBack.cs | Routing (from Internal.Stages) |
| MergeSubstreamsStage.cs | Routing (from Internal.Stages) |
| CacheBidiStage.cs | Features |
| ConnectionReuseStage.cs | Features |
| CookieBidiStage.cs | Features |
| DecompressionBidiStage.cs | Features |
| Http20ConnectionStage.cs | Features |
| MiddlewareBidiStage.cs | Features |
| RedirectBidiStage.cs | Features |
| RetryBidiStage.cs | Features |
| TurboAttributes.cs | Streams.Stages (root, stays) |

---

## Goals

- Eliminate all misleading namespace names (`Middleware`, `Lifecycle`, `IO.Stages`)
- Split `Streams.Stages` into 4 cohesive functional namespaces (Encoding/Decoding/Routing/Features)
- Keep `TurboHttp.Client.*` fully unchanged — no breaking changes for public API consumers
- Update all test projects (`TurboHttp.Tests`, `TurboHttp.StreamTests`, `TurboHttp.IntegrationTests`)
- Build after every task: 0 errors, all tests green

---

## User Stories

### TASK-001: IO → Transport (rename folder + absorb IO.Stages)

**Description:** As a developer, I want the `TurboHttp.IO` namespace renamed to
`TurboHttp.Transport` so that the name clearly communicates its purpose (TCP transport,
byte-moving). `IO.Stages/ConnectionStage.cs` is absorbed into `Transport` (no separate
sub-namespace for a single file).

**Affected files (production):**
```
IO/ClientByteMover.cs          → namespace TurboHttp.Transport
IO/ClientManager.cs            → namespace TurboHttp.Transport
IO/ClientRunner.cs             → namespace TurboHttp.Transport
IO/ClientState.cs              → namespace TurboHttp.Transport
IO/IClientProvider.cs          → namespace TurboHttp.Transport
IO/TcpOptionsFactory.cs        → namespace TurboHttp.Transport
IO/Stages/ConnectionStage.cs   → namespace TurboHttp.Transport (move file into Transport/)
```

**Reference counts:** ~16 files contain `using TurboHttp.IO;`, additional files reference `TurboHttp.IO.Stages;`

**Acceptance Criteria:**
- [x] Folder `TurboHttp/IO/Stages/` emptied and deleted
- [x] All 7 files declare `namespace TurboHttp.Transport`
- [x] All `using TurboHttp.IO;` and `using TurboHttp.IO.Stages;` replaced across the solution
- [x] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [x] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-002: Lifecycle → Pooling (rename folder)

**Description:** As a developer, I want the `TurboHttp.Lifecycle` namespace renamed
to `TurboHttp.Pooling` to make it clear this is actor-based connection pool management.

**Affected files (production):**
```
Lifecycle/ConnectionActor.cs   → namespace TurboHttp.Pooling
Lifecycle/ConnectionHandle.cs  → namespace TurboHttp.Pooling
Lifecycle/ConnectionState.cs   → namespace TurboHttp.Pooling
Lifecycle/HostPool.cs          → namespace TurboHttp.Pooling
Lifecycle/PoolRouter.cs        → namespace TurboHttp.Pooling
```

**Reference counts:** ~11 files contain `using TurboHttp.Lifecycle;`

**Acceptance Criteria:**
- [ ] Folder `TurboHttp/Lifecycle/` renamed to `TurboHttp/Pooling/`
- [ ] All 5 files declare `namespace TurboHttp.Pooling`
- [ ] All `using TurboHttp.Lifecycle;` replaced across the solution
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-003: Middleware + Hosting → TurboHttp (root namespace)

**Description:** As a library consumer, I want to use builder and DI extensions
directly under `TurboHttp` (like `HttpClient` in `System.Net.Http`), without
importing deep sub-namespaces.

**Affected files (production):**
```
Middleware/ITurboHttpClientBuilder.cs          → namespace TurboHttp
Middleware/TurboClientDescriptor.cs            → namespace TurboHttp
Middleware/TurboHttpClientBuilder.cs           → namespace TurboHttp
Middleware/TurboHttpClientBuilderExtensions.cs → namespace TurboHttp
Middleware/TurboMiddleware.cs                  → namespace TurboHttp
Hosting/TurboClientServiceCollectionExtensions.cs → namespace TurboHttp
```

**Note on folder structure:** Files remain physically in their subfolders
(`Middleware/`, `Hosting/`) for organization — only the `namespace` declaration
changes to `TurboHttp`.

**Acceptance Criteria:**
- [ ] All 6 files declare `namespace TurboHttp`
- [ ] All `using TurboHttp.Middleware;` and `using TurboHttp.Hosting;` replaced across the solution (including test projects)
- [ ] `TurboHttp.Client.*` namespaces remain fully unchanged
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-004: Streams.Stages.Encoding — extract encoder stages

**Description:** As a developer, I want all encoding stages (Request→Bytes) grouped
in their own sub-namespace `TurboHttp.Streams.Stages.Encoding`.

**Affected files:**
```
Streams/Stages/Http10EncoderStage.cs   → Streams/Stages/Encoding/
Streams/Stages/Http11EncoderStage.cs   → Streams/Stages/Encoding/
Streams/Stages/Http20EncoderStage.cs   → Streams/Stages/Encoding/
Streams/Stages/PrependPrefaceStage.cs  → Streams/Stages/Encoding/
Streams/Stages/Request2FrameStage.cs   → Streams/Stages/Encoding/
```
New namespace: `TurboHttp.Streams.Stages.Encoding`

**Acceptance Criteria:**
- [ ] Folder `TurboHttp/Streams/Stages/Encoding/` created with the 5 files
- [ ] All 5 files declare `namespace TurboHttp.Streams.Stages.Encoding`
- [ ] All engines (`Http10Engine`, `Http11Engine`, `Http20Engine`) updated
- [ ] All referencing test files updated
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-005: Streams.Stages.Decoding — extract decoder stages

**Description:** As a developer, I want all decoding stages (Bytes→Response) grouped
in `TurboHttp.Streams.Stages.Decoding`.

**Affected files:**
```
Streams/Stages/Http10DecoderStage.cs   → Streams/Stages/Decoding/
Streams/Stages/Http11DecoderStage.cs   → Streams/Stages/Decoding/
Streams/Stages/Http20DecoderStage.cs   → Streams/Stages/Decoding/
Streams/Stages/Http20StreamStage.cs    → Streams/Stages/Decoding/
```
New namespace: `TurboHttp.Streams.Stages.Decoding`

**Acceptance Criteria:**
- [ ] Folder `TurboHttp/Streams/Stages/Decoding/` created with the 4 files
- [ ] All 4 files declare `namespace TurboHttp.Streams.Stages.Decoding`
- [ ] All engines and referencing streams files updated
- [ ] All referencing test files updated
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-006: Streams.Stages.Routing — routing stages + absorb Internal.Stages

**Description:** As a developer, I want all flow-control and correlation stages
(including the host-routing stages previously hidden in `Internal.Stages`) unified
in `TurboHttp.Streams.Stages.Routing`.

**Affected files:**
```
From Streams/Stages/:
  ExtractOptionsStage.cs          → Streams/Stages/Routing/
  Http1XCorrelationStage.cs       → Streams/Stages/Routing/
  Http20CorrelationStage.cs       → Streams/Stages/Routing/
  StreamIdAllocatorStage.cs       → Streams/Stages/Routing/
  RequestEnricherStage.cs         → Streams/Stages/Routing/

From Internal/Stages/ (namespace migration!):
  GroupByHostKeyStage.cs          → Streams/Stages/Routing/
  HostKeyGroupByExtensions.cs     → Streams/Stages/Routing/
  HostKeyMergeBack.cs             → Streams/Stages/Routing/
  MergeSubstreamsStage.cs         → Streams/Stages/Routing/
```
New namespace: `TurboHttp.Streams.Stages.Routing`

**Reference counts:** ~3 files contain `using TurboHttp.Internal.Stages;`

**Acceptance Criteria:**
- [ ] Folder `TurboHttp/Streams/Stages/Routing/` created with all 9 files
- [ ] All 9 files declare `namespace TurboHttp.Streams.Stages.Routing`
- [ ] `TurboHttp/Internal/Stages/` folder emptied and deleted
- [ ] All `using TurboHttp.Internal.Stages;` replaced across the solution
- [ ] All engines, `Engine.cs`, and referencing files updated
- [ ] All referencing test files updated
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-007: Streams.Stages.Features — extract feature stages

**Description:** As a developer, I want all higher-level HTTP semantics stages
(cache, cookies, decompression, redirect, retry, middleware pipeline, HTTP/2 connection)
grouped in `TurboHttp.Streams.Stages.Features`.

**Affected files:**
```
Streams/Stages/CacheBidiStage.cs            → Streams/Stages/Features/
Streams/Stages/ConnectionReuseStage.cs      → Streams/Stages/Features/
Streams/Stages/CookieBidiStage.cs           → Streams/Stages/Features/
Streams/Stages/DecompressionBidiStage.cs    → Streams/Stages/Features/
Streams/Stages/Http20ConnectionStage.cs     → Streams/Stages/Features/
Streams/Stages/MiddlewareBidiStage.cs       → Streams/Stages/Features/
Streams/Stages/RedirectBidiStage.cs         → Streams/Stages/Features/
Streams/Stages/RetryBidiStage.cs            → Streams/Stages/Features/
```
New namespace: `TurboHttp.Streams.Stages.Features`

**Acceptance Criteria:**
- [ ] Folder `TurboHttp/Streams/Stages/Features/` created with all 8 files
- [ ] All 8 files declare `namespace TurboHttp.Streams.Stages.Features`
- [ ] All engines and referencing streams files updated
- [ ] All referencing test files updated
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass

---

### TASK-008: Cleanup & Final Validation

**Description:** As a developer, I want to verify that after all renames no old
namespace references remain, empty folders are deleted, and the full build and
test suite runs clean.

**Acceptance Criteria:**
- [ ] No `using TurboHttp.IO;` or `using TurboHttp.IO.Stages;` anywhere in the solution
- [ ] No `using TurboHttp.Lifecycle;` anywhere in the solution
- [ ] No `using TurboHttp.Middleware;` or `using TurboHttp.Hosting;` anywhere in the solution
- [ ] No `using TurboHttp.Internal.Stages;` anywhere in the solution
- [ ] No unqualified `using TurboHttp.Streams.Stages;` for files now in sub-namespaces
- [ ] Empty folders `IO/Stages/` and `Internal/Stages/` deleted
- [ ] `Streams/Stages/` root contains only `TurboAttributes.cs`
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors, 0 namespace-related warnings
- [ ] `dotnet test src/TurboHttp.sln` → all tests pass
- [ ] Grep across solution for old namespace strings → no matches

---

## Functional Requirements

- FR-1: `TurboHttp.Client.*` (public API) must not be touched — no using changes, no namespace changes
- FR-2: Each task (TASK-001 through TASK-008) must be independently buildable and testable
- FR-3: Namespace declarations must exactly match the folder path (convention)
- FR-4: No compatibility shims, no `[Obsolete]` forwarding — clean cut
- FR-5: All three test projects (`TurboHttp.Tests`, `TurboHttp.StreamTests`, `TurboHttp.IntegrationTests`) must be green after every task
- FR-6: `TurboAttributes.cs` stays in `TurboHttp.Streams.Stages` (root), not in a sub-namespace

---

## Non-Goals

- No renaming of classes or interfaces (namespaces only)
- No changes to `TurboHttp.Protocol.*` (RFC folder structure stays unchanged)
- No changes to `TurboHttp.Streams` (engine level, not stages)
- No merging of protocol stages with the Protocol layer
- No new features or bug fixes within this task

---

## Technical Considerations

- **Order matters**: TASK-001 and TASK-002 first (isolated, low risk), then TASK-003, then TASK-004 through TASK-007 (can be worked on in parallel but build+test each one), finally TASK-008
- **Test projects**: `TurboHttp.StreamTests` has the most stage references — largest using-update effort
- **Engine files**: `Http10Engine.cs`, `Http11Engine.cs`, `Http20Engine.cs`, `Engine.cs` import many stages and must be updated during TASK-004 through TASK-007
- **`HostKeyGroupByExtensions.cs`**: extension methods file — verify if it is `internal`; if so, the namespace change is not a breaking change for consumers
- **No global using directives**: the project does not use `GlobalUsings.cs` for these namespaces — update individual files manually
- **BidiStage pattern**: Several stages were consolidated during the middleware refactoring (cache, cookie, decompression, redirect, retry all use BidiStage now) — reduced TASK-007 from 11 files to 8

---

## Success Metrics

- 18 namespaces → 14 namespaces (4 eliminated: `IO`, `IO.Stages`, `Lifecycle`, `Internal.Stages`)
- `Streams.Stages` reduced from 23 flat files to at most 1 (`TurboAttributes.cs`)
- `Middleware` and `Hosting` no longer visible as namespaces to library consumers
- Grep for `TurboHttp.IO`, `TurboHttp.Lifecycle`, `TurboHttp.Middleware`, `TurboHttp.Hosting`, `TurboHttp.Internal.Stages` → 0 matches after TASK-008

---

## Open Questions

- Should `TurboHttp.Internal` be dissolved long-term? `Messages.cs` and `RequestEndpoint.cs` could move to root or `Pooling` — not in scope here but a logical next step.
- Should the `Middleware/` folder be renamed to `Builder/` after TASK-003 to clarify the physical structure? (Low priority, optional cosmetic improvement.)
