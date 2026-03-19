# Plan 3: RequestEndpoint Propagation Audit & Stage Design Fixes

## Introduction

Preventive audit of RequestEndpoint propagation through the entire TurboHttp pipeline, plus stage design review. The analysis uncovered 4 RequestEndpoint bugs, 2 shared-state bugs in post-processing stages, 1 thread-safety bug in CookieJar, and 1 blocking call in CacheStorageStage. While some bugs don't currently cause visible failures thanks to the `GroupByHostKey` substream model, others (RetryStage, RedirectStage, CookieJar) are active correctness bugs that affect production behavior under concurrent load.

## Goals

- Fix shared-state bugs in `RetryStage` and `RedirectStage` — both use global counters/state that bleed across independent request chains
- Fix `CookieJar` thread-safety — concurrent access from different async boundary islands
- Close all `RequestEndpoint` gaps so every `IOutputItem`/`IControlItem` carries the correct `Key`
- `RedirectHandler.BuildRedirectRequest()` must preserve `Version` from the original request
- Replace blocking call in `CacheStorageStage` with async pattern
- Verify and document correct stage ordering with tests
- Full test coverage for all fixes
- Zero regressions — all existing 2111+ tests stay green

## User Stories

Tasks are ordered by: (1) severity, (2) logical grouping, (3) dependencies.

---

### GROUP A: Shared-State & Thread-Safety Bugs (Critical — Active Runtime Bugs)

---

### TASK-001: Fix RetryStage — Per-Request Attempt Tracking Instead of Global Counter

**Description:** As a developer, I want `RetryStage` to track retry attempts per-request instead of using a single global counter, so that concurrent request chains don't interfere with each other's retry logic.

**Context:**
- File: `src/TurboHttp/Streams/Stages/RetryStage.cs`, line 58
- `_attemptCount` is a single field shared across ALL requests flowing through the stage
- The stage sits in the shared post-processing island — responses from ALL connections pass through it
- **Bug scenario:**
  1. Request A → 503 → retry emitted (`_attemptCount` becomes 2)
  2. While Request A is in-flight through the retry feedback loop, Request B arrives
  3. Request B → 503 → `RetryEvaluator.Evaluate()` receives `attemptCount: 2` even though it's B's first attempt
  4. If `MaxRetries = 2`, Request B would NOT be retried even though it should be
- The counter is never reset between different request chains

**Analysis — why this happens:**
The FanOut demand pattern means the stage processes one element at a time. But retried requests re-enter the pipeline through `retryMerge → CacheLookup → Engine → ... → RetryStage`. While the retried request traverses this path, OTHER responses can flow into the RetryStage. The shared `_attemptCount` is then applied to the wrong request.

**Solution — Per-request tracking via HttpRequestMessage.Options:**
Store the attempt count on the request itself using `HttpRequestMessage.Options`, so it travels WITH the request through the pipeline:

```csharp
private static readonly HttpRequestOptionsKey<int> AttemptCountKey = new("TurboHttp.RetryAttemptCount");

// In onPush:
var attemptCount = original.Options.TryGetValue(AttemptCountKey, out var count) ? count : 1;

var decision = RetryEvaluator.Evaluate(original, response, ..., attemptCount: attemptCount, ...);

if (decision.ShouldRetry)
{
    original.Options.Set(AttemptCountKey, attemptCount + 1);
    _pendingRetryRequest = original;
    ...
}
```

This eliminates the `_attemptCount` field entirely. Each request carries its own retry count.

**Acceptance Criteria:**
- [x] `_attemptCount` field removed from RetryStage Logic
- [x] Retry count tracked per-request via `HttpRequestMessage.Options`
- [x] Concurrent request chains don't interfere with each other's retry counts
- [x] Unit tests: Request A retried 2x, then Request B retried 1x — B starts at attempt 1, not 3
- [x] Unit tests: Interleaved requests each get independent retry budgets
- [x] Unit tests: Retry-After timer on Request A doesn't block Request B's retry evaluation
- [x] Existing RetryStage tests stay green
- [x] Build compiles without errors

---

### TASK-002: Fix RedirectStage & RedirectHandler — Per-Request State + Version Preservation

**Description:** As a developer, I want `RedirectStage` to track redirect state per-request instead of using a single shared `RedirectHandler`, AND I want `RedirectHandler.BuildRedirectRequest()` to preserve `Version` from the original request.

