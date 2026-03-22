# Feature 009: Add Missing RFC-Tagged DisplayName Attributes to Tests

## Introduction

74–80 test methods across RFC9112 (45 tests in 11 files) and RFC9113 (29 tests in 2 files) are missing RFC-tagged `DisplayName` attributes. All other RFC folders (RFC1945, RFC6265, RFC7541, RFC9110, RFC9111, RFC9114, RFC9204) have 100% compliance. This feature closes the gap so that every test in an RFC folder carries a structured `DisplayName` following the project convention `"RFC<NUMBER>-<SECTION>-<CATEGORY>-<NNN>: description"`.

### Decisions

- **Legacy tests** (`07_EncoderLegacyTests.cs`): Tagged with category `LG` but using the RFC section of the tested functionality (e.g. `RFC9112-3-LG-001` for request-line, `RFC9112-5.4-LG-005` for host-header).
- **`27_Http2FrameTests.cs`**: Existing non-standard format (`RFC-9113-§4.1-cat-NNN`) will be normalised to the standard `RFC9113-4.1-CAT-NNN` format. Both existing and new tags get updated.
- **`16_EncoderBaselineTests.cs`**: Tags split by functionality — `CP` (Connection Preface), `FS` (Frame Serialization), `HP` (HPACK Compression), etc.

## Goals

- 100% DisplayName coverage across all RFC test folders
- Consistent `RFC<NUMBER>-<SECTION>-<CATEGORY>-<NNN>: description` format
- Normalise the one file (`27_Http2FrameTests.cs`) using a non-standard format
- Zero build or test regressions

## Tasks

### TASK-009-001: RFC9112 Encoder Tests — Request-Line, Host, Headers, Connection
**Description:** As a developer, I want RFC-tagged DisplayNames on all encoder tests in files 01–04 so that test output is traceable to RFC sections.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-009-006
**Parallel:** yes — can run alongside TASK-009-002, TASK-009-003, TASK-009-004, TASK-009-005

**Files & Changes:**

| File | Tests to tag | Category | Next number |
|------|-------------|----------|-------------|
| `01_EncoderRequestLineTests.cs` | 1 | RL | RFC9112-3-RL-011 |
| `02_EncoderHostHeaderTests.cs` | 3 | HH | RFC9112-5.4-HH-006..008 |
| `03_EncoderHeaderTests.cs` | 1 | HD | RFC9112-5-HD-009 |
| `04_EncoderConnectionTests.cs` | 2 | CN | RFC9112-9-CN-005..006 |

**Acceptance Criteria:**
- [x] 7 tests get RFC-tagged DisplayName attributes
- [x] Numbers continue from existing sequences (no gaps, no duplicates)
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [x] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9112"` passes

---

### TASK-009-002: RFC9112 Encoder Tests — Body & Legacy
**Description:** As a developer, I want RFC-tagged DisplayNames on body encoder tests and all legacy encoder tests so that every encoder test is RFC-traceable.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-009-006
**Parallel:** yes — can run alongside TASK-009-001, TASK-009-003, TASK-009-004, TASK-009-005

**Files & Changes:**

| File | Tests to tag | Category | Notes |
|------|-------------|----------|-------|
| `05_EncoderBodyTests.cs` | 5 | BD | RFC9112-6-BD-010..014 |
| `07_EncoderLegacyTests.cs` | 15–16 | LG | Mixed sections: `RFC9112-3-LG-001` (RL), `RFC9112-5.4-LG-00N` (HH), `RFC9112-9-LG-00N` (CN), `RFC9112-6-LG-00N` (BD) |

**Acceptance Criteria:**
- [x] 20–21 tests get RFC-tagged DisplayName attributes
- [x] Legacy tests use `LG` category with correct RFC section per functionality
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [x] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9112"` passes

---

### TASK-009-003: RFC9112 Decoder Tests — Status-Line, Headers, Body
**Description:** As a developer, I want RFC-tagged DisplayNames on all decoder tests in files 08–10 so that decoder coverage is fully traceable.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-009-006
**Parallel:** yes — can run alongside TASK-009-001, TASK-009-002, TASK-009-004, TASK-009-005

**Files & Changes:**

| File | Tests to tag | Category | Next number |
|------|-------------|----------|-------------|
| `08_DecoderStatusLineTests.cs` | 2–4 | SL | RFC9112-4-SL-012..015 |
| `09_DecoderHeaderTests.cs` | 7 | HD | RFC9112-5-HD-011..017 |
| `10_DecoderBodyTests.cs` | 5–6 | BD | RFC9112-6-BD-009..014 |

**Acceptance Criteria:**
- [x] 14–17 tests get RFC-tagged DisplayName attributes
- [x] Numbers continue from existing sequences
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [x] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9112"` passes

---

### TASK-009-004: RFC9112 Decoder Tests — Chunked & No-Body
**Description:** As a developer, I want RFC-tagged DisplayNames on the remaining decoder tests (chunked and no-body files).

**Token Estimate:** ~15k tokens
**Predecessors:** none
**Successors:** TASK-009-006
**Parallel:** yes — can run alongside TASK-009-001, TASK-009-002, TASK-009-003, TASK-009-005

