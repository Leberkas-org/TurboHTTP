# Feature 007: Exception Hierarchy & Cleanup

## Introduction

TurboHttp has 9 custom exception classes that all inherit directly from `System.Exception` (flat hierarchy). There is no common base, some exceptions are embedded in unrelated files, and error classification is inconsistent. This feature introduces a 3-tier hierarchy (`TurboHttpException` → category → concrete exception), extracts embedded exceptions into their own files, and merges redundant types.

### Architecture Context (from CLAUDE.md)

- **Layers affected**: Protocol (`TurboHttp/Protocol/`), Transport (`TurboHttp/Transport/`), Streams (`TurboHttp/Streams/Stages/`)
- **Catch patterns in stages**: 3 modes — absorb+log, FailStage, data re-emission — remain unchanged
- **Public API**: Exception types are part of the public API (users catch them in catch blocks)
- **Backward compatibility**: Inheritance is transparent — existing `catch(HttpDecoderException)` continues to work

### Current Exception Inventory

| Exception | Location | Error Enum | Issue |
|-----------|----------|------------|-------|
| `HttpDecoderException` | `Protocol/HttpDecoderException.cs` | `HttpDecoderError` (20 values) | OK — own file |
| `HpackException` | `Protocol/RFC7541/HpackDecoder.cs` | None | **Embedded in decoder file** |
| `Http2Exception` | `Protocol/RFC9113/Http2Exception.cs` | `Http2ErrorCode` + `Http2ErrorScope` | OK — own file, best designed |
| `Http3ConnectionException` | `Protocol/RFC9114/Http3ConnectionException.cs` | `Http3ErrorCode` | OK — but needs rename to `Http3Exception` |
| `Http3SettingsException` | `Protocol/RFC9114/Http3Settings.cs` | None | **Embedded, redundant — merge into Http3Exception** |
| `QpackException` | `Protocol/RFC9204/QpackException.cs` | None | OK — own file |
| `RedirectException` | `Protocol/RFC9110/RedirectException.cs` | `RedirectError` (4 values) | OK — own file |
| `RedirectDowngradeException` | `Protocol/RFC9110/RedirectException.cs` | None | **Redundant — merge into RedirectException** |
| `AbruptCloseException` | `Transport/ClientByteMover.cs` | None | **Embedded in mover file** |

## Goals

- Common base `TurboHttpException` for all TurboHttp errors
- Category tier for `catch(TurboProtocolException)` (all RFC violations) and `catch(TurboTransportException)` (connection errors)
- Embedded exceptions extracted into their own files
- `Http3SettingsException` merged into `Http3Exception`
- `RedirectDowngradeException` merged into `RedirectException` (new `RedirectError.ProtocolDowngrade`)

### Target Hierarchy

```
TurboHttpException (abstract)
├── TurboProtocolException (abstract)
│   ├── HttpDecoderException        (RFC 1945/9112 — has HttpDecoderError enum)
│   ├── HpackException              (RFC 7541)
│   ├── QpackException              (RFC 9204)
│   ├── Http2Exception              (RFC 9113 — has Http2ErrorCode + Http2ErrorScope)
│   └── Http3Exception              (RFC 9114 — has Http3ErrorCode, merged with Http3SettingsException)
├── TurboTransportException (abstract)
│   └── AbruptCloseException
└── RedirectException               (RFC 9110 — has RedirectError enum, absorbs RedirectDowngradeException)
```

## Tasks

### TASK-007-001: Create base exception classes
**Description:** As a developer, I want abstract base exception classes so all TurboHttp exceptions share a common base.

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-007-002, TASK-007-003, TASK-007-004
**Parallel:** no

**Acceptance Criteria:**
- [x] New file `src/TurboHttp/TurboHttpException.cs` containing:
  - `public abstract class TurboHttpException : Exception` (3 constructors: message, message+inner, default)
  - `public abstract class TurboProtocolException : TurboHttpException`
  - `public abstract class TurboTransportException : TurboHttpException`
- [x] All classes are `abstract` (not directly instantiable)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors

### TASK-007-002: Extract embedded exceptions into own files
**Description:** As a developer, I want each exception class to live in its own file so it's easy to find and maintain.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-007-001
**Successors:** TASK-007-005
**Parallel:** yes — can run alongside TASK-007-003 and TASK-007-004

**Acceptance Criteria:**
- [x] `HpackException` extracted from `src/TurboHttp/Protocol/RFC7541/HpackDecoder.cs` → new file `src/TurboHttp/Protocol/RFC7541/HpackException.cs`
- [x] `AbruptCloseException` extracted from `src/TurboHttp/Transport/ClientByteMover.cs` → new file `src/TurboHttp/Transport/AbruptCloseException.cs`
- [x] Original files no longer contain the class definitions
- [x] No logic changes in original files
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors

### TASK-007-003: Re-base exception classes on new hierarchy
**Description:** As a developer, I want all exceptions to inherit from the appropriate category base so I can catch by category.

**Token Estimate:** ~30k tokens
**Predecessors:** TASK-007-001
**Successors:** TASK-007-005
**Parallel:** yes — can run alongside TASK-007-002 and TASK-007-004