**Context — Shared State Bug:**
- File: `src/TurboHttp/Streams/Stages/RedirectStage.cs`, line 26
- `_handler` is a single `RedirectHandler` instance shared across ALL requests
- `RedirectHandler` maintains `_visitedUris` (HashSet) and `_redirectCount` (int) — NEVER reset between request chains
- **Bug scenario 1 (false max-redirect):**
  1. Request A → 301 → redirect 1, 301 → redirect 2, 301 → redirect 3 (`_redirectCount` = 3)
  2. Request B → 301 → `_redirectCount` is already 3, exceeds `MaxRedirects` → throws `RedirectException` even though B's first redirect
- **Bug scenario 2 (false loop detection):**
  1. Request A redirects through `https://api.example.com/v2`
  2. Request B independently redirects to `https://api.example.com/v2`
  3. `_visitedUris` already contains this URI → throws "Redirect loop detected" even though B's redirect chain is independent

**Context — Missing Version:**
- File: `src/TurboHttp/Protocol/RFC9110/RedirectHandler.cs`, line 109
- Current: `new HttpRequestMessage(newMethod, locationUri)` — Version defaults to `HttpVersion.Version11`
- Problem: When an HTTP/2 request receives a redirect, the new request is classified as HTTP/1.1
- `RequestEndpoint.FromRequest()` uses `Version` as part of the key → wrong substream
- Comparison: `CacheValidationRequestBuilder.BuildConditionalRequest()` correctly sets `Version = original.Version`

**Solution — Per-request RedirectHandler via HttpRequestMessage.Options:**
Create a fresh `RedirectHandler` for each new request chain and store it on the request:

```csharp
private static readonly HttpRequestOptionsKey<RedirectHandler> RedirectHandlerKey
    = new("TurboHttp.RedirectHandler");

// In onPush, when processing a redirect:
RedirectHandler handler;
if (!original.Options.TryGetValue(RedirectHandlerKey, out handler))
{
    handler = new RedirectHandler(_stage._policy);  // new handler per chain
    original.Options.Set(RedirectHandlerKey, handler);
}

var newRequest = handler.BuildRedirectRequest(original, response);
newRequest.Options.Set(RedirectHandlerKey, handler);  // handler travels with request
```

**Solution — Version preservation in RedirectHandler:**
```csharp
var newRequest = new HttpRequestMessage(newMethod, locationUri)
{
    Version = original.Version
};
```

**Edge case:** On cross-scheme redirects (e.g. `http://` → `https://`), the ideal version might change. However, preserving the version is RFC-compliant since version negotiation happens at the transport level. The redirect request re-enters the pipeline at `redirectMerge` (AFTER `RequestEnricherStage`), so `DefaultRequestVersion` is NOT re-applied.

**Acceptance Criteria:**
- [x] Each request chain gets its own `RedirectHandler` instance
- [x] `_visitedUris` and `_redirectCount` are isolated per request chain
- [x] Concurrent request chains don't interfere with each other's redirect limits or loop detection
- [x] `newRequest.Version = original.Version` in `BuildRedirectRequest()` (line 109)
- [x] Verify the CookieJar overload (line 232) inherits the Version fix (it calls `BuildRedirectRequest` internally, so automatic)
- [x] Policy (`MaxRedirects`, `AllowHttpsToHttpDowngrade`) still shared from `TurboClientOptions` — only the tracking state is per-request
- [x] Unit tests: Request A exhausts 5 redirects, then Request B starts fresh with 0
- [x] Unit tests: Request A and B can visit the same URI independently without false loop detection
- [x] Unit tests: Redirect from HTTP/2 request preserves Version 2.0
- [x] Unit tests: Redirect from HTTP/1.0 request preserves Version 1.0
- [x] Unit tests: Cross-origin redirect preserves Version
- [x] Existing RedirectHandler and RedirectStage tests stay green
- [x] Build compiles without errors

---

### TASK-003: Fix CookieJar Thread-Safety — Add Synchronization

**Description:** As a developer, I want `CookieJar` to be thread-safe, so that concurrent access from `CookieInjectionStage` (pre-processing island) and `CookieStorageStage` (post-processing island) doesn't corrupt cookie state.

