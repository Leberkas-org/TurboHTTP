# Consolidate StreamTests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge TurboHTTP.StreamTests (53 source files) into TurboHTTP.Tests under `Stages/` subfolders, updating namespaces and cleaning up project references.

**Architecture:** Move files component-by-component into `{Component}/Stages/` subfolders within TurboHTTP.Tests. Update all `namespace` declarations from `TurboHTTP.StreamTests.X` to `TurboHTTP.Tests.X.Stages`. Remove StreamTests project from solution and clean up InternalsVisibleTo entries.

**Tech Stack:** .NET 10, xUnit v3, Akka.Streams, PowerShell for file operations

**Spec:** `docs/superpowers/specs/2026-05-10-consolidate-streamtests-design.md`

---

### Task 1: Create Stages/ directory structure in TurboHTTP.Tests

**Files:**
- Create: `src/TurboHTTP.Tests/Caching/Stages/`
- Create: `src/TurboHTTP.Tests/Cookies/Stages/`
- Create: `src/TurboHTTP.Tests/Http10/Stages/`
- Create: `src/TurboHTTP.Tests/Http11/Stages/`
- Create: `src/TurboHTTP.Tests/Http2/Stages/`
- Create: `src/TurboHTTP.Tests/Http3/Stages/`
- Create: `src/TurboHTTP.Tests/Semantics/Stages/`
- Create: `src/TurboHTTP.Tests/Streams/Stages/`
- Create: `src/TurboHTTP.Tests/Streams/Stages/Lifecycle/`

- [ ] **Step 1: Create all Stages/ directories**

```powershell
$base = "src/TurboHTTP.Tests"
$dirs = @(
    "$base/Caching/Stages",
    "$base/Cookies/Stages",
    "$base/Http10/Stages",
    "$base/Http11/Stages",
    "$base/Http2/Stages",
    "$base/Http3/Stages",
    "$base/Semantics/Stages",
    "$base/Streams/Stages",
    "$base/Streams/Stages/Lifecycle"
)
foreach ($d in $dirs) { New-Item -ItemType Directory -Path $d -Force }
```

- [ ] **Step 2: Verify directories exist**

Run: `Get-ChildItem -Recurse -Directory src/TurboHTTP.Tests -Filter Stages`
Expected: 9 directories listed

---

### Task 2: Move Caching files (3 files)

**Files:**
- Move: `src/TurboHTTP.StreamTests/Caching/CacheBidiAsyncBodySpec.cs` → `src/TurboHTTP.Tests/Caching/Stages/CacheBidiAsyncBodySpec.cs`
- Move: `src/TurboHTTP.StreamTests/Caching/CacheBidiSharedResponseSpec.cs` → `src/TurboHTTP.Tests/Caching/Stages/CacheBidiSharedResponseSpec.cs`
- Move: `src/TurboHTTP.StreamTests/Caching/CacheBidiStageSpec.cs` → `src/TurboHTTP.Tests/Caching/Stages/CacheBidiStageSpec.cs`

- [ ] **Step 1: Move files**

```powershell
Move-Item "src/TurboHTTP.StreamTests/Caching/CacheBidiAsyncBodySpec.cs" "src/TurboHTTP.Tests/Caching/Stages/"
Move-Item "src/TurboHTTP.StreamTests/Caching/CacheBidiSharedResponseSpec.cs" "src/TurboHTTP.Tests/Caching/Stages/"
Move-Item "src/TurboHTTP.StreamTests/Caching/CacheBidiStageSpec.cs" "src/TurboHTTP.Tests/Caching/Stages/"
```

- [ ] **Step 2: Update namespaces**

In all 3 files, replace:
```
namespace TurboHTTP.StreamTests.Caching;
```
with:
```
namespace TurboHTTP.Tests.Caching.Stages;
```

---

### Task 3: Move Cookies files (1 file)

**Files:**
- Move: `src/TurboHTTP.StreamTests/Cookies/CookieBidiStageSpec.cs` → `src/TurboHTTP.Tests/Cookies/Stages/CookieBidiStageSpec.cs`

- [ ] **Step 1: Move file**

```powershell
Move-Item "src/TurboHTTP.StreamTests/Cookies/CookieBidiStageSpec.cs" "src/TurboHTTP.Tests/Cookies/Stages/"
```

- [ ] **Step 2: Update namespace**

Replace:
```
namespace TurboHTTP.StreamTests.Cookies;
```
with:
```
namespace TurboHTTP.Tests.Cookies.Stages;
```

---

### Task 4: Move Http10 files (4 files)

**Files:**
- Move: `src/TurboHTTP.StreamTests/Http10/Http10ConnectionStageSpec.cs` → `src/TurboHTTP.Tests/Http10/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http10/Http10ConnectionStageReconnectSpec.cs` → `src/TurboHTTP.Tests/Http10/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http10/Http10DecompressionPipelineSpec.cs` → `src/TurboHTTP.Tests/Http10/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http10/Http10EngineEndToEndSpec.cs` → `src/TurboHTTP.Tests/Http10/Stages/`

