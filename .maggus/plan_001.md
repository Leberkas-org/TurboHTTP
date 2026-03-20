# Plan: Update Outdated Comments — RFC-Compliance Audit

## Introduction

Systematic review and update of all comments in production code (`src/TurboHttp/`). Focus areas:
- **Outdated RFC references** — update to current specifications (RFC 7540 → RFC 9113, RFC 7230/7231 → RFC 9110/9112)
- **Comments that no longer match the code** — correct or remove
- **Completed TODO/FIXME/HACK comments** — remove
- **Missing RFC references** — add where appropriate
- **Redundant comments** — remove (don't comment the obvious)

Ordered by RFC: RFC 7541 → RFC 9110 → RFC 9111 → RFC 9112 → RFC 9113 → Rest.
Max 5 files per task.

## Goals

- Update all outdated RFC numbers to current specifications
- Validate every RFC section reference (§) for correctness (old §-numbers often don't map to new RFCs)
- Correct or remove comments that inaccurately describe the code
- Remove redundant comments (e.g. `// increment counter` above `counter++`)
- Clean up completed TODOs
- Ensure consistent comment style across the entire codebase

## RFC Mapping (old → new)

The following RFC supersession rules apply:

| Old RFC | New RFC | Topic |
|---------|---------|-------|
| RFC 7540 | RFC 9113 | HTTP/2 |
| RFC 7230 | RFC 9110 §7 + RFC 9112 | HTTP/1.1 Message Syntax |
| RFC 7231 | RFC 9110 | HTTP Semantics |
| RFC 7232 | RFC 9110 §13 | Conditional Requests |
| RFC 7233 | RFC 9110 §14 | Range Requests |
| RFC 7234 | RFC 9111 | Caching |
| RFC 7235 | RFC 9110 §11 | Authentication |
| RFC 7541 | RFC 7541 | HPACK (unchanged, still current) |

**Important**: RFC 7541 (HPACK) has NOT been superseded — references to it are correct and must remain. Only references to RFC 7540 §6.5.2 (SETTINGS) need updating to RFC 9113 §6.5.2, since SETTINGS is an HTTP/2 concept.

## User Stories

---

### TASK-001: RFC 7541 — HPACK Encoder & Decoder (Protocol)
**Description:** As a developer, I want the HPACK implementation to have correct RFC references so that specification conformance is traceable.

**Files (2):**
1. `src/TurboHttp/Protocol/RFC7541/HpackEncoder.cs`
2. `src/TurboHttp/Protocol/RFC7541/HpackDecoder.cs`

**Acceptance Criteria:**
- [x] Verify all RFC 7541 §-references — HPACK sections are correct (RFC 7541 remains current)
- [x] Update references to RFC 7540 (e.g. §6.5.2 MAX_HEADER_LIST_SIZE) to RFC 9113 §6.5.2
- [x] Verify comments accurately describe the current code state
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-002: RFC 7541 — HuffmanCodec & Protocol Utilities
**Description:** As a developer, I want the general protocol utilities and Huffman codec reviewed for outdated comments.

**Files (5):**
1. `src/TurboHttp/Protocol/HuffmanCodec.cs`
2. `src/TurboHttp/Protocol/HttpDecoderError.cs`
3. `src/TurboHttp/Protocol/HttpDecodeResult.cs`
4. `src/TurboHttp/Protocol/HttpDecoderException.cs`
5. `src/TurboHttp/Protocol/HttpSizePredictor.cs`

**Acceptance Criteria:**
- [x] Verify RFC 7541 Huffman references (Appendix B)
- [x] Review error enums and exception comments for accuracy
- [x] Remove outdated or redundant comments
- [x] `dotnet build` succeeds

---

### TASK-003: RFC 7541 — WellKnownHeaders
**Description:** As a developer, I want the static header table and WellKnownHeaders reviewed for correct RFC references.

**Files (1):**
1. `src/TurboHttp/Protocol/WellKnownHeaders.cs`

**Acceptance Criteria:**
- [x] Verify RFC 7541 Appendix A (Static Table) references
- [x] Validate header definitions against current RFCs
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-004: RFC 9110 — Redirect Handling (Protocol)
**Description:** As a developer, I want the redirect logic to have correct RFC 9110 §15.4 references.

**Files (4):**
1. `src/TurboHttp/Protocol/RFC9110/RedirectHandler.cs`
2. `src/TurboHttp/Protocol/RFC9110/RedirectPolicy.cs`
3. `src/TurboHttp/Protocol/RFC9110/RedirectException.cs`
4. `src/TurboHttp/Protocol/RFC9110/ContentEncodingDecoder.cs`

**Acceptance Criteria:**
- [x] RFC 9110 §15.4 (Redirects) references are correct
- [x] RFC 9110 §8.4 (Content-Encoding) references in ContentEncodingDecoder verified
- [x] No references to RFC 7231 (old Redirect RFC) remain
- [x] Comments accurately describe current redirect behaviour (301/302/303/307/308)
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-005: RFC 9110 — Retry Handling (Protocol)
**Description:** As a developer, I want the retry logic to have correct RFC 9110 §9.2 references.

**Files (3):**
1. `src/TurboHttp/Protocol/RFC9110/RetryEvaluator.cs`
2. `src/TurboHttp/Protocol/RFC9110/RetryDecision.cs`
3. `src/TurboHttp/Protocol/RFC9110/RetryPolicy.cs`

**Acceptance Criteria:**
- [x] RFC 9110 §9.2 (Idempotent Methods) references are correct
- [x] RFC 9110 §10.2.3 (Retry-After) references verified
- [x] No references to RFC 7231 remain
- [x] Comments accurately describe current retry behaviour
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-006: RFC 9110 — Stream Stages (Redirect, Retry, Decompression)
**Description:** As a developer, I want the RFC 9110 Akka stages to have correct comments.

**Files (3):**
1. `src/TurboHttp/Streams/Stages/RedirectStage.cs`
2. `src/TurboHttp/Streams/Stages/RetryStage.cs`
3. `src/TurboHttp/Streams/Stages/DecompressionStage.cs`

**Acceptance Criteria:**
- [x] RFC 9110 references in stage comments are correct
- [x] Stage descriptions match current behaviour
- [x] No outdated RFC 7231/7230 references
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-007: RFC 9111 — Cache Protocol (Part 1: Parser & Types)
**Description:** As a developer, I want the caching types and CacheControl parser reviewed for RFC 9111 conformance.

**Files (5):**
1. `src/TurboHttp/Protocol/RFC9111/CacheControl.cs`
2. `src/TurboHttp/Protocol/RFC9111/CacheControlParser.cs`
3. `src/TurboHttp/Protocol/RFC9111/CacheEntry.cs`
4. `src/TurboHttp/Protocol/RFC9111/CacheLookupResult.cs`
5. `src/TurboHttp/Protocol/RFC9111/CachePolicy.cs`

**Acceptance Criteria:**
- [x] RFC 9111 §5.2 (Cache-Control) references are correct
- [x] No references to RFC 7234 (old Caching RFC) remain
- [x] Comments accurately describe current cache behaviour
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-008: RFC 9111 — Cache Protocol (Part 2: Freshness, Validation, Store)
**Description:** As a developer, I want the cache freshness, validation and store logic reviewed for RFC 9111 conformance.

**Files (3):**
1. `src/TurboHttp/Protocol/RFC9111/CacheFreshnessEvaluator.cs`
2. `src/TurboHttp/Protocol/RFC9111/CacheValidationRequestBuilder.cs`
3. `src/TurboHttp/Protocol/RFC9111/HttpCacheStore.cs`

**Acceptance Criteria:**
- [x] RFC 9111 §4.2 (Freshness) references in CacheFreshnessEvaluator are correct
- [x] RFC 9111 §4.3 (Validation) references in CacheValidationRequestBuilder are correct
- [x] RFC 9111 §3 (Storing) references in HttpCacheStore are correct
- [x] No references to RFC 7234 remain
- [x] Comments accurately describe current behaviour
- [x] `dotnet build` succeeds

---

### TASK-009: RFC 9111 — Cache Stages
**Description:** As a developer, I want the cache Akka stages to have correct comments.

**Files (2):**
1. `src/TurboHttp/Streams/Stages/CacheLookupStage.cs`
2. `src/TurboHttp/Streams/Stages/CacheStorageStage.cs`

**Acceptance Criteria:**
- [x] RFC 9111 references in stage comments are correct
- [x] Stage descriptions match current behaviour
- [x] No outdated RFC references
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-010: RFC 9112 — HTTP/1.1 Encoder & Decoder (Protocol)
**Description:** As a developer, I want the HTTP/1.1 encoder and decoder to have correct RFC 9112 references, updating all old RFC 7230 references.

**Files (2):**
1. `src/TurboHttp/Protocol/RFC9112/Http11Encoder.cs`
2. `src/TurboHttp/Protocol/RFC9112/Http11Decoder.cs`

**Acceptance Criteria:**
- [x] **RFC 7230 → RFC 9112 mapping**: Update all outdated RFC 7230 references
  - RFC 7230 §3.2 (Header Fields) → RFC 9112 §5 (Field Syntax)
  - RFC 7230 §3.3.2 (Content-Length) → RFC 9112 §6.1 (Transfer-Encoding)
  - RFC 7230 §4.1 (Chunked Transfer) → RFC 9112 §7.1 (Chunked Transfer Coding)
- [x] **RFC 7233 → RFC 9110 mapping**: RFC 7233 §2.1 (byte-range-spec) → RFC 9110 §14.1.1
- [x] Validate all §-numbers against the RFC 9112 table of contents
- [x] Comments accurately describe current encoder/decoder behaviour
- [x] Remove redundant comments
- [x] `dotnet build` succeeds

---

### TASK-011: RFC 9112 — Connection Management (Protocol)
**Description:** As a developer, I want the connection management logic to have correct RFC 9112 §9 references.

**Files (4):**
1. `src/TurboHttp/Protocol/RFC9112/ConnectionReuseEvaluator.cs`
2. `src/TurboHttp/Protocol/RFC9112/ConnectionReuseDecision.cs`
3. `src/TurboHttp/Protocol/RFC9112/ConnectionPolicy.cs`
4. `src/TurboHttp/Protocol/RFC9112/PerHostConnectionLimiter.cs`

**Acceptance Criteria:**
- [ ] RFC 9112 §9 (Connection Management) references are correct
- [ ] No references to RFC 7230 §6 (old Connection Management section) remain
- [ ] Keep-alive/close logic is correctly documented
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-012: RFC 9112 — HTTP/1.x Stages
**Description:** As a developer, I want the HTTP/1.x Akka stages to have correct RFC references.

**Files (5):**
1. `src/TurboHttp/Streams/Stages/Http10EncoderStage.cs`
2. `src/TurboHttp/Streams/Stages/Http10DecoderStage.cs`
3. `src/TurboHttp/Streams/Stages/Http11EncoderStage.cs`
4. `src/TurboHttp/Streams/Stages/Http11DecoderStage.cs`
5. `src/TurboHttp/Streams/Stages/Http1XCorrelationStage.cs`

**Acceptance Criteria:**
- [ ] RFC 1945 references in HTTP/1.0 stages are correct
- [ ] RFC 9112 references in HTTP/1.1 stages are correct
- [ ] No outdated RFC 7230 references in stages
- [ ] Stage descriptions match current behaviour
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-013: RFC 9112 — Connection Reuse Stage & HTTP/1.0 Protocol
**Description:** As a developer, I want the ConnectionReuseStage and HTTP/1.0 protocol files reviewed.

**Files (3):**
1. `src/TurboHttp/Streams/Stages/ConnectionReuseStage.cs`
2. `src/TurboHttp/Protocol/RFC1945/Http10Encoder.cs`
3. `src/TurboHttp/Protocol/RFC1945/Http10Decoder.cs`

**Acceptance Criteria:**
- [ ] RFC 9112 §9 references in ConnectionReuseStage are correct
- [ ] RFC 1945 references in HTTP/1.0 encoder/decoder are correct (RFC 1945 remains current)
- [ ] Comments accurately describe current behaviour
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-014: RFC 9113 — HTTP/2 Frame Types & Exceptions (Protocol)
**Description:** As a developer, I want the HTTP/2 frame definitions and exceptions updated from RFC 7540 to RFC 9113.

**Files (2):**
1. `src/TurboHttp/Protocol/RFC9113/Http2Frame.cs`
2. `src/TurboHttp/Protocol/RFC9113/Http2Exception.cs`

**Acceptance Criteria:**
- [ ] **RFC 7540 → RFC 9113 mapping** for frame types:
  - §4.1 (Frame Format) → RFC 9113 §4.1
  - §5.4 (Error Handling) → RFC 9113 §5.4
  - §6.x (Frame Definitions) → RFC 9113 §6.x (verify §-numbers are identical)
- [ ] Replace all RFC 7540 references with RFC 9113
- [ ] Validate error code comments against RFC 9113 §7
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-015: RFC 9113 — HTTP/2 Encoder & Decoder (Protocol)
**Description:** As a developer, I want the HTTP/2 request encoder and frame decoder updated to RFC 9113.

**Files (2):**
1. `src/TurboHttp/Protocol/RFC9113/Http2RequestEncoder.cs`
2. `src/TurboHttp/Protocol/RFC9113/Http2FrameDecoder.cs`

**Acceptance Criteria:**
- [ ] **RFC 7540 → RFC 9113 mapping** for encoder:
  - §8.1.2.1 (Pseudo-Header Fields) → RFC 9113 §8.3.1
  - §6.9 (WINDOW_UPDATE) → RFC 9113 §6.9
  - §5.2 (Flow Control) → RFC 9113 §5.2
- [ ] **RFC 7540 → RFC 9113 mapping** for decoder:
  - §4.2 (Frame Size) → RFC 9113 §4.2
  - §6.1-6.10 (Frame Types) → RFC 9113 §6.1-6.10
  - §6.5 (SETTINGS) → RFC 9113 §6.5
- [ ] Replace all RFC 7540 references with RFC 9113
- [ ] Validate §-numbers against RFC 9113 table of contents (some sections have shifted!)
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-016: RFC 9113 — HTTP/2 Stages (Part 1: Encoder, Decoder, Connection)
**Description:** As a developer, I want the HTTP/2 Akka stages to have correct RFC 9113 references.

**Files (3):**
1. `src/TurboHttp/Streams/Stages/Http20EncoderStage.cs`
2. `src/TurboHttp/Streams/Stages/Http20DecoderStage.cs`
3. `src/TurboHttp/Streams/Stages/Http20ConnectionStage.cs`

**Acceptance Criteria:**
- [ ] Replace all RFC 7540 references with RFC 9113
- [ ] SETTINGS/PING/GOAWAY comments verified against RFC 9113 §6.5/§6.7/§6.8
- [ ] Flow control comments verified against RFC 9113 §5.2
- [ ] Stage descriptions match current behaviour
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-017: RFC 9113 — HTTP/2 Stages (Part 2: Stream, Correlation, Preface, StreamId, Request2Frame)
**Description:** As a developer, I want the remaining HTTP/2 stages to have correct RFC references.

**Files (5):**
1. `src/TurboHttp/Streams/Stages/Http20StreamStage.cs`
2. `src/TurboHttp/Streams/Stages/Http20CorrelationStage.cs`
3. `src/TurboHttp/Streams/Stages/PrependPrefaceStage.cs`
4. `src/TurboHttp/Streams/Stages/StreamIdAllocatorStage.cs`
5. `src/TurboHttp/Streams/Stages/Request2FrameStage.cs`

**Acceptance Criteria:**
- [ ] **PrependPrefaceStage**: RFC 7540 §3.5 → RFC 9113 §3.4 (Connection Preface)
- [ ] **StreamIdAllocatorStage**: RFC 9113 §5.1.1 (Stream Identifiers) references are correct
- [ ] **Http20StreamStage**: HPACK references (RFC 7541) remain, HTTP/2 references → RFC 9113
- [ ] **Request2FrameStage**: Pseudo-header references RFC 9113 §8.3.1 are correct
- [ ] Replace all remaining RFC 7540 references with RFC 9113
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-018: Cookies & Engine — RFC 6265, Engines, Remaining Stages
**Description:** As a developer, I want the cookie implementation and engine files reviewed for correct comments.

**Files (5):**
1. `src/TurboHttp/Protocol/RFC6265/CookieJar.cs`
2. `src/TurboHttp/Protocol/RFC6265/CookieParser.cs`
3. `src/TurboHttp/Streams/Stages/CookieInjectionStage.cs`
4. `src/TurboHttp/Streams/Stages/CookieStorageStage.cs`
5. `src/TurboHttp/Streams/Engine.cs`

**Acceptance Criteria:**
- [ ] RFC 6265 references are correct (RFC 6265 remains current; check for RFC 6265bis if applicable)
- [ ] Cookie logic comments (domain/path matching, Secure/HttpOnly) are up to date
- [ ] Engine comments accurately describe current version-routing logic
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-019: Engines & Protocol Engine Interface
**Description:** As a developer, I want the protocol engine implementations and interface reviewed.

**Files (5):**
1. `src/TurboHttp/Streams/IProtocolEngine.cs`
2. `src/TurboHttp/Streams/Http10Engine.cs`
3. `src/TurboHttp/Streams/Http11Engine.cs`
4. `src/TurboHttp/Streams/Http20Engine.cs`
5. `src/TurboHttp/Streams/Http30Engine.cs`

**Acceptance Criteria:**
- [ ] Engine comments accurately describe current pipeline composition
- [ ] Http30Engine TODO comment reviewed — is the stub still current?
- [ ] No outdated RFC references in engine files
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-020: I/O Layer — Actors & Data Path
**Description:** As a developer, I want the I/O layer (actors, byte mover, state) reviewed for correct comments.

**Files (5):**
1. `src/TurboHttp/IO/ClientByteMover.cs`
2. `src/TurboHttp/IO/ClientRunner.cs`
3. `src/TurboHttp/IO/ClientState.cs`
4. `src/TurboHttp/IO/ClientManager.cs`
5. `src/TurboHttp/IO/Stages/ConnectionStage.cs`

**Acceptance Criteria:**
- [ ] Comments accurately describe the current hybrid architecture (actors + channels)
- [ ] Data path comments (TCP→Pipe→Channel) match the implementation
- [ ] No outdated architecture comments (e.g. if actor hierarchy has changed)
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-021: Lifecycle Layer — Connection Pool Actors
**Description:** As a developer, I want the lifecycle actors reviewed for correct comments.

**Files (5):**
1. `src/TurboHttp/Lifecycle/PoolRouter.cs`
2. `src/TurboHttp/Lifecycle/HostPool.cs`
3. `src/TurboHttp/Lifecycle/ConnectionActor.cs`
4. `src/TurboHttp/Lifecycle/ConnectionHandle.cs`
5. `src/TurboHttp/Lifecycle/ConnectionState.cs`

**Acceptance Criteria:**
- [ ] Actor hierarchy comments match the current design
- [ ] ConnectionHandle description (Writer + Reader bundle) is correct
- [ ] ConnectionState comments (Active, Idle, Reusable) are up to date
- [ ] No outdated architecture comments
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-022: Client Layer & Internal Utilities
**Description:** As a developer, I want the client API and internal utilities reviewed for correct comments.

**Files (5):**
1. `src/TurboHttp/Client/ITurboHttpClient.cs`
2. `src/TurboHttp/Client/TurboHttpClient.cs`
3. `src/TurboHttp/Client/TurboClientOptions.cs`
4. `src/TurboHttp/Client/TurboClientStreamManager.cs`
5. `src/TurboHttp/Internal/Messages.cs`

**Acceptance Criteria:**
- [ ] Public API comments (XML docs) accurately describe the current API
- [ ] Channel-based API model is correctly documented
- [ ] Message type comments (IOutputItem, IInputItem) are up to date
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-023: Client Layer Rest & Internal Stages
**Description:** As a developer, I want the remaining client files and internal stages reviewed.

**Files (5):**
1. `src/TurboHttp/Client/ITurboHttpClientFactory.cs`
2. `src/TurboHttp/Client/TurboHttpClientFactory.cs`
3. `src/TurboHttp/Hosting/TurboClientServiceCollectionExtensions.cs`
4. `src/TurboHttp/Internal/RequestEndpoint.cs`
5. `src/TurboHttp/IO/IClientProvider.cs`

**Acceptance Criteria:**
- [ ] Factory pattern comments are correct
- [ ] DI/hosting comments are up to date
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-024: Internal Stages & Remaining Files
**Description:** As a developer, I want the internal stream stages and remaining files reviewed.

**Files (5):**
1. `src/TurboHttp/Internal/Stages/GroupByHostKeyStage.cs`
2. `src/TurboHttp/Internal/Stages/HostKeyGroupByExtensions.cs`
3. `src/TurboHttp/Internal/Stages/HostKeyMergeBack.cs`
4. `src/TurboHttp/Internal/Stages/MergeSubstreamsStage.cs`
5. `src/TurboHttp/Streams/Stages/TurboAttributes.cs`

**Acceptance Criteria:**
- [ ] Stage comments accurately describe current partitioning/merging behaviour
- [ ] TurboAttributes comments are up to date
- [ ] No outdated architecture comments
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

### TASK-025: Remaining Stages — RequestEnricher, ExtractOptions & TcpOptions
**Description:** As a developer, I want the final remaining stage files reviewed.

**Files (3):**
1. `src/TurboHttp/Streams/Stages/RequestEnricherStage.cs`
2. `src/TurboHttp/Streams/Stages/ExtractOptionsStage.cs`
3. `src/TurboHttp/IO/TcpOptionsFactory.cs`

**Acceptance Criteria:**
- [ ] Stage comments accurately describe current behaviour
- [ ] TCP options comments are up to date
- [ ] No outdated RFC references
- [ ] Remove redundant comments
- [ ] `dotnet build` succeeds

---

## Functional Requirements

- FR-1: Every outdated RFC number must be replaced with the current one (see RFC Mapping above)
- FR-2: Every §-reference must be validated against the respective RFC's table of contents
- FR-3: Comments that inaccurately describe the code must be corrected or removed
- FR-4: Redundant comments (e.g. `// returns the value`) must be removed
- FR-5: Completed TODO/FIXME/HACK comments must be removed
- FR-6: Incomplete TODOs remain (e.g. Http30Engine stub)
- FR-7: Every task must conclude with a successful `dotnet build`
- FR-8: No functional code changes — only comments are modified
- FR-9: Maximum 5 files per task

## Non-Goals

- No changes to production code (logic, signatures, etc.)
- No changes to test files
- No new comments where none exist (only update existing ones)
- No rewriting XML doc comments — only correct if wrong
- No refactoring of file names or directory structures
- No changes to `.csproj` or solution files

## Technical Considerations

- **RFC 9113 vs RFC 7540 section mapping**: Most §-numbers are identical (§4.1, §6.1-6.10), but some have shifted:
  - RFC 7540 §3.5 (Connection Preface) → RFC 9113 §3.4
  - RFC 7540 §8.1.2.1 (Pseudo-Headers) → RFC 9113 §8.3.1
  - RFC 7540 §8.1.2.3 (Request Pseudo-Headers) → RFC 9113 §8.3.1
- **RFC 7230 → RFC 9112 mapping**: Sections have changed completely — every reference needs individual verification
- **Order**: RFC 7541 first, since HPACK is a dependency for HTTP/2. Then RFC 9110/9111/9112 (independent). RFC 9113 last (builds on all others)

## Success Metrics

- Zero outdated RFC references (RFC 7540, 7230, 7231, 7232, 7233, 7234, 7235) in production code
- All §-references point to correct sections in current RFCs
- No comment describes code that behaves differently than stated
- No redundant/obvious comments remain
- `dotnet build` and `dotnet test` continue to succeed after all changes

## Open Questions

- Should RFC 6265 references be updated to RFC 6265bis (draft), or does RFC 6265 remain as the stable reference?
- Should the Http30Engine TODO remain or be updated (e.g. with an RFC 9114 reference)?