**Context:**
- File: `src/TurboHttp/Protocol/RFC6265/CookieJar.cs`
- `CookieJar` uses `List<CookieEntry>` internally with NO synchronization
- `CookieInjectionStage` (pre-processing, fused island 1) calls `AddCookiesToRequest()` — iterates `_cookies`
- `CookieStorageStage` (post-processing, fused island 3) calls `ProcessResponse()` — calls `_cookies.RemoveAll()` and `_cookies.Add()`
- These stages run on **different async boundaries** (separated by `Attributes.CreateAsyncBoundary()` in `Engine.cs` lines 118, 127)
- This means they can run **concurrently on different threads**
- `HttpCacheStore` already correctly uses `lock(_lock)` for all operations — `CookieJar` should follow the same pattern

**Bug scenario:**
1. Thread 1 (pre-processing): `CookieInjectionStage` iterates `_cookies` via `AddCookiesToRequest()`
2. Thread 2 (post-processing): `CookieStorageStage` calls `ProcessResponse()` which does `_cookies.RemoveAll()` + `_cookies.Add()`
3. Result: `InvalidOperationException: Collection was modified during enumeration` or silent data corruption

**Solution:** Add `lock` synchronization to all public methods of `CookieJar`, following the same pattern as `HttpCacheStore`:

```csharp
public sealed class CookieJar
{
    private readonly object _lock = new();
    private readonly List<CookieEntry> _cookies = new();

    public void ProcessResponse(Uri requestUri, HttpResponseMessage response)
    {
        lock (_lock)
        {
            // existing logic
        }
    }

    public void AddCookiesToRequest(Uri requestUri, ref HttpRequestMessage request)
    {
        lock (_lock)
        {
            // existing logic
        }
    }
}
```

**Acceptance Criteria:**
- [x] All public methods of `CookieJar` are synchronized with `lock`
- [x] Pattern matches `HttpCacheStore` locking approach
- [x] Unit tests: Concurrent read + write doesn't throw or corrupt
- [x] Unit tests: Verify cookie injection and storage work correctly under contention
- [x] Existing CookieJar tests stay green
- [x] Build compiles without errors

---

### GROUP B: RequestEndpoint Propagation Fixes

---

### TASK-004: Fix ConnectionReuseStage — Replace RequestEndpoint.Default

**Description:** As a developer, I want `ConnectionReuseStage` to set the correct `RequestEndpoint` on the `ConnectionReuseItem`, so that connection reuse signals are properly tagged.

**Context:**
- File: `src/TurboHttp/Streams/Stages/ConnectionReuseStage.cs`, line 51
- Current: `new ConnectionReuseItem(RequestEndpoint.Default, decision)`
- Problem: `RequestEndpoint.Default` has empty Host, Port 0, Version Unknown
- The stage receives `HttpResponseMessage`, which contains the correct endpoint via `response.RequestMessage.RequestUri` and `response.RequestMessage.Version`
- **Important:** `ConnectionStage` currently uses `_handle?.ConnectionActor` directly (not the Key), so no acute runtime bug — but the Key must still be correct

**Solution:**
- Extract `RequestEndpoint` from `response.RequestMessage` (when available)
- Fallback to `RequestEndpoint.Default` when `RequestMessage` is null (pass-through scenario)

```csharp
// Instead of:
_pendingSignal = new ConnectionReuseItem(RequestEndpoint.Default, decision);

// New:
var endpoint = response.RequestMessage is { RequestUri: not null, Version: not null }
    ? RequestEndpoint.FromRequest(response.RequestMessage)
    : RequestEndpoint.Default;
_pendingSignal = new ConnectionReuseItem(endpoint, decision);
```

**Acceptance Criteria:**
- [x] `ConnectionReuseItem.Key` contains correct `RequestEndpoint` when `response.RequestMessage` is present
- [x] Fallback to `RequestEndpoint.Default` when `response.RequestMessage` is null
- [x] Unit tests: Verify Key on ConnectionReuseItem for normal response (1.0, 1.1, 2.0)
- [x] Unit tests: Verify fallback when RequestMessage is null
- [x] Existing ConnectionReuseStage tests stay green
- [x] Build compiles without errors

---

### TASK-005: Thread RequestEndpoint Through HTTP/2 Frame Pipeline

**Description:** As a developer, I want the `RequestEndpoint` to flow through the HTTP/2 frame pipeline via the data stream, so that downstream stages (`Http20ConnectionStage`, `Http20EncoderStage`) can capture it without constructor injection.

**Context:**
- The `RequestEndpoint` is dynamic — determined at runtime by the requests flowing through
- It MUST NOT be injected via constructors, because the pipeline doesn't know at build time what hosts, ports, and versions will be requested
- The last stage that has access to `HttpRequestMessage` is `Request2FrameStage` — after that, only `Http2Frame` objects flow downstream
- The endpoint information is lost at the `Request2FrameStage` boundary

