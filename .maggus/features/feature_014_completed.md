# Feature 014: Fuzzing — HTTP/1.x Decoder

## Introduction

Add property-based fuzzing tests for the HTTP/1.0 and HTTP/1.1 response decoders. Uses randomized inputs to find crashes, infinite loops, and uncontrolled memory allocation that deterministic tests might miss.

### Architecture Context

- **Components involved:** Http10Decoder (Protocol/RFC1945), Http11Decoder (Protocol/RFC9112)
- **Existing precedent:** `RFC9113/21_FuzzHarnessTests.cs` uses seeded `Random` for HTTP/2 frame fuzzing — follow the same pattern
- **No external fuzzing framework** — custom xUnit-based property tests with deterministic seeds for reproducibility

## Mandatory Testing Policy

> **Tests are required and must never be skipped, disabled, or marked as `[Skip]`.** Every acceptance criterion that specifies "All tests pass" is a hard gate — the feature is not complete until every test passes. No `[Fact(Skip = "...")]`, no `#if` guards, no `Assert.Skip()`. If a test fails, fix the code or the test — never suppress it.

## Goals

- Verify decoders never crash on arbitrary input
- Verify decoders never enter infinite loops (bounded execution time)
- Verify decoders never allocate unbounded memory on adversarial input
- Use deterministic seeds for reproducible test failures

## Tasks

### TASK-014-001: HTTP/1.0 Decoder Fuzzing
**Description:** As a security engineer, I want to fuzz the HTTP/1.0 response decoder with random byte sequences to find edge cases.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-014-002

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/Security/Http10FuzzTests.cs`
- [x] Tests with `[Theory]` + `[InlineData(seed)]` for multiple fixed seeds (42, 137, 7, 99, 12345, 65536)
- [x] Fuzz categories:
  - Pure random bytes (1–8KB) → TryDecode must not crash, returns false or valid result
  - Partial valid responses (valid status line + random body) → graceful handling
  - Truncated responses at every byte offset → no crash
  - Oversized header values (>64KB of random characters) → bounded handling
  - Valid response followed by garbage → decoder handles remainder correctly
  - Repeated calls with incremental random chunks → state machine stays consistent
- [x] Each test iteration has a 5-second timeout (`CancellationTokenSource`)
- [x] Memory assertion: decoder allocations stay below 1MB per iteration
- [x] Minimum 100 iterations per seed per category
- [x] All tests pass

### TASK-014-002: HTTP/1.1 Decoder Fuzzing
**Description:** As a security engineer, I want to fuzz the HTTP/1.1 response decoder including chunked transfer encoding.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** none
**Parallel:** yes — can run alongside TASK-014-001
**Model:** opus

**Acceptance Criteria:**
- [x] Test file `src/TurboHttp.Tests/Security/Http11FuzzTests.cs`
- [x] Same seed strategy as TASK-014-001
- [x] Additional fuzz categories for HTTP/1.1:
  - Pure random bytes → TryDecode must not crash
  - Random chunked encoding (valid chunk-size lines + random data) → graceful handling
  - Chunk extensions with random bytes → no crash
  - Trailer headers with random content → no crash
  - Content-Length mismatch (claimed vs actual) → decoder detects
  - Mixed Transfer-Encoding and Content-Length → detected as error
  - Extremely large Content-Length (>2GB) → bounded handling, no OOM
  - Valid HTTP/1.1 response with `Connection: close` + random trailing data
  - Fragmented delivery: valid response split at random byte offsets across multiple TryDecode calls
- [x] Each test iteration has a 5-second timeout
- [x] Memory assertion: allocations stay below 1MB per iteration
- [x] Minimum 100 iterations per seed per category
- [x] All tests pass

## Task Dependency Graph

```
TASK-014-001 (standalone)
TASK-014-002 (standalone)
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-014-001 | ~40k | none | yes (with 002) | — |
| TASK-014-002 | ~50k | none | yes (with 001) | opus |

**Total estimated tokens:** ~90k

## Functional Requirements

- FR-1: No decoder method may throw `NullReferenceException`, `IndexOutOfRangeException`, or `AccessViolationException` on any input
- FR-2: No decoder method may loop indefinitely — all calls must complete within 5 seconds
- FR-3: No decoder method may allocate more than 1MB for a single input buffer of ≤8KB
- FR-4: Decoder `Reset()` must return decoder to clean state regardless of previous input
- FR-5: All fuzz tests must be deterministically reproducible via seed

## Non-Goals

- No coverage-guided fuzzing (libFuzzer, AFL) — that's a separate infrastructure project
- No fuzzing of encoders (encoders take structured input, not raw bytes)
- No network-level fuzzing (TCP stream corruption)

## Technical Considerations

- Follow the pattern from `RFC9113/21_FuzzHarnessTests.cs` — `new Random(seed)` with fixed seeds
- Use `[Theory]` + `[InlineData]` for seeds, not random seeds per run (reproducibility)
- Memory measurement: use `GC.GetAllocatedBytesForCurrentThread()` before/after
- Timeout: wrap each iteration in `Task.Run` with `CancellationToken` for hard timeout
- Decoders maintain `_remainder` state — fuzz sequences must test multi-call patterns

## Success Metrics

- 1000+ fuzz iterations per decoder with 0 crashes
- All tests complete within the CI timeout (no infinite loops)
- Memory stays bounded on adversarial input
