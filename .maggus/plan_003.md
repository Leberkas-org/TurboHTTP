# Plan: TurboHttp Production Code Quality Hardening

## Introduction

Comprehensive code quality hardening of the TurboHttp production code based on a full audit of the Protocol, Streams, IO, and Client layers. The central architectural change is the **"Stream Never Dies"** principle: the Akka.Streams pipeline must stay alive for the entire lifetime of the client — only `client.Dispose()` triggers stream completion. Individual request errors, connection failures, and protocol violations are handled gracefully without killing the stream.

Additionally fixes critical bugs (memory leaks, race conditions, deadlock risks), design issues (blocking I/O), performance bottlenecks (unnecessary allocations, O(n) lookups), and code quality problems (magic numbers, missing validation, dead code).

All work happens on the current branch. Every task includes a reproducing/verifying test.

## Goals

- **"Stream Never Dies"**: The pipeline only completes via `Dispose()`. No stage may call `FailStage()` or `CompleteStage()` except in response to upstream completion triggered by Dispose.
- Eliminate all critical memory leaks and resource management bugs
- Fix race conditions and thread-safety issues in the IO layer
- Remove all blocking `.Result`/`.Wait()` calls and synchronous socket operations
- Improve hot-path performance by eliminating unnecessary allocations
- Standardize error handling, exception types, and validation across all layers

## Core Principle: "Stream Never Dies"

### Why

The `ITurboHttpClient` owns a long-lived Akka.Streams pipeline. If any stage calls `FailStage(ex)` or `CompleteStage()` due to a per-request or per-connection error, the **entire pipeline dies** — all subsequent `SendAsync` calls will fail, the client becomes unusable, and the user must recreate it. This is unacceptable for a production HTTP client that must survive transient errors.

### Rules

1. **`onUpstreamFinish: CompleteStage`** — This is **correct and stays**. Upstream completion originates from `Dispose()` closing the request channel writer → the Source completes → `onUpstreamFinish` propagates through all stages. This is the only valid completion path.

2. **`onUpstreamFailure: FailStage`** — This must be **replaced** in all stages. Upstream failures should be logged and absorbed, not propagated. The stage should continue processing.

3. **`FailStage(ex)` inside `onPush` handlers** — This must be **replaced** in all stages. Per-request errors (encoding failure, decoding failure, protocol error) should:
   - Log the error (via Akka `ILoggingAdapter` from stage materializer)
   - Emit an error response (e.g., synthesized 502/500 `HttpResponseMessage`) back to the caller
   - Or silently drop the element and pull the next one
   - **Never kill the stream**

4. **`CompleteStage()` inside `onPush` handlers** — This must be **removed** in all stages. No stage may self-complete.

5. **`onDownstreamFinish: _ => CompleteStage()`** — This is **correct and stays**. Downstream cancellation from Dispose propagates back correctly.

6. **Connection-level errors** (GOAWAY, flow control violation, TCP disconnect) — Handled by ConnectionStage + actor pool: disconnect, reconnect, re-route. The stream continues.

### Handling Existing Tests That Contradict "Stream Never Dies"

Many existing stream tests assert that a stage **fails** or **completes** on error input — e.g., "given corrupt bytes, verify the stage throws / completes / the materialised task fails". These tests validated the **old** design where errors killed the stream. Under the new design, this behavior is **wrong**.

**Rule: Delete, don't fix.** If an existing test asserts any of the following, it must be **deleted** (not adapted):
- Stage completes after an error (`CompleteStage` expected)
- Stage fails with a specific exception (`FailStage` expected)
- Materialised `Task` faults on error input
- Stream terminates after a single bad request/response

These tests are replaced by the new **"Stream Survives Error"** tests (TASK-010) which verify the **opposite** — that the stream stays alive after errors.

