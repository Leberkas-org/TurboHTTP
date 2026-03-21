# Plan: User Middlewares as BidiFlow

## Introduction

The built-in features (Cookie, Redirect, Retry, Cache, Decompression) are all implemented as `BidiFlow` stages and compose cleanly via `Atop`. User middlewares, however, are split into two separate unidirectional stages (`MiddlewareRequestStage` + `MiddlewareResponseStage`) that are applied as plain Flows before and after the BidiFlow chain in `Engine.BuildExtendedPipeline()`.

This creates an inconsistency: built-in features are BidiFlows, user middlewares are not. By wrapping `TurboMiddleware` in a single `MiddlewareBidiStage`, user middlewares can be composed via `Atop` just like built-in features — resulting in cleaner, more uniform pipeline composition.

## Goals

- Unify user middleware and built-in feature composition using `BidiFlow.Atop()`
- Reduce `Engine.BuildExtendedPipeline()` complexity by eliminating separate request/response middleware loops
- Preserve existing semantics: user middlewares see initial requests only (not redirect/retry) and final responses only
- Delete the now-redundant `MiddlewareRequestStage` and `MiddlewareResponseStage`
- Maintain full test coverage

## User Stories

### TASK-001: Create MiddlewareBidiStage
**Description:** As a developer, I want a `MiddlewareBidiStage` that wraps a `TurboMiddleware` instance as a bidirectional stage so that it can compose via `Atop`.

**Acceptance Criteria:**
- [x] New file `src/TurboHttp/Streams/Stages/MiddlewareBidiStage.cs`
- [x] Implements `GraphStage<BidiShape<HttpRequestMessage, HttpRequestMessage, HttpResponseMessage, HttpResponseMessage>>`
- [x] Constructor takes `TurboMiddleware middleware` and `int index`
- [x] Port names use middleware class name with index fallback for uniqueness: e.g. `"AuthMiddleware.In.Request"` for named types, `"Middleware0.In.Request"` as fallback for delegates sharing names
- [x] Request direction: `OnPush` calls `ProcessRequestAsync()`, supports ValueTask fast-path and async callback via `GetAsyncCallback<T>`
- [x] Response direction: `OnPush` calls `ProcessResponseAsync(response.RequestMessage!, response, ...)`, same async pattern
- [x] Each direction manages completion independently (`Complete(outlet)` / `Cancel(inlet)`, not `CompleteStage()`)
- [x] Async in-flight tracking per direction: `_requestAsyncInFlight`, `_responseAsyncInFlight`
- [x] Deferred completion: if upstream finishes while async is in-flight, complete the outlet after the callback fires
- [x] Typecheck passes (`dotnet build`)

### TASK-002: Update Engine Pipeline Composition
**Description:** As a developer, I want `Engine.BuildExtendedPipeline()` to compose user middleware BidiFlows via `Atop` so that the pipeline is uniform.

**Acceptance Criteria:**
- [x] Remove the `foreach` loop that creates `MiddlewareRequestStage` instances (lines ~138-141)
- [x] Remove the `foreach` loop that creates `MiddlewareResponseStage` instances (lines ~147-150)
- [x] Build middleware BidiFlows and stack them outermost via `Atop` (after Redirect, which is currently outermost)
- [x] FIFO order preserved: `Middlewares[0]` is outermost (sees initial request first, final response last)
- [x] Implementation: iterate middlewares in reverse, `Atop` each onto `features`
- [x] When all features are null and no middlewares exist, engine flow is used directly (no regression)
- [x] Update docstring to include middleware in the stacking order list
- [x] Typecheck passes

### TASK-003: Delete Old Middleware Stages
**Description:** As a developer, I want to remove the now-redundant `MiddlewareRequestStage` and `MiddlewareResponseStage` so that there is no dead code.

**Acceptance Criteria:**
- [x] Delete `src/TurboHttp/Streams/Stages/MiddlewareRequestStage.cs`
- [x] Delete `src/TurboHttp/Streams/Stages/MiddlewareResponseStage.cs`
- [x] No remaining references to either class in the codebase
- [x] Typecheck passes

### TASK-004: Create BidiStage Test File
**Description:** As a developer, I want comprehensive tests for `MiddlewareBidiStage` covering both directions, async, chaining, and completion.

**Acceptance Criteria:**
- [x] New file `src/TurboHttp.StreamTests/Streams/20_MiddlewareBidiStageTests.cs`
- [x] Test: sync request transformation (header injection)
- [x] Test: async request transformation (with `Task.Delay`)
- [x] Test: sync response transformation (header injection)
- [x] Test: async response transformation
- [x] Test: original request access in response direction (`response.RequestMessage`)
- [x] Test: multiple BidiStages in series via `Atop` (FIFO order, cumulative changes)
- [x] Test: multiple requests/responses flow through correctly with stream completion
- [x] All tests pass

### TASK-005: Remove Old Test Files
**Description:** As a developer, I want to remove the old middleware test files since they test deleted stages.