**Design principle:** The `RequestEndpoint` must always be derived from data flowing through the pipeline, never from graph construction parameters. This ensures correctness even if substream boundaries change, stages are reused in different contexts, or the GroupByHostKey implementation evolves.

**Solution — Thread endpoint through Http2Frame:**

Option A (preferred): Add an optional `RequestEndpoint?` property to `Http2Frame` base class:
- `Request2FrameStage` sets the endpoint on the first `HeadersFrame` it emits per substream (it has access to `HttpRequestMessage` via its input tuple)
- Downstream stages (`Http20ConnectionStage`, `Http20EncoderStage`) capture the endpoint from the first tagged frame
- Once captured, the endpoint is used for all subsequent emissions
- Minimal change: one optional property on `Http2Frame`, one assignment in `Request2FrameStage`

Option B (alternative): Introduce a dedicated `EndpointTagFrame` control item:
- `Request2FrameStage` emits an `EndpointTagFrame` before the first `HeadersFrame`
- Downstream stages intercept it and capture the endpoint
- More explicit but requires handling a new frame type in all downstream stages

Option C (alternative): Capture from first `ConnectItem` via side-channel:
- `ExtractOptionsStage` already creates `ConnectItem { Key = endpoint }`
- Could add a second signal outlet that broadcasts the endpoint to the engine stages
- More complex graph wiring

**Recommended: Option A** — minimal surface area, follows the existing pattern where `DataItem` and `ConnectItem` already carry `Key` properties.

```csharp
// Http2Frame base class addition:
public RequestEndpoint? Endpoint { get; init; }

// In Request2FrameStage, when creating the first HeadersFrame:
var endpoint = RequestEndpoint.FromRequest(request);
headersFrame = new HeadersFrame(streamId, headerBytes, endStream) { Endpoint = endpoint };
// Subsequent frames in the same substream don't need it (downstream captures from first)

// In Http20ConnectionStage / Http20EncoderStage:
private RequestEndpoint _capturedEndpoint;

// On first frame with Endpoint set:
if (frame.Endpoint.HasValue && _capturedEndpoint == default)
{
    _capturedEndpoint = frame.Endpoint.Value;
}
```

**Acceptance Criteria:**
- [x] `Http2Frame` has an optional `RequestEndpoint? Endpoint` property (or equivalent mechanism)
- [x] `Request2FrameStage` sets the endpoint from `HttpRequestMessage` on the first emitted frame
- [x] No constructor injection of `RequestEndpoint` in any stage
- [x] `Http20ConnectionStage` captures endpoint from pipeline (used in TASK-006)
- [x] `Http20EncoderStage` captures endpoint from pipeline (used in TASK-007)
- [x] `Http2Frame.Endpoint` does not affect serialization (`WriteTo`) or `SerializedSize`
- [x] No breaking changes to public APIs
- [x] Build compiles without errors
- [x] All existing stream tests stay green

---

### TASK-006: Fix Http20ConnectionStage — Set Key on StreamAcquireItem via Pipeline

**Description:** As a developer, I want `Http20ConnectionStage` to set the correct `RequestEndpoint` on `StreamAcquireItem`, so that HTTP/2 stream acquisitions are properly tagged.

**Context:**
- File: `src/TurboHttp/Streams/Stages/Http20ConnectionStage.cs`, line 170
- Current: `Emit(stage._outletSignal, new StreamAcquireItem());` with TODO comment
- **Design principle:** Endpoint MUST come from the pipeline, NOT constructor injection

**Solution:** `Http20ConnectionStage` captures the `RequestEndpoint` from the first tagged frame flowing through `_inletRequest` (set by `Request2FrameStage` in TASK-005). Once captured, it uses the endpoint for all subsequent `StreamAcquireItem` emissions.

```csharp
// In Logic class:
private RequestEndpoint _endpoint;

// In _inletRequest onPush, on first HeadersFrame:
if (frame is HeadersFrame headers && _endpoint == default)
{
    _endpoint = headers.Endpoint ?? RequestEndpoint.Default;
}

// Then:
Emit(stage._outletSignal, new StreamAcquireItem { Key = _endpoint });
```

**Depends on:** TASK-005 (pipeline-based endpoint threading)

