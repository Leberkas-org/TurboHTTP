<!-- maggus-id: 20250325-143000-feature-025 -->

# Feature 025: HTTP/3 Engine ReadonlyMemory Item Wrapping

## Introduction

Ensure all `ReadonlyMemory<byte>` data flowing through the HTTP/3 pipeline (`Http30Engine`, `Http30EncoderStage`, `Http30DecoderStage`, and related stages) is wrapped in the existing `DataItem` record (defined in `TurboHttp/Internal/Messages.cs`) rather than being sent directly as bare `ReadonlyMemory<byte>`.

This leverages the existing **message-based architecture** (already used for HTTP/1.0, 1.1, and 2.0), prevents accidental leaks of naked buffers, and establishes consistent abstraction patterns across all HTTP protocol versions.

### Architecture Context

- **Vision alignment:** Consistent, type-safe data flow across all HTTP protocol versions. Memory safety and ownership clarity at protocol boundaries.
- **Components involved:**
  - `Http30Engine` (Streams/) — HTTP/3 protocol engine, request/response routing
  - `Http30EncoderStage` (Streams/Stages/Encoding/) — serializes HTTP/3 requests to frames
  - `Http30DecoderStage` (Streams/Stages/Decoding/) — parses HTTP/3 frame data
  - `Http3RequestEncoder` (Protocol/RFC9114/) — HTTP/3 request encoding logic
  - `Http3ResponseDecoder` (Protocol/RFC9114/) — HTTP/3 response decoding logic
  - `Http3FrameEncoder` / `Http3FrameDecoder` (Protocol/RFC9114/) — frame serialization/parsing
  - Existing `DataItem`, `ConnectItem` patterns in `Streams/Routing/`
- **New patterns:**
  - Reuse existing `DataItem` record (IMemoryOwner<byte> + Length + RequestEndpoint.Key) for all HTTP/3 frame data
  - Optional: extend with `Http3FrameMetadataItem` wrapper if frame-type/stream-ID tagging is needed (analogous to `Http3TaggedItem`)
  - Static analyzer/lint rule to detect naked `ReadonlyMemory<byte>` in HTTP/3 code
- **Architectural update needed:** Yes — update ARCHITECTURE.md to document the unified item-based abstraction for all protocol versions (new "Data Item Protocol" section)

## Goals

- Audit `Http30Engine` and all HTTP/3 stages to identify naked `ReadonlyMemory<byte>` references
- Wrap all bare `ReadonlyMemory<byte>` in the existing `DataItem` record (from `TurboHttp/Internal/Messages.cs`)
- If frame-type/stream-ID metadata is needed at the pipeline level, use `Http3TaggedItem` or create `Http3FrameMetadataItem` wrapper
- Establish consistent wrapping pattern matching HTTP/1.0, HTTP/1.1, HTTP/2 (which already use `DataItem` and message-based flow)
- Add static validation (lint rule or analyzer) to prevent regressions
- Zero naked `ReadonlyMemory<byte>` references in HTTP/3 production code after completion
- All existing HTTP/3 stage tests pass without modification (wrapping is internal to stage logic)

## Tasks

### TASK-025-001: Analyze DataItem Suitability for HTTP/3 and Design Metadata Strategy
**Description:** As an architect, I want to validate that the existing `DataItem` record (IMemoryOwner<byte> + Length + RequestEndpoint) is sufficient for HTTP/3 frame wrapping, and determine if additional metadata (stream-ID, frame-type) should be carried separately (e.g., via `Http3TaggedItem` or a new `Http3FrameMetadataItem`).

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-025-003
**Parallel:** yes — can analyze in parallel with audit

**Acceptance Criteria:**
- [ ] Analyze existing `DataItem` in `src/TurboHttp/Internal/Messages.cs`:
  - Confirm fields: `Memory: IMemoryOwner<byte>`, `Length: int`, `Key: RequestEndpoint`
  - Check how HTTP/1.1 and HTTP/2 currently use `DataItem` (how do they handle metadata?)
  - Assess: is carrying frame-type/stream-ID metadata necessary at the pipeline level, or handled locally in stages?
- [ ] Analyze existing `Http3TaggedItem` and `Http3InputTaggedItem`:
  - Could these be extended to carry frame metadata (frame-type, stream-ID)?
  - Or should a new `Http3FrameMetadataItem` record be defined?
- [ ] Decision document: `src/TurboHttp/.maggus/design_025_metadata_strategy.md`
  - Option A: Use `DataItem` as-is, metadata handled locally in stages
  - Option B: Create `Http3FrameMetadataItem(IOutputItem Inner, Http3FrameType Type, uint StreamId)`
  - Option C: Extend `Http3TaggedItem` to carry frame metadata
  - Recommendation + rationale