**Acceptance Criteria:**
- [x] Delete `src/TurboHttp.StreamTests/Streams/18_MiddlewareRequestStageTests.cs`
- [x] Delete `src/TurboHttp.StreamTests/Streams/19_MiddlewareResponseStageTests.cs`
- [ ] All remaining tests pass (`dotnet test`)

### TASK-006: Validate Port Naming
**Description:** As a developer, I want to verify that all port names remain globally unique after the changes.

**Acceptance Criteria:**
- [ ] Run `stage-port-validator` agent
- [ ] Zero port naming violations
- [ ] Full build + test green (`dotnet test src/TurboHttp.sln`)

## Functional Requirements

- FR-1: `MiddlewareBidiStage` must call `ProcessRequestAsync()` on every element pushed to `_inRequest` and forward the result to `_outRequest`
- FR-2: `MiddlewareBidiStage` must call `ProcessResponseAsync()` on every element pushed to `_inResponse` and forward the result to `_outResponse`
- FR-3: When `ProcessRequestAsync` or `ProcessResponseAsync` completes synchronously (`IsCompletedSuccessfully`), the result must be pushed immediately without scheduling an async callback
- FR-4: When the ValueTask is not completed, the stage must use `GetAsyncCallback<T>` and `ContinueWith(ExecuteSynchronously)` to push the result safely
- FR-5: Port names must derive from the middleware's runtime class name (e.g. `AuthMiddleware.In.Request`), with an indexed fallback (`Middleware0`) to guarantee uniqueness when multiple delegates share the same class name
- FR-6: User middleware BidiFlows must be outermost in the `Atop` chain — outside Redirect, Cookie, Retry, Cache, Decompression
- FR-7: FIFO ordering: `Middlewares[0]` processes the initial request first and the final response last

## Non-Goals

- No changes to the public `TurboMiddleware` API
- No changes to `TurboHttpClientBuilderExtensions` registration API
- No changes to `PipelineDescriptor` record shape
- No changes to built-in BidiStages (Cookie, Redirect, Retry, Cache, Decompression)
- No performance benchmarks in this plan

## Technical Considerations

- **Port name strategy:** Use `GetType().Name` to extract middleware class name. For `DelegateRequestMiddleware` / `DelegateResponseMiddleware`, the name is still unique enough. If two middlewares share the same class name, append the index: `AuthMiddleware.In.Request` for the first, `AuthMiddleware1.In.Request` for the second. Simpler alternative: always use `"{ClassName}{index}"` — guaranteed unique, slightly less readable.
- **Completion semantics:** Unlike the old Flow stages that used `CompleteStage()`, the BidiStage must complete each direction independently. Follow the CookieBidiStage pattern: `Complete(outlet)` on upstream finish, `Cancel(inlet)` on downstream finish.
- **Async safety:** Both directions can have async operations in-flight simultaneously. Track independently with `_requestAsyncInFlight` and `_responseAsyncInFlight`.
- **Atop ordering:** Build from innermost to outermost. Middlewares are outermost, so they are applied last in the build loop but wrap everything else.

### Key Files

| File | Action |
|------|--------|
| `src/TurboHttp/Streams/Stages/MiddlewareBidiStage.cs` | **Create** (~130 lines) |
| `src/TurboHttp/Streams/Engine.cs` | **Modify** (simplify BuildExtendedPipeline) |
| `src/TurboHttp/Streams/Stages/MiddlewareRequestStage.cs` | **Delete** |
| `src/TurboHttp/Streams/Stages/MiddlewareResponseStage.cs` | **Delete** |
| `src/TurboHttp.StreamTests/Streams/20_MiddlewareBidiStageTests.cs` | **Create** |
| `src/TurboHttp.StreamTests/Streams/18_MiddlewareRequestStageTests.cs` | **Delete** |
| `src/TurboHttp.StreamTests/Streams/19_MiddlewareResponseStageTests.cs` | **Delete** |

### Reference Files (read-only, reuse patterns)

| File | Reuse |
|------|-------|
| `src/TurboHttp/Streams/Stages/CookieBidiStage.cs` | BidiShape pattern, completion semantics |
| `src/TurboHttp/Streams/Stages/MiddlewareRequestStage.cs` | Async callback pattern (before deletion) |
| `src/TurboHttp/Middleware/TurboMiddleware.cs` | API contract |

## Success Metrics

- Zero compile errors, zero test failures
- `Engine.BuildExtendedPipeline()` has no separate request/response middleware loops
- Port naming validator reports zero violations
- All 7+ new BidiStage tests pass
- No references to `MiddlewareRequestStage` or `MiddlewareResponseStage` remain in the codebase

## Open Questions

- Should `MiddlewareBidiStage` port names always include the index for simplicity (`AuthMiddleware0.In.Request`), or only append the index when there's a name collision? Recommend: always include index for simplicity and guaranteed uniqueness.