- [ ] **Step 1: Move files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Http10/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Http10/Stages/"
```

- [ ] **Step 2: Update namespaces**

In all 4 files, replace:
```
namespace TurboHTTP.StreamTests.Http10;
```
with:
```
namespace TurboHTTP.Tests.Http10.Stages;
```

---

### Task 5: Move Http11 files (5 files)

**Files:**
- Move: `src/TurboHTTP.StreamTests/Http11/Http11ConnectionStageSpec.cs` → `src/TurboHTTP.Tests/Http11/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http11/Http11ConnectionStageReconnectSpec.cs` → `src/TurboHTTP.Tests/Http11/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http11/Http11EngineEndToEndSpec.cs` → `src/TurboHTTP.Tests/Http11/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http11/Http11KeepAliveCloseSpec.cs` → `src/TurboHTTP.Tests/Http11/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http11/Http11ResponseCorrelationSpec.cs` → `src/TurboHTTP.Tests/Http11/Stages/`

- [ ] **Step 1: Move files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Http11/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Http11/Stages/"
```

- [ ] **Step 2: Update namespaces**

In all 5 files, replace:
```
namespace TurboHTTP.StreamTests.Http11;
```
with:
```
namespace TurboHTTP.Tests.Http11.Stages;
```

---

### Task 6: Move Http2 files (9 files)

**Files:**
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionTestHelper.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http20ConnectionStageSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http20ConnectionStageReconnectSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionBackpressureSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionFlowControlSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionFlowControlBatchingSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionGoAwaySpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionPingSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http2/Http2ConnectionStreamAcquireSpec.cs` → `src/TurboHTTP.Tests/Http2/Stages/`

- [ ] **Step 1: Move files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Http2/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Http2/Stages/"
```

- [ ] **Step 2: Update namespace declarations**

In all 9 files, replace:
```
namespace TurboHTTP.StreamTests.Http2;
```
with:
```
namespace TurboHTTP.Tests.Http2.Stages;
```

- [ ] **Step 3: Update using static references**

In these 6 files, replace the `using static` import:
- `Http2ConnectionBackpressureSpec.cs`
- `Http2ConnectionFlowControlSpec.cs`
- `Http2ConnectionFlowControlBatchingSpec.cs`
- `Http2ConnectionGoAwaySpec.cs`
- `Http2ConnectionPingSpec.cs`
- `Http2ConnectionStreamAcquireSpec.cs`

Replace:
```csharp
using static TurboHTTP.StreamTests.Http2.Http2ConnectionTestHelper;
```
with:
```csharp
using static TurboHTTP.Tests.Http2.Stages.Http2ConnectionTestHelper;
```

---

### Task 7: Move Http3 files (3 files)

**Files:**
- Move: `src/TurboHTTP.StreamTests/Http3/Http30CertificateValidationSpec.cs` → `src/TurboHTTP.Tests/Http3/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http3/Http30ConnectionStageSpec.cs` → `src/TurboHTTP.Tests/Http3/Stages/`
- Move: `src/TurboHTTP.StreamTests/Http3/Http30EngineEndToEndSpec.cs` → `src/TurboHTTP.Tests/Http3/Stages/`

- [ ] **Step 1: Move files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Http3/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Http3/Stages/"
```

- [ ] **Step 2: Update namespaces**

In all 3 files, replace:
```
namespace TurboHTTP.StreamTests.Http3;
```
with:
```
namespace TurboHTTP.Tests.Http3.Stages;
```

---

### Task 8: Move Semantics files (14 files)

**Files:**
- Move all `.cs` files from `src/TurboHTTP.StreamTests/Semantics/` → `src/TurboHTTP.Tests/Semantics/Stages/`

Files:
- `AltSvcBidiStageSpec.cs`
- `ContentEncodingBidiStageSpec.cs`
- `ContentEncodingDoubleDisposeSpec.cs`
- `ContentEncodingSpec.cs`
- `ExpectContinueBidiStageSpec.cs`
- `ExpectContinueSpec.cs`
- `RedirectChainSpec.cs`
- `RedirectCoreSpec.cs`
- `RedirectDownstreamCancelSpec.cs`
- `RetryCoreSpec.cs`
- `RetryDownstreamCancelSpec.cs`
- `RetryTimerSpec.cs`
- `TracingActivityLeakSpec.cs`
- `TracingBidiStageSpec.cs`

- [ ] **Step 1: Move files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Semantics/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Semantics/Stages/"
```

- [ ] **Step 2: Update namespaces**

In all 14 files, replace:
```
namespace TurboHTTP.StreamTests.Semantics;
```
with:
```
namespace TurboHTTP.Tests.Semantics.Stages;
```

---

### Task 9: Move Streams files (11 files) and Lifecycle subfolder (3 files)

