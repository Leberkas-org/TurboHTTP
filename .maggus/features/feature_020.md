# Feature 020: Consolidate Content-Encoding — Single Source of Truth

## Introduction

Content-Encoding decompression (gzip, deflate, brotli) currently happens redundantly in **two layers**:

1. **Protocol decoders** (innermost): `Http10Decoder`, `Http11Decoder`, `Http20StreamStage`, `Http30StreamStage` — all call `ContentEncodingDecoder.Decompress()` and strip the `Content-Encoding` header
2. **DecompressionBidiStage** (feature layer): checks `Content-Encoding` header, decompresses, strips header

Because decoders decompress first AND remove the header, `DecompressionBidiStage` is always a **no-op**. The `AutomaticDecompression` flag is effectively broken — setting it to `false` still decompresses (test `EBFC-012` documents this).

Additionally, `RequestCompressionBidiStage` and `DecompressionBidiStage` are complementary halves of the same concern (Content-Encoding): one compresses requests, the other decompresses responses. Each is a pass-through in the other direction. They should be merged into a single `ContentEncodingBidiStage`.

### Architecture Context

- **Components touched:** Protocol layer (4 decoders), Streams/Features layer (2 BidiStages → 1), Engine pipeline composition
- **Pattern:** Feature BidiStages handle cross-cutting concerns; protocol decoders do pure parsing. This change aligns decompression with that pattern.
- **New:** `ContentEncodingBidiStage` replaces both `DecompressionBidiStage` and `RequestCompressionBidiStage`

## Goals

- Remove all `ContentEncodingDecoder.Decompress()` calls from protocol decoders — decoders become pure parsers
- Merge `DecompressionBidiStage` + `RequestCompressionBidiStage` into a single `ContentEncodingBidiStage`
- Make `AutomaticDecompression=false` actually work (raw compressed bytes returned to caller)
- Deduplicate `ReadContentAsMemory` helper (naturally resolved by merge)
- Keep all existing test coverage, adjusted for new behavior

## Tasks

### TASK-020-001: Remove Decompression from Http10Decoder and Http11Decoder
**Description:** As a developer, I want the HTTP/1.0 and HTTP/1.1 decoders to stop decompressing response bodies, so that decompression is handled exclusively by the feature BidiStage.

**Token Estimate:** ~35k tokens
**Predecessors:** none
**Successors:** TASK-020-004, TASK-020-005
**Parallel:** yes — can run alongside TASK-020-002, TASK-020-003

**Acceptance Criteria:**
- [x] `Http10Decoder.cs`: `ContentEncodingDecoder.Decompress()` call removed (~line 384); `decompressed` flag and conditional header-stripping removed; all headers including `Content-Encoding` preserved on response; `Content-Length` reflects raw (compressed) body size
- [x] `Http11Decoder.cs`: `ContentEncodingDecoder.Decompress()` call removed (~line 686); conditional `Content-Encoding`/`Content-Length` stripping removed (lines 700-726); all headers preserved
- [x] Decoder unit tests in `RFC1945/` and `RFC9112/` that test gzip responses updated: assert `Content-Encoding` header is preserved and body contains raw compressed bytes
- [x] `dotnet build --configuration Release ./src/TurboHttp.sln` — zero errors
- [x] `dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC1945"` — passes
- [x] `dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9112"` — passes

**Files to modify:**
- `src/TurboHttp/Protocol/RFC1945/Http10Decoder.cs`
- `src/TurboHttp/Protocol/RFC9112/Http11Decoder.cs`
- `src/TurboHttp.Tests/RFC1945/` (tests with gzip assertions)
- `src/TurboHttp.Tests/RFC9112/` (tests with gzip assertions)

### TASK-020-002: Remove Decompression from Http20StreamStage
**Description:** As a developer, I want the HTTP/2 stream assembly stage to stop decompressing response bodies, so that `Content-Encoding` is preserved for the feature layer.

**Token Estimate:** ~30k tokens
**Predecessors:** none
**Successors:** TASK-020-004, TASK-020-005
**Parallel:** yes — can run alongside TASK-020-001, TASK-020-003