- [ ] No code changes in this task — analysis only

### TASK-025-002: Audit Http30Engine and Identify Naked ReadonlyMemory References
**Description:** As a code analyst, I want to scan `Http30Engine` and related stages to identify all places where `ReadonlyMemory<byte>` is used directly (without wrapping) so that I can plan the conversion systematically.

**Token Estimate:** ~25k tokens
**Predecessors:** none
**Successors:** TASK-025-005
**Parallel:** yes — can audit in parallel with type definition

**Acceptance Criteria:**
- [ ] Grep all `Http30*` files for `ReadonlyMemory<byte>` usage
- [ ] Identify `OutboundWriter` calls with direct `ReadonlyMemory<byte>` payloads
- [ ] Identify `InboundReader` expectations (does it expect wrapped items or bare buffers?)
- [ ] Check `Http3RequestEncoder.Encode()` and `Http3ResponseDecoder.Decode()` return types
- [ ] Check `Http3FrameEncoder.WriteTo()` and frame parsing logic
- [ ] Create comprehensive audit report: `src/TurboHttp/Http30Engine.AuditReport.md`
  - List each file, line number, and usage pattern
  - Categorize as: "outbound encoder", "inbound decoder", "frame handling", "stream management"
  - Estimate conversion effort per category
- [ ] Summary: total naked references found, grouped by stage/component

### TASK-025-003: Design DataItem Integration Strategy for HTTP/3
**Description:** As an architect, I want to design how `DataItem` (the existing record) will be used in HTTP/3 encoder/decoder stages so that wrapping is minimal-impact and follows the same patterns as HTTP/1.1 and HTTP/2.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-025-001
**Successors:** TASK-025-004, TASK-025-005
**Parallel:** no — depends on metadata strategy decision

**Acceptance Criteria:**
- [ ] Decision document: `src/TurboHttp/.maggus/design_025_integration.md`
- [ ] Define wrapping points: Where exactly does `ReadonlyMemory<byte>` → `DataItem` conversion happen?
  - In `Http30EncoderStage` before emitting to downstream?
  - In `Http30DecoderStage` before processing frame data?
  - Or at both boundaries?
- [ ] Determine IMemoryOwner strategy:
  - Should buffers be allocated via `MemoryPool<byte>.Shared`?
  - Should ownership be tracked (rented vs. managed)?
  - How does this compare to HTTP/1.1 and HTTP/2 usage?
- [ ] Decide on metadata handling:
  - If using Option B or C from TASK-025-001, design the wrapping strategy (e.g., `new Http3FrameMetadataItem(dataItem, frameType, streamId)`)
  - If using Option A, document which metadata lives in stages vs. which flows through items
- [ ] Performance implications: overhead of wrapping vs. type safety benefits
- [ ] Backward compatibility: Any breaking changes to existing stage interfaces?
- [ ] Sample pseudocode showing encoder → DataItem → writer flow
- [ ] Comparison table: HTTP/1.1 vs HTTP/2 vs HTTP/3 DataItem usage patterns

### TASK-025-004: Implement DataItem Wrapping in Http30EncoderStage
**Description:** As a stream engineer, I want to modify `Http30EncoderStage` to wrap all outbound `ReadonlyMemory<byte>` frame data in `DataItem` (with IMemoryOwner) before emitting downstream so that all encoder output is typed.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-025-001, TASK-025-003
**Successors:** TASK-025-006
**Parallel:** no — requires integration design

**Acceptance Criteria:**
- [ ] `Http30EncoderStage` modified to:
  - Allocate buffer via `MemoryPool<byte>.Shared.Rent(size)` (or equivalent)
  - Create `DataItem(memoryOwner, length, key)` for each frame from `Http3RequestEncoder`
  - If metadata needed: wrap in `Http3FrameMetadataItem` (per TASK-025-001 decision)
  - Emit `DataItem` (not bare `ReadonlyMemory<byte>`) to outlet
- [ ] No bare `ReadonlyMemory<byte>` references in stage outlet logic
- [ ] Existing unit tests pass without modification (wrapping is internal)
- [ ] New unit test: `Http30EncoderStageTests_DataItemWrapping` validates:
  - Each frame produces correct `DataItem` wrapper
  - IMemoryOwner lifecycle is correct (disposal via downstream consumer)
  - Key/metadata preserved correctly
- [ ] Zero compile warnings; static analyzer check passes
- [ ] Code review: memory safety, no buffer leaks, consistent with HTTP/1.1 and HTTP/2 patterns