**Files & Changes:**

| File | Tests to tag | Category | Next number |
|------|-------------|----------|-------------|
| `11_DecoderChunkedTests.cs` | 1 | CH | RFC9112-7-CH-012 |
| `12_DecoderNoBodyTests.cs` | 1 | NB | RFC9112-6-NB-010 |

**Acceptance Criteria:**
- [x] 2 tests get RFC-tagged DisplayName attributes
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [x] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9112"` passes

---

### TASK-009-005: RFC9113 — Encoder Baseline & Frame Tests
**Description:** As a developer, I want RFC-tagged DisplayNames on all HTTP/2 encoder baseline and frame serialisation tests, and normalise the non-standard format in `27_Http2FrameTests.cs`.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-009-006
**Parallel:** yes — can run alongside TASK-009-001, TASK-009-002, TASK-009-003, TASK-009-004

**Files & Changes:**

| File | Tests to tag | Notes |
|------|-------------|-------|
| `16_EncoderBaselineTests.cs` | 29 | Split by functionality: `CP` (Connection Preface), `FS` (Frame Serialization), `HP` (HPACK), `PP` (Push Promise) etc. |
| `27_Http2FrameTests.cs` | 6 new + 2 existing to normalise | Normalise `RFC-9113-§4.1-cat-NNN` → `RFC9113-4.1-FS-NNN` standard format |

**Acceptance Criteria:**
- [x] 23 new DisplayName tags added to `16_EncoderBaselineTests.cs` (actual count; feature estimate of 29 was approximate)
- [x] 6 new DisplayName tags added to `27_Http2FrameTests.cs`
- [x] 2 existing non-standard tags in `27_Http2FrameTests.cs` normalised to `RFC9113-4.1-FS-NNN`
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [x] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9113"` passes

---

### TASK-009-006: Validation Gate
**Description:** As a developer, I want to verify that all tests in RFC folders now have DisplayName attributes and that no regressions were introduced.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-009-001, TASK-009-002, TASK-009-003, TASK-009-004, TASK-009-005
**Successors:** none
**Parallel:** no — must run after all other tasks complete

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors
- [ ] `dotnet test src/TurboHttp.sln` — all tests pass, zero failures
- [ ] Grep for `[Fact]` and `[Theory]` without `DisplayName` in `src/TurboHttp.Tests/RFC*/` returns zero results
- [ ] Grep for non-standard `RFC-9113-§` format returns zero results
- [ ] Report: total DisplayName count per RFC folder

## Task Dependency Graph

```
TASK-009-001 ──┐
TASK-009-002 ──┤
TASK-009-003 ──┼──→ TASK-009-006
TASK-009-004 ──┤
TASK-009-005 ──┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-009-001 | ~25k | none | yes (with 002–005) | haiku |
| TASK-009-002 | ~35k | none | yes (with 001, 003–005) | — |
| TASK-009-003 | ~30k | none | yes (with 001–002, 004–005) | haiku |
| TASK-009-004 | ~15k | none | yes (with 001–003, 005) | haiku |
| TASK-009-005 | ~40k | none | yes (with 001–004) | — |
| TASK-009-006 | ~15k | 001–005 | no | haiku |

**Total estimated tokens:** ~160k

## Functional Requirements

- FR-1: Every `[Fact]` and `[Theory]` in `src/TurboHttp.Tests/RFC9112/` must have a `DisplayName` following `RFC9112-<SECTION>-<CATEGORY>-<NNN>: description`
- FR-2: Every `[Fact]` and `[Theory]` in `src/TurboHttp.Tests/RFC9113/` must have a `DisplayName` following `RFC9113-<SECTION>-<CATEGORY>-<NNN>: description`
- FR-3: Numbers must continue from existing sequences — no gaps, no duplicates within a file
- FR-4: Legacy tests (`07_EncoderLegacyTests.cs`) use category `LG` with the RFC section matching the tested functionality
- FR-5: Non-standard format `RFC-9113-§4.1-cat-NNN` in `27_Http2FrameTests.cs` must be normalised to `RFC9113-4.1-FS-NNN`
- FR-6: `16_EncoderBaselineTests.cs` tags are split by functionality (`CP`, `FS`, `HP`, etc.) not a single blanket category

## Non-Goals

- Not adding DisplayNames to non-RFC test folders (Hosting, Streams/Loopback benchmarks)
- Not modifying test method names — only `DisplayName` attributes
- Not changing test logic or assertions
- Not adding new tests

## Technical Considerations

- The `DisplayName` attribute is part of xUnit's `[Fact(DisplayName = "...")]` and `[Theory(DisplayName = "...")]` syntax
- Category codes must be unique per logical group within a file but can repeat across encoder/decoder files (e.g. `HD` for headers exists in both `03_EncoderHeaderTests.cs` and `09_DecoderHeaderTests.cs`)
- For `[Theory]` tests, the DisplayName goes on the `[Theory]` attribute, not on `[InlineData]`

## Success Metrics

- 0 tests without DisplayName in any `RFC*/` folder
- 0 non-standard DisplayName formats remaining
- All existing tests continue to pass (zero regressions)

## Open Questions

None — all resolved.