**Acceptance Criteria:**
- [x] `RequestEndpoint` is captured from the pipeline, NOT via constructor injection
- [x] `Http20ConnectionStage` captures the endpoint from the first tagged frame
- [x] `StreamAcquireItem` is emitted with correct `Key`
- [x] Check whether `MaxConcurrentStreamsItem` also needs a Key (analysis required)
- [x] Unit tests: Verify Key on StreamAcquireItem when processing HEADERS frame
- [x] Existing Http20ConnectionStage tests stay green
- [x] Build compiles without errors

---

### TASK-007: Fix Http20EncoderStage — Set Key on DataItem via Pipeline

**Description:** As a developer, I want `Http20EncoderStage` to set the correct `RequestEndpoint` on every `DataItem`, so that outbound HTTP/2 frames are properly tagged.

**Context:**
- File: `src/TurboHttp/Streams/Stages/Http20EncoderStage.cs`, line 33
- Current: `new DataItem(owner, frame.SerializedSize)` — Key remains `default(RequestEndpoint)`
- Problem: The stage has no access to `RequestEndpoint` because it processes `Http2Frame` objects that carry no request context
- **Design principle:** The `RequestEndpoint` MUST NOT be injected via constructor. It is dynamic — determined at runtime by the requests flowing through the pipeline.

**Solution:** `Http20EncoderStage` captures the endpoint from the first tagged frame (set by `Request2FrameStage` in TASK-005) and sets it on all emitted `DataItem`s. Since all frames in a substream belong to the same endpoint, the encoder captures once and reuses.

**Depends on:** TASK-005 (pipeline-based endpoint threading)

**Acceptance Criteria:**
- [x] `RequestEndpoint` flows through the HTTP/2 frame pipeline, NOT via constructor injection
- [x] `Http20EncoderStage` captures the endpoint from the first frame in the substream
- [x] Every emitted `DataItem` has `Key = capturedEndpoint`
- [x] Unit tests: Verify Key on DataItem after encoding
- [x] Existing Http20EncoderStage tests stay green
- [x] Build compiles without errors

---

### GROUP C: Code Quality & Async Fixes

---

### TASK-008: Fix CacheStorageStage — Replace Blocking GetAwaiter().GetResult()

**Description:** As a developer, I want `CacheStorageStage` to read the response body asynchronously instead of blocking the Akka thread, to prevent thread pool starvation.

**Context:**
- File: `src/TurboHttp/Streams/Stages/CacheStorageStage.cs`, line 124
- Current: `response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()`
- Problem: Blocks the Akka dispatcher thread, can cause thread pool starvation under load
- Violates CLAUDE.md rule: "Never use `.Result` or `.Wait()`"

**Solution:** Use async pattern with `GetAsyncCallback`:

```csharp
// In the Logic class:
private Action<(HttpResponseMessage response, byte[] body)>? _onBodyRead;

public override void PreStart()
{
    _onBodyRead = GetAsyncCallback<(HttpResponseMessage, byte[])>(tuple =>
    {
        var (resp, body) = tuple;
        var now = DateTimeOffset.UtcNow;
        _stage._store.Put(resp.RequestMessage!, resp, body, now, now);
        Push(_stage._outlet, resp);
    });
}
```

**Alternative (simpler):** Since response bodies in TurboHttp are typically already fully buffered in memory (through DecompressionStage or direct buffer allocation), `ReadAsByteArrayAsync()` is synchronous in practice. A sync fast-path for `ByteArrayContent`/`ReadOnlyMemoryContent` with async fallback could be simpler.

**Acceptance Criteria:**
- [x] No `GetAwaiter().GetResult()` remaining in the file
- [x] Async read using `GetAsyncCallback` or synchronous fast-path when content is already in memory
- [x] Response is only pushed downstream after body has been read
- [x] Unit tests: Cache stores body correctly (async path)
- [x] Unit tests: Cache stores body correctly (sync fast-path, if implemented)
- [x] Existing CacheStorageStage tests stay green
- [x] Build compiles without errors

---

### GROUP D: Verification & Documentation

---

### TASK-009: Verify and Document Stage Ordering — Audit Full Pipeline Sequence

**Description:** As a developer, I want the stage ordering across all three pipeline islands to be verified against RFC semantics and documented with tests, so that future changes don't accidentally reorder stages in a way that breaks correctness.

**Context:**
The current pipeline ordering is:

```
PRE-PROCESSING (shared, fused island 1):
  1. RequestEnricherStage     — applies BaseAddress, DefaultVersion, DefaultHeaders
  2. MergePreferred(redirect) — feedback from RedirectStage
  3. CookieInjectionStage     — RFC 6265 §5.4: inject matching cookies
  4. MergePreferred(retry)    — feedback from RetryStage
  5. CacheLookupStage         — RFC 9111 §4: cache hit → post-processing, miss → engine

ENGINE (per GroupByHostKey substream, fused island 2):
  6. ExtractOptionsStage      — first request → ConnectItem, all → engine
  7. ProtocolEngine BidiFlow   — encode/decode (version-specific)
  8. ConnectionStage           — TCP I/O via actor hierarchy
  9. ConnectionReuseStage      — RFC 9112 §9: evaluate keep-alive/close
  10. DecompressionStage       — RFC 9110 §8.4: gzip/deflate/brotli

POST-PROCESSING (shared, fused island 3):
  11. CookieStorageStage       — RFC 6265 §5.3: store Set-Cookie from response
  12. CacheStorageStage        — RFC 9111 §3: store cacheable 2xx responses
  13. RetryStage               — RFC 9110 §9.2: evaluate idempotent retry
  14. Merge(cache hits)        — merge cache hits from CacheLookupStage
  15. RedirectStage            — RFC 9110 §15.4: evaluate 301/302/303/307/308
```

**Ordering invariants to verify:**

1. **ConnectionReuseStage (9) before CacheStorageStage (12)** — Connection lifecycle decisions must be signalled back to the ConnectionActor before the response enters the shared post-processing island. If cache storage happened first (within the substream), a slow body read could delay connection reuse signalling. Current order: CORRECT.

2. **CookieInjectionStage (3) before CacheLookupStage (5)** — Cookies may be part of the cache Vary key. If cookies weren't injected before cache lookup, the wrong cached entry could be served. Current order: CORRECT.

3. **CookieStorageStage (11) before CacheStorageStage (12)** — Set-Cookie headers from the response must be stored in the CookieJar before the response is cached, so that subsequent requests (including retries/redirects) have up-to-date cookies. Current order: CORRECT.

4. **CacheStorageStage (12) before RetryStage (13)** — Only 2xx responses are cached (line 121), so caching before retry evaluation is safe. 408/503 responses that trigger retries are never cached. Current order: CORRECT but should be documented.

5. **RetryStage (13) before RedirectStage (15)** — Retry should be evaluated first: a 503 with Retry-After should be retried, not redirected. A response can't be both retryable and a redirect (different status code ranges), so the order is technically independent — but retry-first is safer. Current order: CORRECT.

6. **DecompressionStage (10) before CacheStorageStage (12)** — Responses are decompressed before caching. This means cached entries store decompressed bodies. This is a deliberate choice: avoids re-decompression on cache hits. Current order: CORRECT but should be documented as design decision.

7. **Redirect feedback enters at redirectMerge (2) BEFORE CookieInjection (3)** — Redirected requests get fresh cookies for the new URI. Current order: CORRECT.

8. **Retry feedback enters at retryMerge (4) AFTER CookieInjection (3)** — Retried requests reuse the same cookies as the original. This is correct: retry goes to the same URI, cookies haven't changed. Current order: CORRECT.

9. **Redirect feedback enters AFTER RequestEnricherStage (1)** — Redirected requests do NOT get DefaultRequestVersion/DefaultHeaders re-applied. This means the Version from the original request is preserved (fixed in TASK-002). This is intentional: the redirect should preserve the original request's characteristics. Current order: CORRECT but should be documented.

10. **Cache hits from CacheLookupStage (5) merge at step (14) AFTER RetryStage (13)** — Cache hits bypass retry evaluation entirely. This is correct: cached responses don't need retry logic. Current order: CORRECT.

**Solution:**
- Create a `StageOrderingTests` class that verifies key ordering invariants
- Tests should use the test-mode `Engine.CreateFlow()` with mock transport factories
- Verify that stages are wired in the documented order by observing side effects (cookie injection before cache lookup, connection reuse before cache storage, etc.)
- Add XML doc comments to `BuildExtendedPipeline` and `BuildPostProcessGraph` documenting the ordering rationale

**Acceptance Criteria:**
- [ ] All 10 ordering invariants above are verified by tests
- [ ] At least 5 integration-level stream tests covering critical ordering sequences:
  - Cookie injection happens before cache lookup (Vary + Cookie)
  - ConnectionReuseItem is signalled before response enters post-processing
  - Decompressed body is what gets cached (not compressed)
  - Redirect feedback gets fresh cookies for new URI
  - Cache hits bypass retry evaluation