### TASK-025-005: Implement DataItem Unwrapping in Http30DecoderStage
**Description:** As a stream engineer, I want to modify `Http30DecoderStage` to accept `DataItem` inputs and unwrap them for frame parsing, while emitting typed responses wrapped in `DataItem` as well.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-025-001, TASK-025-003
**Successors:** TASK-025-006
**Parallel:** no — requires integration design

**Acceptance Criteria:**
- [ ] `Http30DecoderStage` modified to:
  - Accept `DataItem` from inbound stream
  - Extract IMemoryOwner and create Span/Memory for frame parsing
  - Emit parsed responses wrapped in `DataItem` (with proper memory ownership)
  - Properly dispose IMemoryOwner when done parsing
- [ ] No bare `ReadonlyMemory<byte>` references in decoder outlets
- [ ] Existing unit tests pass without modification
- [ ] New unit test: `Http30DecoderStageTests_DataItemWrapping` validates:
  - Wrapped input is correctly unwrapped for parsing
  - Parsed responses are re-wrapped in items
  - IMemoryOwner lifecycle is correct
  - Metadata preserved through decode cycle
- [ ] Zero compile warnings; static analyzer check passes
- [ ] Consistent with HTTP/1.1 and HTTP/2 decoder patterns

### TASK-025-006: Audit and Verify Http3FrameEncoder / Http3FrameDecoder Don't Leak Buffers
**Description:** As an engineer, I want to ensure `Http3FrameEncoder` and `Http3FrameDecoder` don't return bare `ReadonlyMemory<byte>` without stage-level wrapping, so frame handling remains type-safe.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-025-004, TASK-025-005
**Successors:** TASK-025-007
**Parallel:** no — requires stage wrapping to be complete

**Acceptance Criteria:**
- [ ] Audit `Http3FrameEncoder.WriteTo()` and `Http3Frame` subclasses:
  - Identify any public APIs returning bare `ReadonlyMemory<byte>` or `Span<byte>`
  - Confirm all are internal/protocol-level utilities (not exposed through stages)
  - Document which APIs are safe for internal-only use (without wrapping)
- [ ] Audit `Http3FrameDecoder.TryDecode()`:
  - Confirm input is received as `ReadonlyMemory<byte>` from upstream stages
  - Verify no intermediate buffers leak to callers
  - Confirm output is passed to stage-level wrappers (not exposed naked)
- [ ] Document the boundary:
  - Frame-level APIs work with raw buffers internally (no wrapping needed)
  - Stage-level wrapping happens at Http30EncoderStage / Http30DecoderStage boundaries
  - Comments clarify the contract
- [ ] Unit tests validate boundary (frame tests use raw buffers, stage tests use DataItem)
- [ ] Static analyzer report: zero violations in RFC9114/ folder (for public APIs)

### TASK-025-007: Create Static Validation Rule (Lint / Analyzer)
**Description:** As a quality engineer, I want to create a static validator that detects stage-level `ReadonlyMemory<byte>` references without `DataItem` wrapping so that regressions are caught at build time.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-025-002, TASK-025-006
**Successors:** TASK-025-008
**Parallel:** no — requires understanding final wrapping strategy

**Acceptance Criteria:**
- [ ] Implement one of:
  - **Option A:** Custom Roslyn analyzer in `src/TurboHttp.Analyzers/` that flags naked `ReadonlyMemory<byte>` in `Http30*Stage` outlet logic
  - **Option B:** EditorConfig rule + msbuild target that greps for violations
  - **Option C:** Documented lint rule in CI post-build
- [ ] Rule is configured to:
  - Flag `ReadonlyMemory<byte>` writes to stage outlets (e.g., `Emit(readonlyMemory)` without wrapping in `DataItem`)
  - Allow exceptions for internal/protocol-level APIs (with `// approved-naked-memory` comment)
  - Report violations with file + line number
- [ ] Rule is integrated into CI/CD pipeline (pre-commit or build)
- [ ] Test: attempt to introduce a naked outlet reference and verify rule catches it
- [ ] Documentation: `src/TurboHttp/HTTP3_MEMORY_WRAPPING_RULES.md` explains rule, exceptions, and boundary between frame-level (naked ok) vs. stage-level (wrap required)