**Acceptance Criteria:**
- [x] `Http20StreamStage.cs`: `ContentEncodingDecoder.Decompress()` call removed (~line 220)
- [x] `ApplyContentHeaders()`: `wasDecompressed` flag removed; all content headers applied unconditionally (including `Content-Encoding` and `Content-Length`)
- [x] `state.ContentEncoding` field can be removed if only used for decompression decisions (verify no other usages first)
- [x] Stream tests in `RFC9113/` adjusted if any assert decompressed output from Http20StreamStage
- [x] `dotnet test ./src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9113"` — passes

**Files to modify:**
- `src/TurboHttp/Streams/Stages/Decoding/Http20StreamStage.cs`
- `src/TurboHttp.StreamTests/RFC9113/` (if tests assert decompressed output)

### TASK-020-003: Remove Decompression from Http30StreamStage
**Description:** As a developer, I want the HTTP/3 stream assembly stage to stop decompressing response bodies, matching the pattern applied to HTTP/2.

**Token Estimate:** ~20k tokens
**Predecessors:** none
**Successors:** TASK-020-004, TASK-020-005
**Parallel:** yes — can run alongside TASK-020-001, TASK-020-002

**Acceptance Criteria:**
- [ ] `Http30StreamStage.cs`: `ContentEncodingDecoder.Decompress()` call removed (~line 150)
- [ ] Content headers applied unconditionally (same pattern as TASK-020-002)
- [ ] Stream tests in `RFC9114/` adjusted if any assert decompressed output from Http30StreamStage
- [ ] `dotnet test ./src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj --filter "FullyQualifiedName~RFC9114"` — passes

**Files to modify:**
- `src/TurboHttp/Streams/Stages/Decoding/Http30StreamStage.cs`
- `src/TurboHttp.StreamTests/RFC9114/` (if tests assert decompressed output)

### TASK-020-004: Merge DecompressionBidiStage + RequestCompressionBidiStage into ContentEncodingBidiStage
**Description:** As a developer, I want a single `ContentEncodingBidiStage` that handles both request compression (outbound) and response decompression (inbound), replacing the two separate stages.

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-020-001, TASK-020-002, TASK-020-003
**Successors:** TASK-020-005
**Parallel:** no — must run after decoders are cleaned up so tests pass end-to-end
**Model:** opus

**Acceptance Criteria:**
- [ ] New file `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs` created
- [ ] Request path (In1→Out1): compresses request body per `RequestCompressionPolicy` (same logic as `RequestCompressionBidiStage.CompressIfNeeded`); pass-through when policy is null
- [ ] Response path (In2→Out2): decompresses response body per `Content-Encoding` header (same logic as `DecompressionBidiStage.Decompress`); pass-through when `automaticDecompression=false`
- [ ] Constructor: `ContentEncodingBidiStage(bool automaticDecompression = true, RequestCompressionPolicy? compressionPolicy = null)`
- [ ] `ReadContentAsMemory` helper exists once (no duplication)
- [ ] Port names follow convention: `ContentEncoding.In.Request`, `ContentEncoding.Out.Request`, `ContentEncoding.In.Response`, `ContentEncoding.Out.Response`
- [ ] All `onUpstreamFailure` handlers absorb failures with `Log.Warning` (same pattern as existing stages)
- [ ] `Engine.cs`: the two conditional blocks for `RequestCompressionPolicy` (line 103-107) and `AutomaticDecompression` (line 109-113) replaced with a single conditional block adding `ContentEncodingBidiStage`
- [ ] `ContentEncodingBidiStage` is added when `AutomaticDecompression=true` OR `RequestCompressionPolicy is not null`
- [ ] Old files `DecompressionBidiStage.cs` and `RequestCompressionBidiStage.cs` deleted
- [ ] All references updated (`using` statements, test files, etc.)
- [ ] Build succeeds with zero errors

**Files to modify:**
- `src/TurboHttp/Streams/Stages/Features/ContentEncodingBidiStage.cs` (new)
- `src/TurboHttp/Streams/Stages/Features/DecompressionBidiStage.cs` (delete)
- `src/TurboHttp/Streams/Stages/Features/RequestCompressionBidiStage.cs` (delete)
- `src/TurboHttp/Streams/Engine.cs`

### TASK-020-005: Update Tests and Verify End-to-End
**Description:** As a developer, I want all existing test coverage to pass with the new consolidated architecture, and the `AutomaticDecompression` flag to be verified working.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-020-004
**Successors:** none
**Parallel:** no — final verification