**Files:**
- Move all `.cs` from `src/TurboHTTP.StreamTests/Streams/` → `src/TurboHTTP.Tests/Streams/Stages/`
- Move all `.cs` from `src/TurboHTTP.StreamTests/Streams/Lifecycle/` → `src/TurboHTTP.Tests/Streams/Stages/Lifecycle/`

Top-level Streams files (11):
- `EngineBidiFlowCompositionSpec.cs`
- `EnginePipelineDescriptorSpec.cs`
- `FeedbackBufferOptimizationSpec.cs`
- `HandlerBidiStageSpec.cs`
- `LoopbackBenchmarkStageSpec.cs`
- `RefererSanitizationSpec.cs`
- `StageCompletionRegressionSpec.cs`
- `StageOrderingIntegrationSpec.cs`
- `StageOrderingSpec.cs`
- `TransportRegistrySpec.cs`
- `VersionDispatchCachingSpec.cs`

Lifecycle files (3):
- `ClientStreamManagerSpec.cs`
- `ConsumerSpec.cs`
- `StreamOwnerSpec.cs`

- [ ] **Step 1: Move top-level Streams files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Streams/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Streams/Stages/"
```

- [ ] **Step 2: Move Lifecycle files**

```powershell
Get-ChildItem "src/TurboHTTP.StreamTests/Streams/Lifecycle/*.cs" | Move-Item -Destination "src/TurboHTTP.Tests/Streams/Stages/Lifecycle/"
```

- [ ] **Step 3: Update namespaces in Streams files**

In the 11 top-level Streams files, replace:
```
namespace TurboHTTP.StreamTests.Streams;
```
with:
```
namespace TurboHTTP.Tests.Streams.Stages;
```

- [ ] **Step 4: Update namespaces in Lifecycle files**

In the 3 Lifecycle files, replace:
```
namespace TurboHTTP.StreamTests.Streams.Lifecycle;
```
with:
```
namespace TurboHTTP.Tests.Streams.Stages.Lifecycle;
```

---

### Task 10: Update project configuration

**Files:**
- Modify: `src/TurboHTTP.slnx` — remove StreamTests project entry
- Modify: `src/TurboHTTP.Tests.Shared/TurboHTTP.Tests.Shared.csproj` — remove InternalsVisibleTo for StreamTests
- Modify: `src/TurboHTTP/TurboHTTP.csproj` — remove InternalsVisibleTo for StreamTests

- [ ] **Step 1: Remove StreamTests from solution**

In `src/TurboHTTP.slnx`, remove this line:
```xml
  <Project Path="TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj" />
```

- [ ] **Step 2: Remove InternalsVisibleTo from TurboHTTP.Tests.Shared.csproj**

In `src/TurboHTTP.Tests.Shared/TurboHTTP.Tests.Shared.csproj`, remove:
```xml
        <InternalsVisibleTo Include="TurboHTTP.StreamTests"/>
```

- [ ] **Step 3: Remove InternalsVisibleTo from TurboHTTP.csproj**

In `src/TurboHTTP/TurboHTTP.csproj`, remove:
```xml
        <InternalsVisibleTo Include="TurboHTTP.StreamTests"/>
```

---

### Task 11: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Remove StreamTests test command**

Remove this line from the Build & Test section:
```
dotnet test --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj  # stage
```

- [ ] **Step 2: Update spec-refactorer agent description**

In the Custom Agents table, update the `spec-refactorer` description to remove references to `TurboHTTP.StreamTests`. Change:
```
Refactor test specs: remove non-Protocol RFC traits, validate RFC section refs against Obsidian vault, strip `///` comments outside methods
```
to only reference `TurboHTTP.Tests`.

---

### Task 12: Build and test verification

- [ ] **Step 1: Restore and build**

Run:
```powershell
dotnet build --configuration Release src/TurboHTTP.slnx
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Run unit tests (full suite)**

Run:
```powershell
dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
```
Expected: All tests pass (previous Tests count + 53 migrated StreamTests).

- [ ] **Step 3: Verify stage tests run by namespace**

Run:
```powershell
dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.Http2.Stages"
```
Expected: 9 Http2 stage tests pass.

---

### Task 13: Delete StreamTests folder and commit

- [ ] **Step 1: Delete the StreamTests project folder**

```powershell
Remove-Item -Recurse -Force "src/TurboHTTP.StreamTests"
```

- [ ] **Step 2: Verify it's gone**

Run: `Test-Path "src/TurboHTTP.StreamTests"`
Expected: `False`

- [ ] **Step 3: Final build check**

Run:
```powershell
dotnet build --configuration Release src/TurboHTTP.slnx
```
Expected: Build succeeded.

- [ ] **Step 4: Commit all changes**

```bash
git add -A
git commit -m "refactor: consolidate StreamTests into TurboHTTP.Tests

Move 53 test files into Stages/ subfolders per component.
Update namespaces from TurboHTTP.StreamTests.* to TurboHTTP.Tests.*.Stages.
Remove StreamTests project from solution and InternalsVisibleTo entries."
```
