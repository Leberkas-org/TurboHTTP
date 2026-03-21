# Plan: Remove Obsolete API — Migrate Tests, Delete Dead Code

## Introduction

Three `TurboClientOptions` properties (`RedirectPolicy`, `RetryPolicy`, `CachePolicy`) and the
`AddTurboHttpClientFactory` extension method are marked `[Obsolete]`. Two internal `Engine.CreateFlow`
overloads that accept `TurboClientOptions?` and silently ignore (or unsafely read) these properties
also exist as backward-compat bridges. This plan migrates all call sites to the replacement APIs,
then deletes every obsolete declaration and backward-compat bridge — leaving zero `[Obsolete]` items
and zero `#pragma warning disable CS0618` suppressions in the production codebase.

---

## Goals

- Migrate all test and benchmark call sites away from the `TurboClientOptions?` overloads.
- Delete the `AddTurboHttpClientFactory` backward-compat test.
- Remove all `#pragma warning disable CS0618` blocks from `Engine.cs`.
- Delete the two obsolete `CreateFlow(…, TurboClientOptions?)` overloads from `Engine.cs`.
- Delete the three `[Obsolete]` properties from `TurboClientOptions`.
- Delete the `[Obsolete]` `AddTurboHttpClientFactory` method from `TurboClientServiceCollectionExtensions`.
- `dotnet build` produces **zero CS0618 warnings** after all tasks are complete.

---

## User Stories

### TASK-001: Migrate `04_AsyncBoundaryTests.cs` to `PipelineDescriptor`
**Description:** As a developer, I want the async boundary tests to use `PipelineDescriptor` so that
they no longer depend on the `TurboClientOptions?` overload slated for deletion.

**Acceptance Criteria:**
- [x] ABND-001 through ABND-004: 4-argument `engine.CreateFlow(http10, http11, h20, h30)` calls
      each gain an explicit 5th argument `PipelineDescriptor.Empty`.
- [x] ABND-005 (`options = new TurboClientOptions()`): replaced with `PipelineDescriptor.Empty`.
      The local `options` variable and `TurboClientOptions` import (if unused after the change) are removed.
- [x] ABND-006 (`options: null`): replaced with `PipelineDescriptor.Empty`.
- [x] No `#pragma warning` suppressions are added.
- [x] All 6 ABND tests remain structurally identical (same assertions, same request/response bytes).
- [x] `dotnet build` passes with no new errors.

**Migration pattern:**
```csharp
// BEFORE — resolves to TurboClientOptions? = null default
engine.CreateFlow(() => Http10Flow(…), () => Http11Flow(…), NoOpH2Flow, NoOpH2Flow)

// AFTER — resolves to PipelineDescriptor overload
engine.CreateFlow(() => Http10Flow(…), () => Http11Flow(…), NoOpH2Flow, NoOpH2Flow,
    PipelineDescriptor.Empty)
```

---

### TASK-002: Migrate `EnginePipelineBenchmarks.cs` to `PipelineDescriptor`
**Description:** As a developer, I want the engine-pipeline benchmarks to use `PipelineDescriptor`
so that their two `CreateFlow` call sites no longer depend on the obsolete overload.

**Acceptance Criteria:**
- [x] `SetupHttp11Pipeline` (line ≈307): `options: null` replaced with
      `descriptor: PipelineDescriptor.Empty`.
- [x] `SetupHttp20Pipeline` (line ≈327): `options: null` replaced with
      `descriptor: PipelineDescriptor.Empty`.
- [x] No other benchmark logic is changed.
- [x] `dotnet build` passes with no new errors.

---

### TASK-003: Delete backward-compat test in `NamedClientIsolationTests.cs`
**Description:** As a developer, I want to remove the `AddTurboHttpClientFactory` backward-compat
test so that no test file suppresses CS0618 for a method that no longer exists.

**Acceptance Criteria:**
- [x] The entire test `AddTurboHttpClientFactory_IsObsoleteButRegistersFactory` (lines ≈103–113)
      is deleted, including its comment block and the two `#pragma warning` lines.
- [x] All other tests in `NamedClientIsolationTests.cs` are untouched.
- [x] No `#pragma warning disable CS0618` remains in that file.
- [x] `dotnet build` passes with no new errors.

---

### TASK-004: Clean up `Engine.cs` — remove backward-compat overloads and pragma blocks
**Description:** As a developer, I want to remove dead code from `Engine.cs` so that the public
API no longer exposes a `TurboClientOptions`-based entry point and no internal overload accepts
`TurboClientOptions?` as a pipeline descriptor.

**Acceptance Criteria:**
- [ ] The public 3-argument overload `CreateFlow(IActorRef, TurboClientOptions?, Func<TurboRequestOptions>?)`
      (currently lines 21–39) is deleted in full, including its `#pragma warning` block.
- [ ] The internal 5-argument overload `CreateFlow(http10, http11, http20, http30, TurboClientOptions? options = null)`
      (currently lines 53–74) is deleted in full.
- [ ] In `BuildExtendedPipeline`, the `CacheLookupStage` construction (line ≈176):
      ```csharp
      // BEFORE
      #pragma warning disable CS0618
      var cacheLookup = builder.Add(new CacheLookupStage(descriptor.CacheStore, options.CachePolicy));
      #pragma warning restore CS0618
      // AFTER
      var cacheLookup = builder.Add(new CacheLookupStage(descriptor.CacheStore));
      ```
      The two `#pragma warning` lines are removed along with the `options.CachePolicy` argument.