**Acceptance Criteria:**
- [ ] Test `EBFC-012` in `16_EngineBidiFlowCompositionTests.cs` updated: with `AutomaticDecompression=false`, assert response contains raw compressed bytes + `Content-Encoding` header preserved
- [ ] Test `EBFC-011` still passes: with `AutomaticDecompression=true`, gzip response is decompressed
- [ ] Stream tests in `RFC9110/` that reference `DecompressionBidiStage` or `RequestCompressionBidiStage` updated to reference `ContentEncodingBidiStage`
- [ ] `ContentEncodingDecoder.Decompress` only called in `ContentEncodingBidiStage.cs` and its direct unit tests — grep verification
- [ ] `dotnet test ./src/TurboHttp.sln` — full solution: 0 failures
- [ ] `dotnet build --configuration Release ./src/TurboHttp.sln` — 0 errors

**Files to modify:**
- `src/TurboHttp.StreamTests/Streams/16_EngineBidiFlowCompositionTests.cs`
- `src/TurboHttp.StreamTests/RFC9110/` (decompression stage tests)
- Any other files referencing old stage names

## Task Dependency Graph

```
TASK-020-001 (Http10/Http11 Decoders) ─────┐
TASK-020-002 (Http20StreamStage) ───────────┼──→ TASK-020-004 (Merge Stages) ──→ TASK-020-005 (Verify)
TASK-020-003 (Http30StreamStage) ───────────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-020-001 | ~35k | none | yes (with 002, 003) | — |
| TASK-020-002 | ~30k | none | yes (with 001, 003) | — |
| TASK-020-003 | ~20k | none | yes (with 001, 002) | — |
| TASK-020-004 | ~50k | 001, 002, 003 | no | opus |
| TASK-020-005 | ~40k | 004 | no | — |

**Total estimated tokens:** ~175k

## Functional Requirements

- FR-1: Protocol decoders (Http10Decoder, Http11Decoder, Http20StreamStage, Http30StreamStage) must not call `ContentEncodingDecoder.Decompress()` — they produce raw bytes with all headers intact
- FR-2: `ContentEncodingBidiStage` handles both request compression and response decompression in a single stage
- FR-3: When `AutomaticDecompression=true` (default), gzip/deflate/brotli responses are decompressed and `Content-Encoding` header is removed
- FR-4: When `AutomaticDecompression=false`, responses pass through with raw compressed bytes and `Content-Encoding` header preserved
- FR-5: When `RequestCompressionPolicy` is set, request bodies above the threshold are compressed with `Content-Encoding` header added
- FR-6: When neither decompression nor compression is needed, the stage is not added to the pipeline

## Non-Goals

- No new compression algorithms (only gzip, deflate, brotli as before)
- No streaming decompression (current approach buffers full body — unchanged)
- No changes to `ContentEncodingDecoder` or `ContentEncodingEncoder` utility classes themselves
- No changes to `PipelineDescriptor` record shape (both `AutomaticDecompression` and `RequestCompressionPolicy` fields remain)

## Technical Considerations

- **Pipeline position:** `ContentEncodingBidiStage` sits at the same position as the current innermost pair (closest to engine). Request compression happens last before engine, response decompression happens first after engine — this is correct.
- **Conditional addition in Engine.cs:** Add the stage when `descriptor.AutomaticDecompression || descriptor.RequestCompressionPolicy is not null`. Pass both config values to the constructor.
- **Port naming:** `ContentEncoding.In.Request` / `ContentEncoding.Out.Request` / `ContentEncoding.In.Response` / `ContentEncoding.Out.Response` — must be globally unique (verify with stage-port-validator agent).
- **ReadContentAsMemory:** The identical helper method currently duplicated in both old stages is naturally deduplicated by the merge — it becomes a single `private static` method in `ContentEncodingBidiStage`.

## Success Metrics

- `AutomaticDecompression=false` actually prevents decompression (test EBFC-012 proves it)
- Zero `ContentEncodingDecoder.Decompress()` calls in protocol decoders or stream stages
- Two stage files replaced by one — net reduction in code
- All existing tests pass

## Open Questions

*None — all decisions made.*