### TASK-025-008: Validate DataItem Usage Consistency Across HTTP/1.1, HTTP/2, HTTP/3
**Description:** As an architect, I want to confirm that `DataItem` wrapping is consistently applied across HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 stages so that all versions use the same message-based abstraction.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-025-007
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Audit report: `src/TurboHttp/.maggus/audit_025_dataitem_consistency.md`
- [ ] Analyze `Http11EncoderStage`, `Http11DecoderStage`, `Http20EncoderStage`, `Http20DecoderStage`
  - Do they currently emit/consume `DataItem`?
  - Are there any naked `ReadonlyMemory<byte>` outlets? If so, should they be wrapped?
  - How is metadata (stream-ID, frame-type) handled in each version?
- [ ] Identify any inconsistencies or gaps
- [ ] Propose lint rules or patterns for future consistency (no code changes in this task)
- [ ] Mark any follow-up work as "Future Feature 027+" if needed
- [ ] Document the unified message-based abstraction across all protocols

### TASK-025-009: Comprehensive Integration Testing
**Description:** As a QA engineer, I want to run all existing HTTP/3 stage tests and integration tests to ensure the wrapping changes don't break functionality.

**Token Estimate:** ~25k tokens
**Predecessors:** TASK-025-006
**Successors:** TASK-025-010
**Parallel:** no — requires all wrapping to be complete

**Acceptance Criteria:**
- [ ] Run full test suite: `dotnet test src/TurboHttp.StreamTests/ -c Release --filter "*Http30*"`
- [ ] Run integration tests: `dotnet test src/TurboHttp.IntegrationTests/ -c Release --filter "*H30*"` (or similar)
- [ ] 100% pass rate on all HTTP/3 tests
- [ ] Run full build: `dotnet build --configuration Release src/TurboHttp.sln`
  - Zero compile errors
  - Zero warnings (except approved suppressions)
  - Analyzer/lint rule runs and reports zero violations
- [ ] Create test report: `src/TurboHttp/.maggus/test_results_025.md`
  - Test count by category (encoder, decoder, frame handling, integration)
  - Pass rates
  - Performance baseline (no regression vs. baseline)
- [ ] Spot check: manually trace one request through Http30Engine and verify item wrapping at each stage

### TASK-025-010: Documentation and Code Review
**Description:** As a documentation owner, I want to update CLAUDE.md and create a summary document so that future developers understand the HTTP/3 memory wrapping pattern and why it's important.

**Token Estimate:** ~20k tokens
**Predecessors:** TASK-025-009
**Successors:** none
**Parallel:** no

**Acceptance Criteria:**
- [ ] Update `CLAUDE.md` → Architecture section → "Data Item Protocol" subsection:
  - Document `Http3DataItem` structure
  - Explain why wrapping is important (type safety, memory safety, ownership)
  - Show a code example: request → encoder → Http3DataItem → stage → writer
- [ ] Create `src/TurboHttp/HTTP3_MEMORY_WRAPPING_SUMMARY.md`:
  - Executive summary of changes
  - Before/after diagrams
  - Link to design documents and lint rules
  - Checklist for extending to HTTP/1.1, HTTP/2 in future
- [ ] Code review comments added to:
  - `Http3DataItem.cs` — explain ownership semantics
  - `Http30EncoderStage.cs` — explain wrapping strategy
  - `Http30DecoderStage.cs` — explain unwrapping strategy
- [ ] CLAUDE.md updated to reflect new "HTTP/3 memory wrapping rules" section

## Task Dependency Graph

```
TASK-025-001 (Analyze DataItem suitability)
    ├─→ TASK-025-003 (Integration strategy) ──→ TASK-025-004 (Encoder wrapping)
    │                                           ├─→ TASK-025-006 (Frame audit)
    │                                           │       ├─→ TASK-025-007 (Lint rule)
    │                                           │           ├─→ TASK-025-008 (Consistency audit)
    │                                           │           └─→ TASK-025-009 (Integration tests)
    │                                           │                   └─→ TASK-025-010 (Documentation)
    │                                           │
    └─→ TASK-025-005 (Decoder wrapping) ───────┘

TASK-025-002 (Audit Http30*) ──→ TASK-025-007 (used during lint rule)
```

Simplified execution order:
1. **TASK-025-001** + **TASK-025-002** run in parallel (metadata strategy + naked reference audit)
2. **TASK-025-003** depends on 001 (integration strategy)
3. **TASK-025-004** + **TASK-025-005** depend on 001 & 003 (stage wrapping with DataItem)
4. **TASK-025-006** depends on 004 & 005 (frame-level boundary verification)
5. **TASK-025-007** depends on 002 & 006 (static validation rule)
6. **TASK-025-008** depends on 007 (consistency across protocols)
7. **TASK-025-009** depends on 006 (integration tests)
8. **TASK-025-010** depends on 009 (documentation)

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-025-001 | ~25k | none | yes (with 002) | opus |
| TASK-025-002 | ~25k | none | yes (with 001) | — |
| TASK-025-003 | ~25k | 001 | no | opus |
| TASK-025-004 | ~40k | 001, 003 | no | — |
| TASK-025-005 | ~40k | 001, 003 | no | — |
| TASK-025-006 | ~25k | 004, 005 | no | — |
| TASK-025-007 | ~35k | 002, 006 | no | opus |
| TASK-025-008 | ~20k | 007 | no | — |
| TASK-025-009 | ~25k | 006 | no | — |
| TASK-025-010 | ~20k | 009 | no | — |