**Acceptance Criteria:**
- [x] `HttpDecoderException` : `Exception` → `: TurboProtocolException`
- [x] `HpackException` : `Exception` → `: TurboProtocolException`
- [x] `QpackException` : `Exception` → `: TurboProtocolException`
- [x] `Http2Exception` : `Exception` → `: TurboProtocolException`
- [x] `Http3ConnectionException` : `Exception` → `: TurboProtocolException`
- [x] `AbruptCloseException` : `Exception` → `: TurboTransportException`
- [x] `RedirectException` : `Exception` → `: TurboHttpException`
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors

### TASK-007-004: Merge Http3SettingsException and RedirectDowngradeException
**Description:** As a developer, I want to eliminate redundant exception types so the API is more consistent.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-007-001
**Successors:** TASK-007-005
**Parallel:** yes — can run alongside TASK-007-002 and TASK-007-003
**Model:** opus — requires careful refactoring across multiple files

**Acceptance Criteria:**
- [x] `Http3ConnectionException` renamed to `Http3Exception` (file: `Http3Exception.cs`)
- [x] `Http3SettingsException` removed from `Http3Settings.cs`
- [x] All `throw new Http3SettingsException(...)` → `throw new Http3Exception(Http3ErrorCode.SettingsError, ...)`
- [x] `RedirectDowngradeException` removed from `RedirectException.cs`
- [x] New enum value: `RedirectError.ProtocolDowngrade`
- [x] All `throw new RedirectDowngradeException(...)` → `throw new RedirectException(..., RedirectError.ProtocolDowngrade)`
- [x] All `catch (RedirectDowngradeException)` → `catch (RedirectException ex) when (ex.Error == RedirectError.ProtocolDowngrade)`
- [x] All `catch (Http3SettingsException)` → `catch (Http3Exception ex) when (ex.ErrorCode == Http3ErrorCode.SettingsError)`
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors

### TASK-007-005: Update tests and validation gate
**Description:** As a developer, I want to ensure all tests pass after the exception refactoring.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-007-002, TASK-007-003, TASK-007-004
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [x] All `Assert.Throws<Http3SettingsException>` → `Assert.Throws<Http3Exception>` (with ErrorCode check)
- [x] All `Assert.Throws<RedirectDowngradeException>` → `Assert.Throws<RedirectException>` (with Error check)
- [x] Grep for `Http3SettingsException` across entire repo returns 0 matches
- [x] Grep for `RedirectDowngradeException` across entire repo returns 0 matches
- [x] Grep for `class \w+Exception\b.*: Exception\b` in production code returns 0 matches (all inherit from TurboHttp base)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` — 0 errors, 0 warnings
- [x] `dotnet test src/TurboHttp.sln` — all tests green

## Task Dependency Graph

```
                ┌── TASK-007-002 ──┐
TASK-007-001 ──→├── TASK-007-003 ──├──→ TASK-007-005
                └── TASK-007-004 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-007-001 | ~15k | none | no | — |
| TASK-007-002 | ~25k | 001 | yes (with 003, 004) | — |
| TASK-007-003 | ~30k | 001 | yes (with 002, 004) | — |
| TASK-007-004 | ~40k | 001 | yes (with 002, 003) | opus |
| TASK-007-005 | ~35k | 002, 003, 004 | no | — |

**Total estimated tokens:** ~145k

## Functional Requirements

- FR-1: `catch (TurboHttpException)` must catch all TurboHttp exceptions
- FR-2: `catch (TurboProtocolException)` must catch all RFC violations (HttpDecoder, Hpack, Qpack, Http2, Http3)
- FR-3: `catch (TurboTransportException)` must catch all connection errors (AbruptClose)
- FR-4: Existing specific catches (`catch (HttpDecoderException)`) continue to work unchanged
- FR-5: `Http3Exception` must distinguish connection and settings errors via `Http3ErrorCode`
- FR-6: `RedirectException` must indicate protocol downgrade via `RedirectError.ProtocolDowngrade`
- FR-7: Each exception class lives in its own file (no embedded definitions)

## Non-Goals

- No new exception types beyond the base classes
- No changes to the 3 stage error handling modes (absorb, FailStage, re-emit)
- No changes to error enums `HttpDecoderError`, `Http2ErrorCode`, `Http2ErrorScope`, `Http3ErrorCode` (beyond additions)
- No ErrorCode/enum addition for `HpackException` or `QpackException` (future feature)
- No serialization/deserialization of exceptions

## Technical Considerations

- `sealed` modifier stays on all concrete exceptions
- Base classes are `abstract` and cannot be instantiated directly
- `TreatWarningsAsErrors` is globally enabled — removed types must not be referenced anywhere
- `Http2Exception` uses primary constructor (`public sealed class Http2Exception(...)`) — base class must be compatible
- Stages using `catch (Exception ex)` (absorb pattern) are unaffected — they catch everything already

## Success Metrics

- 0 exceptions inherit directly from `System.Exception`
- 0 embedded exception definitions in unrelated files
- 0 references to removed types (`Http3SettingsException`, `RedirectDowngradeException`)
- All tests green
- `catch (TurboProtocolException)` catchable (verifiable with new unit test)

## Open Questions

_None — all questions resolved._