- [ ] Zero `#pragma warning disable CS0618` suppressions remain in `Engine.cs`.
- [ ] The remaining overloads and the rest of `BuildExtendedPipeline` are not modified.
- [ ] `dotnet build` passes with no new errors.

**Important:** The internal `CreateFlow(poolRouter, options, requestOptionsFactory, PipelineDescriptor)`
overload (currently lines 41–51) and the internal `CreateFlow(http10, http11, http20, http30, PipelineDescriptor)`
overload (currently lines 76–95) are **kept unchanged**. Only the backward-compat bridges are deleted.

---

### TASK-005: Delete three obsolete properties from `TurboClientOptions.cs`
**Description:** As a developer, I want to remove the three `[Obsolete]` policy properties from
`TurboClientOptions` so that the class no longer contains deprecated API surface.

**Acceptance Criteria:**
- [ ] `RedirectPolicy? RedirectPolicy` (with its `[Obsolete]` attribute and XML doc comment) is deleted.
- [ ] `RetryPolicy? RetryPolicy` (with its `[Obsolete]` attribute and XML doc comment) is deleted.
- [ ] `CachePolicy? CachePolicy` (with its `[Obsolete]` attribute and XML doc comment) is deleted.
- [ ] All other `TurboClientOptions` members are untouched.
- [ ] `dotnet build` passes with no new errors.

---

### TASK-006: Delete `AddTurboHttpClientFactory` from `TurboClientServiceCollectionExtensions.cs`
**Description:** As a developer, I want to remove the obsolete `AddTurboHttpClientFactory` extension
method so that the hosting API no longer exposes a deprecated registration path.

**Acceptance Criteria:**
- [ ] The entire `AddTurboHttpClientFactory` method (its `[Obsolete]` attribute, XML doc, and body)
      is deleted.
- [ ] All other extension methods in `TurboClientServiceCollectionExtensions.cs` are untouched.
- [ ] `dotnet build` passes with no new errors.

---

### TASK-007: Validation gate — zero obsolete warnings, all tests green
**Description:** As a developer, I want to confirm that after all migrations and deletions the
build and test suite are fully clean.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` exits 0 with **zero CS0618 warnings**.
- [ ] `dotnet test src/TurboHttp.sln` exits 0 with all tests passing.
- [ ] No `#pragma warning disable CS0618` lines remain anywhere in `src/`.
- [ ] No `[Obsolete]` attributes remain on any production member in `TurboHttp/` (non-test) code.

---

## Functional Requirements

- FR-1: Every `engine.CreateFlow(http10, http11, http20, http30)` call site must resolve to the
  `PipelineDescriptor` overload, not the `TurboClientOptions?` overload.
- FR-2: Every `engine.CreateFlow(http10, http11, http20, http30, options)` or
  `engine.CreateFlow(…, options: null)` call site must be replaced with an explicit
  `PipelineDescriptor.Empty` (or a specific descriptor if the test sets policies).
- FR-3: No production file under `src/TurboHttp/` may contain a `#pragma warning disable CS0618`
  suppression after TASK-004.
- FR-4: No test file may contain a `#pragma warning disable CS0618` suppression after TASK-003.
- FR-5: `TurboClientOptions` must not expose `RedirectPolicy`, `RetryPolicy`, or `CachePolicy`
  after TASK-005.
- FR-6: `TurboClientServiceCollectionExtensions` must not expose `AddTurboHttpClientFactory`
  after TASK-006.

---

## Non-Goals

- Do NOT migrate the files already in git status (`01_StageOrderingTests.cs`,
  `02_TaskFixVerificationTests.cs`, `02_RedirectStageTests.cs`, `03_RetryStageTests.cs`,
  `RedirectStage.cs`, `RetryStage.cs`). These have unrelated in-progress changes.
- Do NOT migrate `14_FeedbackBufferOptimizationTests.cs` — it is already fully migrated.
- Do NOT remove or rename `RedirectPolicy`, `RetryPolicy`, or `CachePolicy` record types — only
  the `TurboClientOptions` properties that reference them.
- Do NOT modify `CacheLookupStage` internals — only the call site in `Engine.cs`.
- Do NOT push or commit — all commits are done manually by the developer.

---

## Technical Considerations

- **Overload resolution**: `PipelineDescriptor.Empty` is the `static readonly` singleton.
  Use it everywhere the old `null` or empty `TurboClientOptions()` was passed.
- **CacheLookupStage second arg**: `CachePolicy?` is optional and defaults to `CachePolicy.Default`
  inside `CacheLookupStage`. The `CachePolicy` stored in `HttpCacheStore` is the authoritative
  value. Dropping the second arg is safe.
- **Build order**: Complete TASK-001 and TASK-002 before TASK-004 to avoid compile errors when
  the `TurboClientOptions?` overloads are deleted.
- **Test ordering**: Complete TASK-003 before TASK-006 so the test file doesn't call a deleted method.
- **Recommended task order**: 001 → 002 → 003 → 004 → 005 → 006 → 007.

---

## Success Metrics

- `dotnet build` output shows `0 Warning(s)` for CS0618.
- `grep -r "CS0618" src/` returns no results.
- `grep -r "\[Obsolete\]" src/TurboHttp/` returns no results.
- All tests pass.