- [ ] XML doc comments on `BuildExtendedPipeline` and `BuildPostProcessGraph` document ordering rationale
- [ ] Test file follows project conventions (NN_StageOrderingTests.cs)
- [ ] All new tests green
- [ ] All existing tests still green
- [ ] Build compiles without errors

---

### GROUP E: Test Coverage

---

### TASK-010: Comprehensive Tests for All Fixes

**Description:** As a developer, I want comprehensive tests ensuring all fixes from TASK-001 through TASK-009 work correctly.

**Test categories:**

1. **Unit tests RetryStage** (TASK-001):
   - Interleaved requests get independent retry budgets
   - Attempt count resets per new request chain
   - Retry-After timer on Request A doesn't corrupt Request B's state

2. **Unit tests RedirectStage + RedirectHandler** (TASK-002):
   - Interleaved requests get independent redirect handlers
   - Request A exhausting redirects doesn't block Request B
   - No false loop detection across independent request chains
   - Version preservation on redirect for all HTTP versions
   - Cross-origin redirect preserves version

3. **Unit tests CookieJar** (TASK-003):
   - Concurrent read + write doesn't throw or corrupt
   - Cookie injection and storage work correctly under contention

4. **Unit tests ConnectionReuseStage** (TASK-004):
   - `ConnectionReuseItem.Key` correct for HTTP/1.0, 1.1, 2.0 responses
   - Fallback for null RequestMessage

5. **Unit tests Http20EncoderStage** (TASK-007):
   - `DataItem.Key` correct after encoding
   - Endpoint captured from pipeline

6. **Unit tests Http20ConnectionStage** (TASK-006):
   - `StreamAcquireItem.Key` correct on HEADERS frame
   - Endpoint captured from pipeline

7. **Unit tests CacheStorageStage** (TASK-008):
   - Async body read works correctly
   - No thread blocking

8. **Integration/stream tests** (TASK-005, TASK-009):
   - End-to-end: HTTP/2 request through engine → all DataItems/StreamAcquireItems have correct key
   - End-to-end: Redirect from HTTP/2 request → new request has Version 2.0
   - Stage ordering invariants verified

**Acceptance Criteria:**
- [ ] At least 20 new tests across all TASK fixes
- [ ] Test files follow project conventions (NN_ThemaTests.cs, sealed class, DisplayName)
- [ ] All new tests green
- [ ] All existing 2111+ tests still green
- [ ] Build compiles without errors

---

## Functional Requirements

- FR-1: `RetryStage` MUST track retry attempt count per-request, NOT globally — each request chain gets its own independent retry budget
- FR-2: `RedirectStage` MUST track redirect state (visited URIs, redirect count) per-request chain, NOT globally — independent request chains MUST NOT interfere with each other's redirect logic
- FR-3: Per-request state for retry and redirect MUST travel with the request through the feedback loop (e.g. via `HttpRequestMessage.Options`)
- FR-4: `RedirectHandler.BuildRedirectRequest()` MUST copy `Version` from the original request
- FR-5: `CookieJar` MUST be thread-safe — it is accessed from different async boundary islands concurrently (pre-processing reads, post-processing writes)
- FR-6: Every `IOutputItem` and `IControlItem` emitted by a stage MUST carry a correct `RequestEndpoint` Key matching the current substream/connection context
- FR-7: `Http20EncoderStage` and `Http20ConnectionStage` MUST capture `RequestEndpoint` from the data flowing through the pipeline, NEVER via constructor injection — the endpoint is dynamic and determined at runtime by the requests
- FR-8: `ConnectionReuseStage` MUST extract `RequestEndpoint` from `response.RequestMessage` (runtime extraction, since the response carries request context)
- FR-9: `CacheStorageStage` MUST NOT use `GetAwaiter().GetResult()`, `.Result`, or `.Wait()`
- FR-10: All fixes MUST be backwards-compatible — no breaking changes to public APIs

## Non-Goals

- No redesign of GroupByHostKey architecture — only threading the endpoint through
- No multi-connection-per-host support in this plan
- No changes to the 3-island topology (Pre-Processing, Engine, Post-Processing)
- No new features — only correctness fixes and their tests
- No refactoring of stages that work correctly (e.g. Http1XCorrelationStage, Http20StreamStage)