**Do NOT attempt to "fix" these tests to pass with the new behavior.** They test a contract that no longer exists. Trying to adapt them wastes time and creates misleading test names (e.g., `Should_FailStage_When_InvalidInput` rewritten to assert the stage doesn't fail — the test name is now a lie).

**Concrete examples of tests to delete:**
- Any test named `*_Should_Fail_*` or `*_Should_Complete_*` that targets error scenarios in stages
- Tests that assert `ExpectComplete()` after injecting bad data into a stage
- Tests that assert specific exception types from `FailStage` calls
- Tests that use `EventFilter.Exception<T>()` to expect stage failures

**Each Phase 1 TASK (002–009) must include a sub-step:**
1. Search for tests in `TurboHttp.StreamTests` that reference the affected stage
2. Identify tests that assert on `FailStage`/`CompleteStage` behavior
3. **Delete** those tests
4. Document which tests were deleted and why (in the commit message)
5. The new TASK-010 StreamSurvivalTests replace the deleted coverage

### Affected Stages (Full Inventory)

**Stages calling `FailStage` in `onPush` (must convert to log-and-continue):**
- `Http10EncoderStage` (line 59) — encoding error
- `Http11EncoderStage` (line 61) — encoding error
- `Http10DecoderStage` (line 63) — decoding error
- `Http11DecoderStage` (line 65) — decoding error
- `RequestEnricherStage` (line 48) — enrichment error
- `ConnectionStage` (line 190) — missing handle
- `Http20ConnectionStage` (lines 163, 265, 270, 332) — GOAWAY, flow control

**Stages with `onUpstreamFailure: FailStage` (must convert to log-and-absorb):**
- `CacheLookupStage`, `CacheStorageStage`, `CookieInjectionStage`, `CookieStorageStage`
- `ConnectionReuseStage`, `DecompressionStage`, `ExtractOptionsStage`
- `Http10DecoderStage`, `Http10EncoderStage`, `Http11DecoderStage`, `Http11EncoderStage`
- `Http20DecoderStage`, `MergeSubstreamsStage`
- `RedirectStage`, `RetryStage`, `PrependPrefaceStage`, `RequestEnricherStage`

**Stages calling `CompleteStage()` in `onPush` (must remove):**
- `Http10DecoderStage` (lines 71, 75) — self-completes after EOF response
- `Http1XCorrelationStage` (lines 102, 120) — self-completes when queues empty
- `Http20CorrelationStage` (line 110)
- `ConnectionReuseStage` (lines 65, 135)
- `RetryStage` (lines 136, 206)
- `Request2FrameStage` (lines 63, 81)
- `MergeSubstreamsStage` (lines 77, 118)
- `GroupByHostKeyStage` (line 106)

## User Stories

---

### Phase 1: Stream Lifecycle Architecture (Priority: Immediate)

---

### TASK-001: Establish Stage Logging Infrastructure
**Description:** As a developer, I want all stages to have access to a structured logger so that errors can be logged instead of failing the stream.

**Problem:** Most stages have no logging. When we convert `FailStage(ex)` to log-and-continue, we need a consistent logging pattern.

**Required Change:**
1. Add `ILoggingAdapter Log` property to stage `GraphStageLogic` implementations (Akka provides this via `Materializer`)
2. Create a helper extension or base pattern: `Log.Warning("Stage {0}: {1}", stageName, ex.Message)`
3. Ensure log output includes stage name and request context (e.g., URL) where available

**Acceptance Criteria:**
- [x] Logging pattern established and documented
- [x] At least one stage (e.g. `RequestEnricherStage`) migrated as reference implementation
- [x] Log output includes stage name and error details
- [x] Unit test verifies log output on error
- [x] Build compiles with 0 errors

---

### TASK-002: Convert Encoder Stages to Log-and-Continue
**Description:** As a developer, I want encoding errors to not kill the stream so that one malformed request doesn't destroy the client.

**Files:**
- `src/TurboHttp/Streams/Stages/Http10EncoderStage.cs`, line 59: `FailStage(ex)`
- `src/TurboHttp/Streams/Stages/Http11EncoderStage.cs`, line 61: `FailStage(ex)`

**Current Code:**
```csharp
catch (Exception ex)
{
    FailStage(ex);  // ← Kills entire stream
}
```

**Required Change:**
```csharp
catch (Exception ex)
{
    Log.Warning("Http11EncoderStage: Failed to encode request [{0}]: {1}", request.RequestUri, ex.Message);
    // Pull next element — this request is lost, caller will timeout
    if (!HasBeenPulled(stage._inlet))
        Pull(stage._inlet);
}
```

Also change `onUpstreamFailure: FailStage` → `onUpstreamFailure: ex => Log.Error(ex, "...")`

**Acceptance Criteria:**
- [x] `Http10EncoderStage` and `Http11EncoderStage` no longer call `FailStage`
- [x] Encoding errors are logged with request context
- [x] Stage continues processing after error
- [x] `onUpstreamFailure` logs instead of failing
- [x] Stream test: send a malformed request, verify stream stays alive for next request
- [x] Existing tests asserting `FailStage`/`CompleteStage` on encode errors → **deleted**
- [x] Remaining encoder stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-003: Convert Decoder Stages to Log-and-Continue
**Description:** As a developer, I want decoding errors to not kill the stream so that one corrupt response doesn't destroy the client.

**Files:**
- `src/TurboHttp/Streams/Stages/Http10DecoderStage.cs`, lines 63, 71, 75
- `src/TurboHttp/Streams/Stages/Http11DecoderStage.cs`, line 65

**Problem:** Decoder stages call `FailStage(ex)` on decode errors AND call `CompleteStage()` after emitting EOF-delimited responses.

**Required Change:**
1. Replace `FailStage(ex)` → log error and pull next element
2. Remove `CompleteStage()` after EOF response — emit the response and continue waiting for next connection data
3. Replace `onUpstreamFailure: FailStage` → log-and-absorb
4. For HTTP/1.0 (connection-close-based): after emitting response on EOF, reset decoder state and pull for next connection's data

**Acceptance Criteria:**
- [x] Decoder stages never call `FailStage` or self-`CompleteStage`
- [x] Decode errors logged with context (bytes received, error type)
- [x] Stream stays alive after a decode error
- [x] HTTP/1.0 decoder resets state after EOF response instead of completing
- [x] Stream test: corrupt response bytes, verify stream survives
- [x] Existing tests asserting `FailStage`/`CompleteStage` on decode errors → **deleted**
- [x] Remaining decoder stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-004: Convert RequestEnricherStage to Log-and-Continue
**Description:** As a developer, I want request enrichment errors to not kill the stream.

**File:** `src/TurboHttp/Streams/Stages/RequestEnricherStage.cs`, line 48

**Current:** `FailStage(ex)` in catch block + `onUpstreamFailure: FailStage`

**Required Change:**
```csharp
catch (Exception ex)
{
    Log.Warning("RequestEnricherStage: Enrichment failed for [{0}]: {1}", request.RequestUri, ex.Message);
    // Pass request through unenriched — downstream will handle it
    Emit(stage._outlet, element);
}
```

**Acceptance Criteria:**
- [x] Stage never calls `FailStage`
- [x] Errors logged, request passed through (possibly unenriched)
- [x] Stream test verifies stream survives enrichment error
- [x] Existing tests asserting `FailStage` on enrichment errors → **deleted**
- [x] Remaining enricher tests pass
- [x] Build compiles with 0 errors

---

### TASK-005: Convert ConnectionStage to Log-and-Continue
**Description:** As a developer, I want connection-level errors to not kill the stream so that a TCP disconnect or missing handle doesn't destroy the client.

**File:** `src/TurboHttp/IO/Stages/ConnectionStage.cs`

**Current Issues:**
- Line 68: `CompleteStage()` on upstream finish (correct for Dispose path)
- Line 82: `CompleteStage()` on downstream finish (correct for Dispose path)
- Line 190: `FailStage(new InvalidOperationException("DataItem received but no ConnectionHandle"))` — **kills stream**

**Required Change for line 190:**
```csharp
if (_handle is null)
{
    Log.Warning("ConnectionStage: DataItem received without ConnectionHandle — dropping. URI: {0}", ...);
    // Don't fail — the connection may be re-establishing
    // Pull next element to keep stream flowing
    if (!HasBeenPulled(stage._inlet))
        Pull(stage._inlet);
    return;
}
```

Also fix the race condition (from original audit): use local copy of `_handle`.

**Acceptance Criteria:**
- [x] `FailStage` removed from ConnectionStage
- [x] Missing handle logs warning and drops element instead of killing stream
- [x] Race condition on `_handle` fixed (local copy pattern)
- [x] `onUpstreamFinish`/`onDownstreamFinish` → `CompleteStage()` remains (Dispose path)
- [x] Stream test: simulate missing handle, verify stream survives
- [x] Existing tests asserting `FailStage` on missing handle → **deleted**
- [x] Remaining ConnectionStage stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-006: Convert Http20ConnectionStage to Log-and-Recover
**Description:** As a developer, I want HTTP/2 connection-level errors (GOAWAY, flow control violations) to not kill the stream so that the connection pool can reconnect.

**File:** `src/TurboHttp/Streams/Stages/Http20ConnectionStage.cs`

**Current Issues:**
- Line 163: `FailStage(new Http2Exception("GOAWAY"))` — **kills stream on GOAWAY**
- Line 265: `FailStage(new Exception("Connection window exceeded"))` — **kills stream**
- Line 270: `FailStage(new Exception("Stream window exceeded"))` — **kills stream**
- Line 332: `FailStage(new Exception("Outbound flow control exceeded"))` — **kills stream**

**Required Change:**
1. **GOAWAY**: Log, signal ConnectionStage to reconnect via a control message, don't fail stream. Pending requests should receive a "connection reset" error response, but the stream lives on.
2. **Flow control violations**: Log, reset the connection (signal reconnect), don't fail stream. These indicate a protocol mismatch that reconnect may fix.
3. Replace `new Exception(...)` with `Http2Exception` for proper typing.
4. Add `return` after each error handler to prevent continued execution.

**Acceptance Criteria:**
- [x] Zero `FailStage` calls remain in Http20ConnectionStage
- [x] GOAWAY triggers reconnect signal, not stream failure
- [x] Flow control violations trigger reconnect, not stream failure
- [x] All errors use `Http2Exception` with RFC references
- [x] Stream test: simulate GOAWAY, verify stream survives and reconnects
- [x] Stream test: simulate flow control violation, verify stream recovers
- [x] Existing tests asserting `FailStage` on GOAWAY/flow control → **deleted**
- [x] Remaining HTTP/2 connection tests pass
- [x] Build compiles with 0 errors

---

### TASK-007: Convert Correlation Stages to Never-Complete
**Description:** As a developer, I want correlation stages to never self-complete so that the request-response matching pipeline stays alive.

**Files:**
- `src/TurboHttp/Streams/Stages/Http1XCorrelationStage.cs`, lines 102, 120: `CompleteStage()`
- `src/TurboHttp/Streams/Stages/Http20CorrelationStage.cs`, line 110: `CompleteStage()`

**Problem:** These stages self-complete when both upstream ports finish and pending queues are empty. But in a long-lived client, the upstream port finishing means Dispose — which is correct. The issue is that they also complete on transient conditions (empty queues after connection close).

**Required Change:**
1. Remove `CompleteStage()` from `onPush`/`TryComplete` logic
2. Only allow completion via `onUpstreamFinish` propagation (Dispose path)
3. Correlation stages should be stateless between connections — reset pending/waiting queues on connection reset signals

**Acceptance Criteria:**
- [x] Correlation stages never call `CompleteStage()` except in `onUpstreamFinish` (Dispose path)
- [x] Empty queues don't trigger completion
- [x] Stream test: complete a request, verify stage stays alive for next request
- [x] Existing tests asserting `CompleteStage` on empty queues → **deleted**
- [x] Remaining correlation tests pass
- [x] Build compiles with 0 errors

---

### TASK-008: Convert Business Logic Stages (Redirect, Retry, Cache, Cookie, Decompression) to Never-Fail
**Description:** As a developer, I want all business logic stages to absorb upstream failures instead of propagating them so that the pipeline survives.

**Files (all have `onUpstreamFailure: FailStage`):**
- `CacheLookupStage.cs` (line 94)
- `CacheStorageStage.cs` (line 87)
- `CookieInjectionStage.cs` (line 48)
- `CookieStorageStage.cs` (line 49)
- `DecompressionStage.cs` (line 44)
- `RedirectStage.cs` (line 120)
- `RetryStage.cs` (line 143)

**Additional — stages that self-complete in `onPush`:**
- `RetryStage.cs` (lines 136, 206): `CompleteStage()` when retry/upstream finishes
- `ConnectionReuseStage.cs` (lines 65, 135): `CompleteStage()` on upstream finish conditions

**Required Change:**
1. Replace all `onUpstreamFailure: FailStage` → `onUpstreamFailure: ex => Log.Error(ex, "StageName: upstream failure absorbed")`
2. Remove all `CompleteStage()` calls from `onPush` handlers
3. `RetryStage`: when upstream finishes, drain pending retries but don't complete
4. `ConnectionReuseStage`: when connection closes, reset state but don't complete

**Acceptance Criteria:**
- [x] Zero `FailStage` calls remain in any business logic stage
- [x] Zero `CompleteStage` calls in `onPush` handlers
- [x] All stages have `onUpstreamFailure` → log-and-absorb
- [x] Stream tests: inject upstream failure, verify each stage survives
- [x] Existing tests asserting `FailStage`/`CompleteStage` on error scenarios → **deleted**
- [x] Remaining business logic stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-009: Convert Internal Infrastructure Stages (GroupBy, Merge, Extract, Allocator, Preface, Request2Frame) to Never-Fail
**Description:** As a developer, I want internal pipeline infrastructure stages to absorb failures.

**Files:**
- `GroupByHostKeyStage.cs` (line 106): `CompleteStage()` in onPush
- `MergeSubstreamsStage.cs` (lines 77, 118, 126): `CompleteStage()` + `FailStage` via async callback
- `ExtractOptionsStage.cs` (lines 55-56): `onUpstreamFinish: CompleteStage`, `onUpstreamFailure: FailStage`
- `StreamIdAllocatorStage.cs` (line 42): `onUpstreamFinish: CompleteStage`
- `PrependPrefaceStage.cs` (lines 69-70): `onUpstreamFinish: CompleteStage`, `onUpstreamFailure: FailStage`
- `Request2FrameStage.cs` (lines 63, 81): `CompleteStage()` in onPush
- `Http20DecoderStage.cs` (lines 69-70): `onUpstreamFinish: CompleteStage`, `onUpstreamFailure: FailStage`

**Required Change:**
1. `onUpstreamFailure: FailStage` → `onUpstreamFailure: ex => Log.Error(ex, "...")`
2. Remove `CompleteStage()` from `onPush` handlers where they trigger on transient conditions
3. Keep `onUpstreamFinish: CompleteStage` — this is the Dispose path and is correct
4. `MergeSubstreamsStage`: replace `_onSubstreamFailed = GetAsyncCallback<Exception>(FailStage)` with log-and-continue

**Acceptance Criteria:**
- [x] Zero `FailStage` calls remain in infrastructure stages
- [x] `CompleteStage()` only called from `onUpstreamFinish` (Dispose path) and `onDownstreamFinish` (Dispose path)
- [x] `MergeSubstreamsStage` absorbs substream failures
- [x] Stream tests: verify infrastructure stages survive upstream failures
- [x] Existing tests asserting `FailStage`/`CompleteStage` on transient errors → **deleted**
- [x] Remaining stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-010: Add "Stream Survives Error" Integration Tests
**Description:** As a developer, I want integration tests that prove the stream survives various error conditions end-to-end.

**File:** `src/TurboHttp.StreamTests/Streams/NN_StreamSurvivalTests.cs` (new file)

**Acceptance Criteria:**
- [x] Test: Send request → encoding error (malformed URI) → send normal request → succeeds
- [x] Test: Send request → server returns corrupt response → send normal request → succeeds
- [x] Test: Send request → connection drops mid-response → send normal request → succeeds (reconnect)
- [x] Test: Send request → HTTP/2 GOAWAY received → send normal request → succeeds (new connection)
- [x] Test: Send request → timeout → send normal request → succeeds
- [x] Test: 100 sequential requests where every 10th triggers an error → 90 succeed, 10 fail individually, stream never dies
- [x] Test: Verify stream only completes when client is disposed
- [x] Test: After Dispose, verify all stages have completed
- [x] `dotnet test --filter "FullyQualifiedName~StreamSurvivalTests"` — all pass

---

### Phase 2: Critical Bugs (Priority: High)

---

### TASK-011: Fix ActorSystem Name "turbomqtt" → "turbohttp"
**Description:** As a developer, I want the ActorSystem to have the correct name so that logs, metrics, and monitoring show the right application identity.

**File:** `src/TurboHttp/Hosting/TurboClientServiceCollectionExtensions.cs`, line 22

**Required Change:** Replace `"turbomqtt"` with `"turbohttp"`.

**Acceptance Criteria:**
- [x] ActorSystem name is `"turbohttp"`
- [x] Unit test verifies the ActorSystem name after service registration
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-012: Fix Blocking .Result Call in Http10Encoder
**Description:** As a developer, I want the HTTP/1.0 encoder to avoid synchronous blocking so that no deadlock can occur.

**File:** `src/TurboHttp/Protocol/RFC1945/Http10Encoder.cs`, lines 59-68

**Required Change:** Use `content.ReadAsStream()` with synchronous I/O instead of `.Result`. The Akka stage push handler is synchronous, so async is not an option here.

```csharp
private static ReadOnlyMemory<byte> ReadBody(HttpContent? content)
{
    if (content == null) return ReadOnlyMemory<byte>.Empty;
    using var stream = content.ReadAsStream();
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return ms.ToArray().AsMemory();
}
```

Check `Http11Encoder` for same pattern.

**Acceptance Criteria:**
- [x] No `.Result` or `.Wait()` call remains in any encoder
- [x] Unit test verifies encoding with streaming content works correctly
- [x] All existing RFC1945/RFC9112 encoder tests pass
- [x] Build compiles with 0 errors

---

### TASK-013: Fix IMemoryOwner Leak in ClientByteMover
**Description:** As a developer, I want rented memory buffers to be disposed when channel writes fail so that memory does not leak on rapid disconnects.

**File:** `src/TurboHttp/IO/ClientByteMover.cs`, lines 86-91

**Required Change:**
```csharp
if (!state.InboundWriter.TryWrite((pooled, length)))
{
    pooled.Dispose();
    return;
}
```

**Acceptance Criteria:**
- [x] `TryWrite` return value is checked
- [x] Buffer is disposed when `TryWrite` returns false
- [x] Unit test verifies disposal on closed channel
- [x] All existing IO tests pass
- [x] Build compiles with 0 errors

---

### TASK-014: Fix Race Condition on _handle in ConnectionStage
**Description:** As a developer, I want `_handle` access in ConnectionStage to be thread-safe.

**File:** `src/TurboHttp/IO/Stages/ConnectionStage.cs`, lines 147-201

**Required Change:** Capture `_handle` into a local variable before use (already part of TASK-005, but the race condition fix is independent of the FailStage removal):
```csharp
var handle = _handle;
if (handle is null) { /* log and drop, per TASK-005 */ }
_ = handle.OutboundWriter.WriteAsync(...);
```

**Acceptance Criteria:**
- [x] All `_handle` accesses use a local copy pattern
- [x] No direct field access after null check
- [x] Stream test verifies concurrent disconnect doesn't crash
- [x] Build compiles with 0 errors

---

### TASK-015: Fix TaskCompletionSource Leak on Timeout in TurboHttpClient
**Description:** As a developer, I want pending request entries to be cleaned up on timeout/cancellation.

**File:** `src/TurboHttp/Client/TurboHttpClient.cs`, lines 74-81

**Required Change:**
```csharp
_pending.TryAdd(requestId, tcs);
try
{
    await _manager.Requests.WriteAsync(request, cancellationToken);
    return await tcs.Task.WaitAsync(Timeout, cancellationToken);
}
finally
{
    _pending.TryRemove(requestId, out _);
}
```

**Acceptance Criteria:**
- [x] TCS always removed from `_pending` after completion, timeout, or cancellation
- [x] Unit test verifies `_pending` is empty after timeout
- [x] Unit test verifies `_pending` is empty after cancellation
- [x] Unit test verifies `_pending` is empty after success
- [x] Build compiles with 0 errors

---

### TASK-016: Fix Shared Options Mutation in TurboHttpClientFactory
**Description:** As a developer, I want each client to get its own options copy.

**File:** `src/TurboHttp/Client/TurboHttpClientFactory.cs`, lines 10-14

**Required Change:** Clone options before mutation.

**Acceptance Criteria:**
- [x] Each `CreateClient` call gets an independent options copy
- [x] Unit test: create two clients with different configs, verify isolation
- [x] Unit test: verify `IOptionsMonitor.CurrentValue` is not mutated
- [x] Build compiles with 0 errors

---

### Phase 3: Design Issues (Priority: High)

---

### TASK-017: Convert Synchronous Socket Operations to Async
**Description:** As a developer, I want TCP connect and TLS handshake to be async so that actor threads are not blocked.

**Files:**
- `src/TurboHttp/IO/IClientProvider.cs`, line 36: `_socket.Connect(addresses, port)`
- `src/TurboHttp/IO/IClientProvider.cs`, line 114: `_sslStream.AuthenticateAsClient(authOptions)`

**Required Change:**
1. `IClientProvider.GetStream()` → `GetStreamAsync()` returning `Task<Stream>`
2. `_socket.ConnectAsync()`, `_sslStream.AuthenticateAsClientAsync()`
3. Update all callers

**Acceptance Criteria:**
- [x] No synchronous `Connect()` or `AuthenticateAsClient()` calls remain
- [x] All callers updated to async
- [x] All existing IO/stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-018: Handle Channel Errors in DrainResponsesAsync
**Description:** As a developer, I want all pending requests to be failed when the response channel closes with an error.

**File:** `src/TurboHttp/Client/TurboHttpClient.cs`, lines 47, 59-71

**Required Change:**
1. Wrap in try/catch
2. On exception: iterate `_pending`, `TrySetException` on each TCS, then clear
3. Log the error

**Acceptance Criteria:**
- [x] Channel read errors propagated to all pending TCS instances
- [x] Unit test: simulate channel closure with exception, verify pending requests fail
- [x] Build compiles with 0 errors

---

### TASK-019: Implement IAsyncDisposable for ClientState Channel Cleanup
**Description:** As a developer, I want channels to be properly drained and IMemoryOwner buffers disposed on disconnect.

**File:** `src/TurboHttp/IO/ClientState.cs`, lines 28-29

**Required Change:**
1. `ClientState : IAsyncDisposable`
2. In `DisposeAsync`: complete writers, drain readers disposing each `IMemoryOwner<byte>`
3. Update `ClientRunner.PostStop()` to use new disposal

**Acceptance Criteria:**
- [x] All pending `IMemoryOwner<byte>` items are disposed during cleanup
- [x] Unit test verifies disposal
- [x] Build compiles with 0 errors

---

### TASK-020: Fix Fire-and-Forget Tasks in TurboClientStreamManager
**Description:** As a developer, I want fire-and-forget tasks to have proper exception handling.

**File:** `src/TurboHttp/Client/TurboClientStreamManager.cs`, lines 70-83

**Required Change:** Store task reference, add `.ContinueWith(t => Log, OnlyOnFaulted)`.

**Acceptance Criteria:**
- [x] All fire-and-forget tasks have exception continuations
- [x] Unit test verifies faulted task doesn't cause UnobservedTaskException
- [x] Build compiles with 0 errors

---

### TASK-021: Fix Fire-and-Forget Tasks in ClientRunner.PreStart
**Description:** As a developer, I want the three ClientByteMover tasks to have exception handling.

**File:** `src/TurboHttp/IO/ClientRunner.cs`, lines 49-51

**Required Change:** Store task references, add fault continuations that trigger `DoClose`.

**Acceptance Criteria:**
- [x] All three tasks have exception continuations
- [x] Faulted tasks trigger `DoClose`
- [x] Build compiles with 0 errors

---

### Phase 4: Performance (Priority: Medium — High-Impact Only)

---

### TASK-022: Add Secondary Index to HttpCacheStore for O(1) Lookup
**Description:** As a developer, I want cache lookups to be O(1) instead of O(n).

**File:** `src/TurboHttp/Protocol/RFC9111/HttpCacheStore.cs`, lines 48-63

**Required Change:** Add `Dictionary<string, List<LinkedListNode>>` index. Update Get/Set/Eviction.

**Acceptance Criteria:**
- [x] Get uses index (no linear scan)
- [x] Set/Eviction maintain index consistency
- [x] All existing RFC9111 cache tests pass
- [x] Build compiles with 0 errors

---

### TASK-023: Eliminate .ToArray() Allocations in Http2FrameDecoder
**Description:** As a developer, I want the HTTP/2 frame decoder to avoid unnecessary byte array copies.

**File:** `src/TurboHttp/Protocol/RFC9113/Http2FrameDecoder.cs`, lines 50, 68, 122, 213, 223

**Required Change:** Change frame types to accept `ReadOnlyMemory<byte>` for payloads where source buffer lifetime allows.

**Acceptance Criteria:**
- [x] Reduced `.ToArray()` calls
- [x] All existing RFC9113 decoder tests pass
- [x] Build compiles with 0 errors

---

### Phase 5: Code Quality (Priority: Low)

---

### TASK-024: Add Meaningful Exception Messages to HuffmanCodec
**Description:** As a developer, I want Huffman decoding errors to include diagnostic context.

**File:** `src/TurboHttp/Protocol/RFC7541/HuffmanCodec.cs`, lines 157, 170, 184

**Required Change:** Replace `throw new HpackException("")` with RFC-referenced messages.

**Acceptance Criteria:**
- [x] All `HpackException` throws have meaningful messages
- [x] All existing HPACK tests pass
- [x] Build compiles with 0 errors

---

### TASK-025: Replace Magic Numbers with Constants in Http2FrameDecoder
**Description:** As a developer, I want named constants instead of magic numbers.

**File:** `src/TurboHttp/Protocol/RFC9113/Http2FrameDecoder.cs`

**Required Change:** Add `const int FrameHeaderSize = 9;` etc., use flag enums for `0x08`/`0x20`.

**Acceptance Criteria:**
- [x] No magic numbers in frame parsing
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-026: Add Stream ID Validation to Http2Frame Constructor
**Description:** As a developer, I want negative stream IDs rejected at construction time.

**File:** `src/TurboHttp/Protocol/RFC9113/Http2Frame.cs`, line 104

**Acceptance Criteria:**
- [x] Negative stream IDs throw `ArgumentOutOfRangeException`
- [x] Unit tests verify
- [x] Build compiles with 0 errors

---

### TASK-027: Remove Dead Code in HttpSizePredictor
**File:** `src/TurboHttp/Protocol/HttpSizePredictor.cs`, lines 183-184

**Required Change:** Remove dead assignment, fix logic to `return content.Headers.ContentLength ?? 0;`

**Acceptance Criteria:**
- [x] Dead code removed
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-028: Fix Inconsistent Buffer Estimation in Encoder Stages
**Files:** `Http10EncoderStage.cs` vs `Http11EncoderStage.cs`

**Required Change:** Unify to `_minBufferSize + contentLength`.

**Acceptance Criteria:**
- [x] Both use identical estimation
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-029: Fix Volatile/Volatile.Read Inconsistency in ConnectionHandle
**File:** `src/TurboHttp/Lifecycle/ConnectionHandle.cs`, lines 22-27

**Required Change:** Use `volatile` keyword alone (sufficient for int), remove `Volatile.Write` and `#pragma`.

**Acceptance Criteria:**
- [x] Consistent synchronization
- [x] `#pragma` removed
- [x] Build compiles with 0 errors

---

### TASK-030: Add TryGetValue Guards in Http20StreamStage
**File:** `src/TurboHttp/Streams/Stages/Http20StreamStage.cs`, lines 162, 176, 188

**Required Change:** Replace `_streams[frame.StreamId]` with `TryGetValue`, log and drop on unknown stream.

**Acceptance Criteria:**
- [x] All dict accesses use `TryGetValue`
- [x] Unknown streams handled gracefully (logged, not exception)
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-031: Change ClientRunner to Internal Sealed
**File:** `src/TurboHttp/IO/ClientRunner.cs`, line 11

**Required Change:** `internal sealed class`. Check other IO types. Add `[InternalsVisibleTo]` if needed.

**Acceptance Criteria:**
- [x] `ClientRunner` is `internal sealed`
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-032: Add Exception Logging to ClientByteMover Catch Blocks
**File:** `src/TurboHttp/IO/ClientByteMover.cs`, lines 105-113

**Required Change:** Capture exception, include in `DoCloseWithReason(ex)` or log.

**Acceptance Criteria:**
- [x] Exception captured, not swallowed
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-033: Fix LINQ Allocation in Http20DecoderStage Hot Path
**File:** `src/TurboHttp/Streams/Stages/Http20DecoderStage.cs`, line 58

**Required Change:** Replace `.Where().ToList()` with manual loop.

**Acceptance Criteria:**
- [ ] No LINQ in decode path
- [ ] All existing tests pass
- [ ] Build compiles with 0 errors

---

### TASK-034: Replace String Key with Struct Key in PrependPrefaceStage
**File:** `src/TurboHttp/Streams/Stages/PrependPrefaceStage.cs`, lines 51-52

**Required Change:** Use tuple key `(Host, Port, isTls)`.

**Acceptance Criteria:**
- [ ] No string interpolation for key
- [ ] All existing tests pass
- [ ] Build compiles with 0 errors

---

### TASK-035: Fix ConnectionActor Reconnect Lifecycle
**File:** `src/TurboHttp/Lifecycle/ConnectionActor.cs`, lines 94-132

**Required Change:**
1. Track mover tasks (from TASK-021)
2. On reconnect: await mover completion, then create fresh channels
3. Add exponential backoff cap (max 30s)

**Acceptance Criteria:**
- [ ] Old pump tasks awaited before new channels
- [ ] Backoff capped
- [ ] All existing lifecycle tests pass
- [ ] Build compiles with 0 errors

---

## Functional Requirements

- FR-1: **No stage may call `FailStage()` or `CompleteStage()` in response to per-request or per-connection errors.** Only `onUpstreamFinish` (Dispose path) and `onDownstreamFinish` (Dispose path) may complete stages.
- FR-2: All errors in stages must be logged via Akka `ILoggingAdapter`
- FR-3: All `IMemoryOwner<byte>` buffers must be disposed on every code path
- FR-4: No `.Result` or `.Wait()` calls in production code
- FR-5: All dictionary accesses on frame-stream maps must use `TryGetValue`
- FR-6: All fire-and-forget tasks must have exception continuations
- FR-7: Internal implementation types must not be public
- FR-8: Every fix must include at least one test
- FR-9: **Existing tests that assert `FailStage`, `CompleteStage`, or stream termination on error input must be deleted, not fixed.** These tests validate the old design. Do not rename or adapt them — the "Stream Survives" tests (TASK-010) replace their coverage.

## Non-Goals

- No new features or API additions
- No refactoring beyond what's needed for each fix
- No changes to the Akka Streams graph topology or pipeline wiring
- No changes to test infrastructure
- Performance tasks limited to high-impact items only

## Technical Considerations

- **Akka Stage Constraints:** `onPush` handlers are synchronous. Logging must be non-blocking. Use `Log` from `GraphStageLogic.Log` (built-in Akka logging).
- **"Log and drop" vs "Log and pass through":** For encoder errors, the element must be dropped (can't emit garbage bytes). For enricher errors, the element can be passed through unenriched. For decoder errors, the corrupt bytes are dropped and the decoder state is reset.
- **onUpstreamFinish remains CompleteStage:** This is correct. Upstream finish originates from Dispose closing the request channel → Source completes → propagation. This IS the disposal path.
- **MergeSubstreamsStage special case:** This stage has async callbacks that call `FailStage`. The async callback must be changed to log-and-drop. Be careful with the substream lifecycle — a failed substream should be removed from tracking, not kill the merge.
- **Http10DecoderStage EOF handling:** HTTP/1.0 relies on connection close for message termination. After emitting the response on EOF, the decoder must reset its state and wait for the next connection's data — not complete.
- **Test strategy for "Stream Survives":** Create `StreamSurvivalTests` (TASK-010) that materializes the full pipeline, injects errors at various points, and verifies the stream continues processing subsequent requests.

## Success Metrics

- Zero `FailStage` calls in any stage except for irrecoverable Akka infrastructure errors
- Zero `CompleteStage` calls in `onPush` handlers
- Stream survives: encoding error, decoding error, connection drop, GOAWAY, flow control violation, timeout
- Only `Dispose()` terminates the stream
- All remaining tests pass (after deleting tests that assert old FailStage/CompleteStage behavior)
- New "Stream Survives" tests (TASK-010) all pass
- Zero new compiler warnings

## Open Questions

1. **Error response synthesis:** When an encoder fails, should the stage emit a synthesized 502 error response to the correlation stage so the caller's TCS is resolved? Or should the caller rely on timeout? Synthesizing responses requires the stage to know the response outlet — which encoder stages don't have. The cleanest approach may be timeout-based cleanup (TASK-015).
2. **GOAWAY recovery scope:** When a GOAWAY arrives, should all in-flight HTTP/2 streams on that connection be failed with an error response? Or should they be retried on a new connection transparently? The retry stage already handles 503 — a connection error could be surfaced as a "retryable" signal.
3. **Logging volume:** In high-error scenarios, log-and-continue could produce massive log output. Should we add rate-limiting to error logs (e.g., "suppressed N similar errors in last 10s")?
4. **MergeSubstreamsStage failure policy:** If a substream (per-host connection) fails, should the merge stage remove it and continue, or should it signal the GroupByHostKeyStage to recreate it?
