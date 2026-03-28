<!-- maggus-id: f9def089-336d-4c99-8626-2900796d6f7f -->
<!-- maggus-id: 20250325-140000-feature-022 -->
# Feature 022: TurboTrace — Termina-Style Developer Trace System

## Introduction

TurboHttp has production-grade observability (OTel ActivitySource, ETW EventSource, Metrics, DiagnosticListener) in `src/TurboHttp/Diagnostics/`. These are excellent for production telemetry but lack a lightweight, developer-focused trace system for debugging individual subsystems during development.

Inspired by [termina](https://github.com/Aaronontheweb/termina)'s diagnostics architecture, this feature adds a zero-cost, deferred-formatting trace system with per-instance source correlation, category/level filtering, and an `ILoggerFactory` bridge. It complements (does not replace) the existing OTel/ETW infrastructure.

### Architecture Context

- **Components involved:** `src/TurboHttp/Diagnostics/` (new files alongside existing instrumentation), `src/TurboHttp.Tests/Diagnostics/` (new test files following existing `NN_` naming)
- **Existing patterns reused:** DI extension methods from hosting layer, test DisplayName conventions from existing diagnostics tests
- **New patterns introduced:** First use of `[MethodImpl(MethodImplOptions.AggressiveInlining)]` in the codebase; `readonly struct` with deferred formatting; volatile field pattern for lock-free listener swap

### Design Decisions

1. **Dedicated methods per level** (termina pattern): `TurboTrace.Protocol.Debug(source, "msg")`, `.Info(source, "msg")`, etc. — 5 levels x 4 overloads (0-3 args) = 20 methods per category
2. **Pass `this` as source** (termina pattern): `TurboTrace.Protocol.Debug(this, "Frame received")` stores `source.GetType().Name` + `source.GetHashCode()` for per-instance correlation like `Http2FrameDecoder#A1B2C3D4`
3. **Plain interface** for `ITurboTraceListener` — no `IAsyncDisposable`; listeners manage their own lifecycle
4. **No FileTraceListener** — removed from scope; LoggerTraceListener covers all needs
5. **No TextWriterTraceListener** — LoggerTraceListener covers console/file via logging providers

## Goals

- Provide zero-cost trace calls when no listener is configured (~1-2ns overhead via volatile check + AggressiveInlining)
- Defer all string formatting until a listener actually consumes the event (zero allocation on the hot path)
- Support 10 HTTP-specific trace categories mappable to TurboHttp's architectural layers
- Support 5 severity levels (Trace, Debug, Info, Warning, Error) with numeric filtering
- Provide per-instance source correlation (`SourceType#SourceHash`) for debugging specific component instances
- Integrate with Microsoft.Extensions.Logging via `LoggerTraceListener` for seamless DI usage
- Coexist orthogonally with existing OTel/ETW/Metrics infrastructure — no interference

## Tasks

### TASK-022-001: Core Type Definitions
**Description:** As a developer, I want the foundational enum and struct types for the trace system so that all other components can build on stable type definitions.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-022-002, TASK-022-003
**Parallel:** no — all other tasks depend on these types

**Files to create:**
- `src/TurboHttp/Diagnostics/TurboTraceLevel.cs` (~30 lines)
- `src/TurboHttp/Diagnostics/TurboTraceCategory.cs` (~45 lines)
- `src/TurboHttp/Diagnostics/TraceEvent.cs` (~130 lines)
- `src/TurboHttp/Diagnostics/ITurboTraceListener.cs` (~25 lines)

**Implementation details:**

`TurboTraceLevel` — byte enum:
```
Trace=0, Debug=1, Info=2, Warning=3, Error=4
```

`TurboTraceCategory` — [Flags] ushort enum:
```
None=0, Connection=1, Protocol=2, Request=4, Response=8, Cache=16,
Redirect=32, Retry=64, Pool=128, Transport=256, Stream=512, All=1023
```

`TraceEvent` — readonly struct with fields:
- `long TimestampTicks` (from `Stopwatch.GetTimestamp()`)
- `TurboTraceLevel Level`
- `TurboTraceCategory Category`
- `string SourceType` (from `source.GetType().Name`)
- `int SourceHash` (from `source.GetHashCode()`)
- `string Template`
- `object? _arg0, _arg1, _arg2` + `byte _argCount`
- `string FormatMessage()` — switch on `_argCount`, calls `string.Format()` only when invoked
- Internal constructors for 0, 1, 2, 3 args

`ITurboTraceListener` — plain interface:
- `void Write(in TraceEvent evt)` — `in` for zero-copy pass
- `bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category)`

**Acceptance Criteria:**
- [x] `TurboTraceLevel` has 5 values with correct numeric ordering (Trace < Debug < Info < Warning < Error)
- [x] `TurboTraceCategory` has 10 categories + None + All, all powers of 2, bitwise OR works correctly
- [x] `TraceEvent.FormatMessage()` correctly formats 0-3 args using stored template
- [x] `TraceEvent` is a `readonly struct` (verified via reflection in tests)
- [x] `ITurboTraceListener.Write` takes `in TraceEvent` (pass by ref, no copy)
- [x] All files use `namespace TurboHttp.Diagnostics;` file-scoped namespace
- [x] No `#nullable enable` directives (project-level)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors/warnings

---

### TASK-022-002: TurboTrace Static API
**Description:** As a developer, I want a static API with per-category trace loggers so that I can instrument code without DI dependencies and with zero overhead when tracing is disabled.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-022-001
**Successors:** TASK-022-004, TASK-022-005
**Parallel:** yes — can run alongside TASK-022-003
**Model:** opus — complex nested class structure with 10 categories x 20 methods each

**Files to create:**
- `src/TurboHttp/Diagnostics/TurboTrace.cs` (~350 lines)

**Implementation details:**

Static class `TurboTrace` with:
- `private static volatile ITurboTraceListener? _listener` — single volatile field
- `private static volatile TurboTraceCategory _enabledCategories = TurboTraceCategory.None`
- `private static volatile TurboTraceLevel _minimumLevel = TurboTraceLevel.Trace`
- `Configure(ITurboTraceListener listener, TurboTraceCategory categories = All, TurboTraceLevel minimumLevel = Trace)` — sets all three volatile fields
- `Disable()` — sets `_listener = null`, `_enabledCategories = None`

Guard method:
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
internal static bool ShouldTrace(TurboTraceCategory category, TurboTraceLevel level)
{
    return _listener != null
        && (_enabledCategories & category) != 0
        && level >= _minimumLevel;
}
```

Internal write method:
```csharp
internal static void WriteEvent(in TraceEvent evt)
{
    _listener?.Write(in evt);
}
```

10 nested static classes as static properties:
`Connection`, `Protocol`, `Request`, `Response`, `Cache`, `Redirect`, `Retry`, `Pool`, `Transport`, `Stream`

Each nested class has the fixed category and exposes 5 level methods x 4 overloads:
```csharp
public static class Protocol
{
    private const TurboTraceCategory Category = TurboTraceCategory.Protocol;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Trace(object source, string message) { ... }
    public static void Trace(object source, string message, object? arg0) { ... }
    public static void Trace(object source, string message, object? arg0, object? arg1) { ... }
    public static void Trace(object source, string message, object? arg0, object? arg1, object? arg2) { ... }

    // Same 4 overloads for Debug, Info, Warning, Error
}
```

Each method body:
```csharp
public static void Debug(object source, string message, object? arg0)
{
    if (!ShouldTrace(Category, TurboTraceLevel.Debug)) return;
    WriteEvent(new TraceEvent(
        Stopwatch.GetTimestamp(), TurboTraceLevel.Debug, Category,
        source.GetType().Name, source.GetHashCode(),
        message, 1, arg0, null, null));
}
```

**Acceptance Criteria:**
- [x] `TurboTrace.Configure(listener)` enables tracing; `TurboTrace.Disable()` disables it
- [x] `ShouldTrace()` returns false when no listener configured (zero-cost path)
- [x] `ShouldTrace()` respects category filter (bitwise AND)
- [x] `ShouldTrace()` respects minimum level (>= comparison)
- [x] All 10 category classes exist with correct category constants
- [x] Each category has 5 levels x 4 overloads = 20 methods
- [x] `ShouldTrace()` is marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]`
- [x] Source type name and hash code are captured from the `source` parameter
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors/warnings

---

### TASK-022-003: LoggerTraceListener
**Description:** As a developer using Microsoft.Extensions.Logging, I want a trace listener that routes events to `ILoggerFactory` so that trace output integrates with my existing logging pipeline (Serilog, NLog, Console, etc.).

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-022-001
**Successors:** TASK-022-004
**Parallel:** yes — can run alongside TASK-022-002

**Files to create:**
- `src/TurboHttp/Diagnostics/LoggerTraceListener.cs` (~100 lines)

**Implementation details:**

Sealed class implementing `ITurboTraceListener`:
- Constructor: `LoggerTraceListener(ILoggerFactory loggerFactory, TurboTraceCategory categories = All, TurboTraceLevel minimumLevel = Debug)`
- Pre-creates `Dictionary<TurboTraceCategory, ILogger>` in constructor — one `ILogger` per individual category flag (not combinations)
- Logger names: `"TurboHttp.Trace.Connection"`, `"TurboHttp.Trace.Protocol"`, etc.
- `_enabledCategories` and `_minimumLevel` fields for filtering

Level mapping (values align exactly):
```
TurboTraceLevel.Trace   -> LogLevel.Trace       (0)
TurboTraceLevel.Debug   -> LogLevel.Debug        (1)
TurboTraceLevel.Info    -> LogLevel.Information  (2)
TurboTraceLevel.Warning -> LogLevel.Warning      (3)
TurboTraceLevel.Error   -> LogLevel.Error        (4)
```

Note: `LogLevel.Information = 2` vs `TurboTraceLevel.Info = 2` — values match but names differ. Direct cast `(LogLevel)evt.Level` works for all values.

`IsEnabled()`: checks `level >= _minimumLevel && (category & _enabledCategories) != 0`

`Write(in TraceEvent evt)`:
```csharp
if (_loggers.TryGetValue(evt.Category, out var logger))
{
    var logLevel = MapLevel(evt.Level);
    if (logger.IsEnabled(logLevel))
    {
        var message = evt.FormatMessage();
        logger.Log(logLevel, "[{SourceType}#{SourceHash:X8}] {Message}",
            evt.SourceType, evt.SourceHash, message);
    }
}
```

**Acceptance Criteria:**
- [ ] Constructor pre-creates 10 ILogger instances (one per category)
- [ ] Logger names follow `"TurboHttp.Trace.{Category}"` pattern
- [ ] Level mapping is correct for all 5 levels (including Info -> Information)
- [ ] `IsEnabled()` respects both level and category filters
- [ ] `Write()` checks `logger.IsEnabled()` before calling `FormatMessage()` (extra guard)
- [ ] `Write()` includes SourceType and SourceHash in log output
- [ ] Null `ILoggerFactory` throws `ArgumentNullException`
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors/warnings

---

### TASK-022-004: DI Extensions
**Description:** As a developer using dependency injection, I want extension methods on `IServiceCollection` so that I can configure TurboTrace with a single line in my service registration.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-022-002, TASK-022-003
**Successors:** TASK-022-005
**Parallel:** no — requires both static API and listener

**Files to create:**
- `src/TurboHttp/Diagnostics/TurboTraceExtensions.cs` (~60 lines)

**Implementation details:**

Static class `TurboTraceExtensions`:

```csharp
public static IServiceCollection AddTurboLoggerTracing(
    this IServiceCollection services,
    TurboTraceCategory categories = TurboTraceCategory.All,
    TurboTraceLevel minimumLevel = TurboTraceLevel.Debug)
{
    services.AddSingleton<ITurboTraceListener>(sp =>
    {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var listener = new LoggerTraceListener(loggerFactory, categories, minimumLevel);
        TurboTrace.Configure(listener, categories, minimumLevel);
        return listener;
    });
    return services;
}
```

Also provide a generic overload for custom listeners:
```csharp
public static IServiceCollection AddTurboTracing(
    this IServiceCollection services,
    ITurboTraceListener listener,
    TurboTraceCategory categories = TurboTraceCategory.All,
    TurboTraceLevel minimumLevel = TurboTraceLevel.Debug)
```

**Acceptance Criteria:**
- [ ] `AddTurboLoggerTracing()` registers `ITurboTraceListener` as singleton
- [ ] `AddTurboLoggerTracing()` calls `TurboTrace.Configure()` when resolved
- [ ] `AddTurboTracing()` accepts custom listener and configures immediately
- [ ] Extension methods return `IServiceCollection` for chaining
- [ ] Coexists with existing `AddTurboHttpClient()` registration without conflict
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors/warnings

---

### TASK-022-005: Test Suite
**Description:** As a maintainer, I want comprehensive unit tests for TurboTrace so that the zero-cost guard, deferred formatting, category/level filtering, and listener integration are all verified.

**Token Estimate:** ~60k tokens
**Predecessors:** TASK-022-002, TASK-022-004
**Successors:** none
**Parallel:** no — requires all production code complete

**Files to create:**
- `src/TurboHttp.Tests/Diagnostics/05_TurboTraceTests.cs` (~400 lines, ~40 tests)
- `src/TurboHttp.Tests/Diagnostics/06_LoggerTraceListenerTests.cs` (~250 lines, ~15 tests)

**Implementation details:**

Both test classes implement `IDisposable`, calling `TurboTrace.Disable()` in `Dispose()` to isolate static state between tests.

`05_TurboTraceTests.cs` uses a simple `MockTraceListener`:
```csharp
private sealed class MockTraceListener : ITurboTraceListener
{
    public List<TraceEvent> Events { get; } = new();
    public bool IsEnabled(TurboTraceLevel level, TurboTraceCategory category) => true;
    public void Write(in TraceEvent evt) => Events.Add(evt);
}
```

**Test scenarios for `05_TurboTraceTests.cs`:**

TraceEvent struct tests (~10):
- `"Diagnostics-Trace-001: FormatMessage returns template when no args"`
- `"Diagnostics-Trace-002: FormatMessage formats single arg correctly"`
- `"Diagnostics-Trace-003: FormatMessage formats two args correctly"`
- `"Diagnostics-Trace-004: FormatMessage formats three args correctly"`
- `"Diagnostics-Trace-005: TraceEvent captures TimestampTicks from Stopwatch"`
- `"Diagnostics-Trace-006: TraceEvent stores Level and Category correctly"`
- `"Diagnostics-Trace-007: TraceEvent stores SourceType from object.GetType().Name"`
- `"Diagnostics-Trace-008: TraceEvent stores SourceHash from object.GetHashCode()"`
- `"Diagnostics-Trace-009: TraceEvent is a readonly struct"`

TurboTrace static API tests (~15):
- `"Diagnostics-Trace-010: ShouldTrace returns false when listener is null"`
- `"Diagnostics-Trace-011: ShouldTrace returns true when listener enabled for category and level"`
- `"Diagnostics-Trace-012: ShouldTrace returns false when category not enabled"`
- `"Diagnostics-Trace-013: ShouldTrace returns false when level below minimum"`
- `"Diagnostics-Trace-014: Configure sets listener and enables tracing"`
- `"Diagnostics-Trace-015: Disable clears listener and stops tracing"`
- `"Diagnostics-Trace-016: Protocol.Debug writes event with correct category"`
- `"Diagnostics-Trace-017: Connection.Info writes event with correct category"`
- `"Diagnostics-Trace-018: Request.Warning writes event with correct level"`
- `"Diagnostics-Trace-019: Trace call with no listener produces no event"`
- `"Diagnostics-Trace-020: Category filtering via bitwise flags works"`
- `"Diagnostics-Trace-021: Level filtering via minimum level works"`
- `"Diagnostics-Trace-022: All 10 categories produce events with correct category flag"`
- `"Diagnostics-Trace-023: Source object type name and hash are captured"`
- `"Diagnostics-Trace-024: ShouldTrace is marked AggressiveInlining"`

Overload tests (~5):
- `"Diagnostics-Trace-025: Debug with 0 args writes event"`
- `"Diagnostics-Trace-026: Debug with 1 arg writes event with formatted message"`
- `"Diagnostics-Trace-027: Debug with 2 args writes event with formatted message"`
- `"Diagnostics-Trace-028: Debug with 3 args writes event with formatted message"`
- `"Diagnostics-Trace-029: All five level methods write events"`

Edge case tests (~5):
- `"Diagnostics-Trace-030: Null arg in format does not throw"`
- `"Diagnostics-Trace-031: Configure with All categories enables all 10 categories"`
- `"Diagnostics-Trace-032: Configure with None categories disables all tracing"`
- `"Diagnostics-Trace-033: Rapid Configure and Disable cycles do not throw"`
- `"Diagnostics-Trace-034: Multiple categories can be enabled via bitwise OR"`

**Test scenarios for `06_LoggerTraceListenerTests.cs`:**
- `"Diagnostics-LoggerListener-001: Constructor creates logger per category"`
- `"Diagnostics-LoggerListener-002: Write calls ILogger.Log with correct level"`
- `"Diagnostics-LoggerListener-003: Info level maps to LogLevel.Information"`
- `"Diagnostics-LoggerListener-004: Debug level maps to LogLevel.Debug"`
- `"Diagnostics-LoggerListener-005: Warning level maps to LogLevel.Warning"`
- `"Diagnostics-LoggerListener-006: Error level maps to LogLevel.Error"`
- `"Diagnostics-LoggerListener-007: Trace level maps to LogLevel.Trace"`
- `"Diagnostics-LoggerListener-008: IsEnabled respects minimum level"`
- `"Diagnostics-LoggerListener-009: IsEnabled respects category filter"`
- `"Diagnostics-LoggerListener-010: Write includes SourceType and SourceHash in output"`
- `"Diagnostics-LoggerListener-011: Write skips FormatMessage when logger not enabled"`
- `"Diagnostics-LoggerListener-012: Null ILoggerFactory throws ArgumentNullException"`
- `"Diagnostics-LoggerListener-013: Logger name follows TurboHttp.Trace.Category pattern"`
- `"Diagnostics-LoggerListener-014: Combined category filter works correctly"`
- `"Diagnostics-LoggerListener-015: DI extension registers singleton and configures TurboTrace"`

**Acceptance Criteria:**
- [ ] All ~55 tests pass
- [ ] DisplayName format follows `"Diagnostics-Trace-NNN: ..."` and `"Diagnostics-LoggerListener-NNN: ..."`
- [ ] Test classes are `sealed` and implement `IDisposable`
- [ ] `TurboTrace.Disable()` called in `Dispose()` for static state isolation
- [ ] No `#nullable enable` directives
- [ ] `dotnet test src/TurboHttp.sln` passes with zero failures
- [ ] Existing diagnostics tests (01-04) still pass unchanged

## Task Dependency Graph

```
TASK-022-001 ──> TASK-022-002 ──> TASK-022-004 ──> TASK-022-005
             └──> TASK-022-003 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-022-001 | ~25k | none | no | — |
| TASK-022-002 | ~50k | 001 | yes (with 003) | opus |
| TASK-022-003 | ~30k | 001 | yes (with 002) | — |
| TASK-022-004 | ~25k | 002, 003 | no | — |
| TASK-022-005 | ~60k | 002, 004 | no | — |

**Total estimated tokens:** ~190k

## Functional Requirements

- FR-1: When no `ITurboTraceListener` is configured, all trace calls must be no-ops with sub-nanosecond overhead (single volatile read + inlined branch)
- FR-2: `TraceEvent.FormatMessage()` must only allocate strings when called — storing template + raw args until that point
- FR-3: `TurboTraceCategory` must support bitwise combination for filtering (e.g., `Protocol | Connection`)
- FR-4: `TurboTraceLevel` must support `>=` comparison for minimum level filtering
- FR-5: Each of the 10 category loggers must expose 5 level methods (Trace, Debug, Info, Warning, Error) with 4 overloads each (0-3 args)
- FR-6: Trace calls must capture `source.GetType().Name` and `source.GetHashCode()` for per-instance correlation
- FR-7: `LoggerTraceListener` must pre-create one `ILogger` per category named `"TurboHttp.Trace.{Category}"`
- FR-8: `LoggerTraceListener` must check `logger.IsEnabled()` before calling `FormatMessage()` to avoid unnecessary allocation
- FR-9: `TurboTrace.Configure()` and `TurboTrace.Disable()` must be thread-safe via volatile fields (no locks)
- FR-10: DI extension `AddTurboLoggerTracing()` must register `ITurboTraceListener` as singleton and call `TurboTrace.Configure()` on resolution

## Non-Goals

- No `FileTraceListener` — removed from scope
- No `TextWriterTraceListener` — LoggerTraceListener covers console/file via logging providers
- No integration of trace calls into existing stages/decoders — that is a separate follow-up feature
- No performance benchmarks — zero-cost property is verified via unit tests (AggressiveInlining attribute check + no-listener-no-event assertion)
- No modifications to existing OTel/ETW/Metrics/DiagnosticListener infrastructure
- No more than 3 format args per trace call — sufficient for all practical use cases

## Technical Considerations

- **First AggressiveInlining usage:** This introduces `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to the codebase. Requires `using System.Runtime.CompilerServices;`
- **Volatile semantics:** `volatile` ensures visibility across threads without locks. Sufficient for "eventually consistent" listener swap — a trace call might use the old listener for one cycle after `Configure()`, which is acceptable
- **TreatWarningsAsErrors:** All code must compile with zero warnings. Pay attention to nullable reference types (project-level enabled) and unused variables
- **LogLevel mapping:** `TurboTraceLevel.Info (2)` maps to `LogLevel.Information (2)` — same numeric value but different enum name. A direct cast `(LogLevel)level` works for all values
- **Static state in tests:** `TurboTrace` uses static fields. Test classes MUST call `TurboTrace.Disable()` in `Dispose()` and should not run in parallel with each other (xUnit default: parallel per class is fine since each class isolates)

## Success Metrics

- All ~55 new unit tests pass
- Zero build warnings introduced
- Existing 98 diagnostics tests (files 01-04) continue to pass unchanged
- `ShouldTrace()` confirmed as `[AggressiveInlining]` via reflection test
- `TraceEvent` confirmed as `readonly struct` via reflection test

## Open Questions

None — all questions resolved.