## Technical Considerations

### Pipeline Topology (for reference)

```
Pre-Processing (shared):
  RequestEnricher → redirectMerge → CookieInjection → retryMerge → CacheLookup

Engine (per GroupByHostKey substream):
  ExtractOptions → [ProtocolEngine BidiFlow] → ConnectionStage → ConnectionReuse
  ↕ (feedback: ConnectionReuseItem → transportMerge → ConnectionStage)

Post-Processing (shared):
  CookieStorage → CacheStorage → Retry → Merge(cache hits) → Redirect → Output
  ↕ (feedback: Retry → retryMerge, Redirect → redirectMerge)
```

### Substream Isolation & Dynamic Endpoint Propagation

Each GroupByHostKey substream has a fixed `RequestEndpoint`. This means:
- Stages within a substream process ONLY requests/responses for ONE endpoint
- The endpoint is determined dynamically at runtime by the first request flowing through
- **Constructor injection of RequestEndpoint is NOT allowed** — even though each substream has a fixed endpoint, the architecture must derive it from the data flowing through the pipeline, not from build-time parameters
- This ensures correctness even if substream boundaries change, stages are reused in different contexts, or the GroupByHostKey implementation evolves
- Stages should capture the endpoint from the first element they see (first request, first frame, first response) and reuse it for the lifetime of the substream

### Task Dependencies

```
GROUP A (Critical bugs — no dependencies, do first):
  TASK-001 (RetryStage)     — independent
  TASK-002 (RedirectStage)  — independent
  TASK-003 (CookieJar)      — independent

GROUP B (RequestEndpoint — ordered by dependency):
  TASK-004 (ConnectionReuse)      — independent
  TASK-005 (Http2Frame threading) — independent
  TASK-006 (Http20Connection)     — depends on TASK-005
  TASK-007 (Http20Encoder)        — depends on TASK-005

GROUP C (Code quality):
  TASK-008 (CacheStorage async) — independent

GROUP D (Verification):
  TASK-009 (Stage ordering) — independent

GROUP E (Tests):
  TASK-010 (All tests) — depends on all above
```

**Execution order:**
1. TASK-001 + TASK-002 + TASK-003 (Group A — parallel, critical bugs)
2. TASK-004 + TASK-005 (Group B start — parallel, independent)
3. TASK-006 + TASK-007 (Group B finish — parallel, depend on TASK-005)
4. TASK-008 (Group C — independent)
5. TASK-009 (Group D — independent, can overlap with Group C)
6. TASK-010 (Group E — tests for everything)

## Success Metrics

- 0 places in code where `RequestEndpoint.Default` is used as placeholder (except in `RequestEndpoint.cs` itself)
- 0 places where `IOutputItem`/`IControlItem` is emitted without correct Key
- 0 blocking calls (`GetAwaiter().GetResult()`, `.Result`, `.Wait()`) in stage code
- All 2111+ existing tests green
- At least 20 new tests for the fixes
- `RetryStage` and `RedirectStage` maintain independent state per request chain — verified by interleaved request tests
- `CookieJar` is thread-safe — verified by concurrent access tests
- `RedirectHandler` sets `Version` correctly — verified by tests with HTTP/1.0, 1.1, 2.0
- Stage ordering invariants documented and tested

## Open Questions

1. **MaxConcurrentStreamsItem Key:** Should `MaxConcurrentStreamsItem` also get a `RequestEndpoint` Key? Currently it only has `MaxStreams`. It's an `IControlItem` — consistency argues for it, but immediate need is unclear.

2. **Http10/Http11 EncoderStage Consistency:** These stages already extract the Key via `RequestEndpoint.FromRequest(request)` (runtime extraction from the request). This is the correct pattern — they derive the endpoint from the data flowing through. No changes needed for HTTP/1.x.

3. **CacheStorageStage async complexity:** The async pattern with `GetAsyncCallback` requires state management (pending response + pending body). Is the simpler approach (sync fast-path for ByteArrayContent/ReadOnlyMemoryContent) acceptable as a first iteration?

4. **Version policy on redirect:** Should cross-scheme redirects (http → https or vice versa) preserve the version, or should `RequestEnricherStage` apply DefaultRequestVersion? Currently the redirect request enters the pipeline at `redirectMerge` (AFTER `RequestEnricherStage`), so `DefaultRequestVersion` is NOT re-applied. This means the version from the original request is the only source.