**Total estimated tokens:** ~280k

## Functional Requirements

- FR-1: All frame data emitted from `Http30EncoderStage` must be wrapped in `DataItem` (zero naked `ReadonlyMemory<byte>` outlets)
- FR-2: All frame data consumed by `Http30DecoderStage` must expect `DataItem` input (with proper IMemoryOwner lifecycle management)
- FR-3: Frame-level APIs (`Http3FrameEncoder`, `Http3FrameDecoder`) may use naked `ReadonlyMemory<byte>` internally (documented boundary)
- FR-4: Stage-level outlet writes must never emit bare `ReadonlyMemory<byte>` (wrapped in `DataItem` or metadata item)
- FR-5: Static analyzer/lint rule must prevent regression of naked `ReadonlyMemory<byte>` in stage outlets
- FR-6: All existing HTTP/3 tests must pass without modification (wrapping is internal)
- FR-7: DataItem usage pattern must be consistent with HTTP/1.1 and HTTP/2 implementations
- FR-8: Performance overhead of wrapping must be ≤2% on frame throughput benchmarks

## Non-Goals

- Refactoring HTTP/1.0, HTTP/1.1, or HTTP/2 encoder/decoder implementations in this feature (audit only in TASK-025-008)
- Creating new item types (reuse existing `DataItem`, optionally extend `Http3TaggedItem` if metadata needed)
- Rewriting frame parsing logic (wrapping is a layer on top, not a rewrite)
- Performance optimization beyond baseline (just ensure wrapping isn't a bottleneck)
- Changing the `OutboundWriter` / `InboundReader` channel architecture (wrapping works within channels)

## Technical Considerations

- **IMemoryOwner Lifecycle:** `DataItem.Memory` is an `IMemoryOwner<byte>`; the stage must ensure ownership is transferred to downstream consumers (who dispose it)
- **MemoryPool Allocation:** Buffers should be allocated via `MemoryPool<byte>.Shared.Rent(size)` to avoid GC pressure
- **Metadata Strategy:** Decide if frame-type/stream-ID metadata flows with `DataItem` or in separate wrapper items (see TASK-025-001 decision)
- **Boundary Clarity:** Frame-level APIs (`Http3FrameEncoder`, `Http3FrameDecoder`) work with raw `ReadonlyMemory<byte>` internally; wrapping happens at stage layer only
- **Backward Compatibility:** Stages that currently emit/consume `ReadonlyMemory<byte>` must be updated to use `DataItem`; no API changes to public consumers
- **Consistency with HTTP/1.1, HTTP/2:** Audit whether those protocols also use `DataItem` consistently (TASK-025-008)

## Success Metrics

- Zero naked `ReadonlyMemory<byte>` in HTTP/3 stage outlets (enforced by lint rule)
- 100% pass rate on all HTTP/3 stage and integration tests
- Lint rule successfully prevents regressions (manual test: introduce violation, rule catches it)
- DataItem usage is consistent across HTTP/1.0, 1.1, 2.0, 3.0 stages (verified in TASK-025-008)
- Documentation clearly explains DataItem lifecycle and metadata strategy
- Performance: wrapping overhead is <2% on frame throughput vs. baseline

## Open Questions

**None — all clarifications resolved!** 🎯

---

## Execution Notes

- This is a **systematic adoption** of the existing `DataItem` pattern in HTTP/3 — no new types invented, reusing infrastructure already proven in HTTP/1.1 and HTTP/2
- **TASK-025-001** is critical: the metadata strategy decision (frame-type/stream-ID in DataItem vs. wrapper) affects downstream tasks
- **TASK-025-008** validates consistency — identifies any gaps in HTTP/1.1, HTTP/2 and flags for future work
- **Lint rule (TASK-025-007)** is essential for long-term maintainability; without it, future changes may reintroduce naked buffers
- **Parallelism opportunity:** TASK-025-001 and TASK-025-002 can run in parallel; TASK-025-004 and TASK-025-005 can run in parallel
