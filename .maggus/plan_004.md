# Plan: Consistent Inlet/Outlet Naming Convention

## Introduction

TurboHttp has 26 GraphStage implementations with 6 different naming patterns for Inlet/Outlet string names and inconsistent C# field names. This creates confusion when debugging Akka.Streams graphs, causes name collisions (Http1X and Http20 CorrelationStage share identical names), and includes a bug where `GroupByHostKeyStage` has the same name for both inlet and outlet. This plan establishes a unified PascalCase naming convention aligned with C# idioms, fixes the bug, resolves collisions, and renames all 26 stages systematically.

## Goals

- Establish a single, consistent naming convention for all Inlet/Outlet string names: `StageName.Direction` (PascalCase)
- Unify C# field names: `_in`/`_out` for FlowShape, `_inRole`/`_outRole` for multi-port
- Fix the `GroupByHostKeyStage` duplicate name bug
- Resolve the CorrelationStage name collision
- Zero test regressions after renaming
- Document the convention so future stages follow it

## Naming Convention

### String Names (Akka debug names)

**Pattern:** `StageName.Direction` or `StageName.Direction.Role`

| Shape Type | Pattern | Example |
|-----------|---------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` / `StageName.Out` | `"Http11Encoder.In"`, `"Http11Encoder.Out"` |
| FanOutShape (1 in, 2+ out) | `StageName.In` / `StageName.Out.Role` | `"Redirect.In"`, `"Redirect.Out.Final"`, `"Redirect.Out.Redirect"` |
| FanInShape (2+ in, 1 out) | `StageName.In.Role` / `StageName.Out` | `"H2Correlation.In.Request"`, `"H2Correlation.Out"` |
| Custom multi-port | `StageName.In.Role` / `StageName.Out.Role` | `"H2Connection.In.Server"`, `"H2Connection.Out.Stream"` |

**Rules:**
- PascalCase throughout — matches C# idiom
- No protocol prefix — stage name is sufficient (e.g., `Http11Encoder` already contains `Http11`)
- Stage name is abbreviated where the full class name is too long (drop `Stage` suffix)
- Role names are semantic: `Request`, `Response`, `Final`, `Retry`, `Redirect`, `Signal`, `Miss`, `Hit`, `Server`, `Stream`

### C# Field Names

| Shape Type | Inlet Fields | Outlet Fields |
|-----------|-------------|--------------|
| FlowShape (1 in, 1 out) | `_in` | `_out` |
| FanOutShape (1 in, 2+ out) | `_in` | `_outRole` (e.g., `_outFinal`, `_outSignal`) |
| FanInShape (2+ in, 1 out) | `_inRole` (e.g., `_inRequest`) | `_out` |
| Custom multi-port | `_inRole` | `_outRole` |

**Rules:**
- Always prefixed with `_` (private field convention)
- camelCase after `_in`/`_out` prefix
- Role matches the string name role (e.g., `_outFinal` → `"Redirect.Out.Final"`)

## Full Rename Matrix

### FlowShape Stages (18 stages)

| # | Stage | Old String In | New String In | Old String Out | New String Out | Old Field In | New Field In | Old Field Out | New Field Out |
|---|-------|--------------|---------------|---------------|----------------|-------------|-------------|--------------|--------------|
| 1 | Http10EncoderStage | `http10.encoder.in` | `Http10Encoder.In` | `http10.encoder.out` | `Http10Encoder.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 2 | Http10DecoderStage | `http10.decoder.in` | `Http10Decoder.In` | `http10.decoder.out` | `Http10Decoder.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 3 | Http11EncoderStage | `http11.encoder.in` | `Http11Encoder.In` | `http11.encoder.out` | `Http11Encoder.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 4 | Http11DecoderStage | `http11.decoder.in` | `Http11Decoder.In` | `http11.decoder.out` | `Http11Decoder.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 5 | Http20EncoderStage | `frameEncoder.in` | `Http20Encoder.In` | `frameEncoder.out` | `Http20Encoder.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 6 | Http20DecoderStage | `http20.tcp.in` | `Http20Decoder.In` | `http20.frame.out` | `Http20Decoder.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 7 | Http20StreamStage | `h2.stream.in` | `H2Stream.In` | `h2.stream.out` | `H2Stream.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 8 | Request2FrameStage | `req.in` | `Request2Frame.In` | `req.out` | `Request2Frame.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 9 | StreamIdAllocatorStage | `allocator.in` | `StreamIdAllocator.In` | `allocator.out` | `StreamIdAllocator.Out` | `_in` | `_in` ✓ | `_out` | `_out` ✓ |
| 10 | PrependPrefaceStage | `preface.in` | `PrependPreface.In` | `preface.out` | `PrependPreface.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 11 | RequestEnricherStage | `enricher.in` | `RequestEnricher.In` | `enricher.out` | `RequestEnricher.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 12 | DecompressionStage | `decompression.in` | `Decompression.In` | `decompression.out` | `Decompression.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 13 | CacheStorageStage | `cache.storage.in` | `CacheStorage.In` | `cache.storage.out` | `CacheStorage.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 14 | CookieInjectionStage | `cookie.injection.in` | `CookieInjection.In` | `cookie.injection.out` | `CookieInjection.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 15 | CookieStorageStage | `cookie.storage.in` | `CookieStorage.In` | `cookie.storage.out` | `CookieStorage.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 16 | ConnectionStage | `connection.in` | `Connection.In` | `connection.out` | `Connection.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 17 | GroupByHostKeyStage | `groupby.hostkey.in` | `GroupByHostKey.In` | `groupby.hostkey.in` **BUG** | `GroupByHostKey.Out` | `_inlet` | `_in` | `_outlet` | `_out` |
| 18 | MergeSubstreamsStage | `merge.substreams.in` | `MergeSubstreams.In` | `merge.substreams.out` | `MergeSubstreams.Out` | `_inlet` | `_in` | `_outlet` | `_out` |

### FanOutShape Stages (5 stages)

| # | Stage | Port | Old String | New String | Old Field | New Field |
|---|-------|------|-----------|------------|-----------|-----------|
| 19 | CacheLookupStage | In | `cache.lookup.in` | `CacheLookup.In` | `_in` | `_in` ✓ |
| | | Out Miss | `cache.lookup.out0.miss` | `CacheLookup.Out.Miss` | `_outMiss` | `_outMiss` ✓ |
| | | Out Hit | `cache.lookup.out1.hit` | `CacheLookup.Out.Hit` | `_outHit` | `_outHit` ✓ |
| 20 | ConnectionReuseStage | In | `connection.reuse.in.response` | `ConnectionReuse.In` | `_inletResponse` | `_in` |
| | | Out Response | `connection.reuse.out.response` | `ConnectionReuse.Out.Response` | `_outletResponse` | `_outResponse` |
| | | Out Signal | `connection.reuse.out.signal` | `ConnectionReuse.Out.Signal` | `_outletSignalItem` | `_outSignal` |
| 21 | ExtractOptionsStage | In | `options.in.request` | `ExtractOptions.In` | `_inletRequest` | `_in` |
| | | Out Request | `options.out.request` | `ExtractOptions.Out.Request` | `_outletRequest` | `_outRequest` |
| | | Out Signal | `options.out.signal` | `ExtractOptions.Out.Signal` | `_outletSignal` | `_outSignal` |
| 22 | RedirectStage | In | `redirect.in` | `Redirect.In` | `_in` | `_in` ✓ |
| | | Out Final | `redirect.out0.final` | `Redirect.Out.Final` | `_outFinal` | `_outFinal` ✓ |
| | | Out Redirect | `redirect.out1.redirect` | `Redirect.Out.Redirect` | `_outRedirect` | `_outRedirect` ✓ |
| 23 | RetryStage | In | `retry.in` | `Retry.In` | `_in` | `_in` ✓ |
| | | Out Final | `retry.out0.final` | `Retry.Out.Final` | `_outFinal` | `_outFinal` ✓ |
| | | Out Retry | `retry.out1.retry` | `Retry.Out.Retry` | `_outRetry` | `_outRetry` ✓ |

### FanInShape Stage (1 stage)

| # | Stage | Port | Old String | New String | Old Field | New Field |
|---|-------|------|-----------|------------|-----------|-----------|
| 24 | Http20CorrelationStage | In Request | `correlation.request.in` | `H2Correlation.In.Request` | `_requestIn` | `_inRequest` |
| | | In Response | `correlation.response.in` | `H2Correlation.In.Response` | `_responseIn` | `_inResponse` |
| | | Out | `correlation.out` | `H2Correlation.Out` | `_out` | `_out` ✓ |

### Custom Shape Stages (2 stages)

| # | Stage | Port | Old String | New String | Old Field | New Field |
|---|-------|------|-----------|------------|-----------|-----------|
| 25 | Http1XCorrelationStage | In Request | `correlation.request.in` **COLLISION** | `H1XCorrelation.In.Request` | `_requestIn` | `_inRequest` |
| | | In Response | `correlation.response.in` **COLLISION** | `H1XCorrelation.In.Response` | `_responseIn` | `_inResponse` |
| | | Out | `correlation.out` **COLLISION** | `H1XCorrelation.Out` | `_out` | `_out` ✓ |
| | | Out Signal | `correlation.signal.out` | `H1XCorrelation.Out.Signal` | `_outletSignal` | `_outSignal` |
| 26 | Http20ConnectionStage | In Server | `h2.server.in` | `H2Connection.In.Server` | `_inletRaw` | `_inServer` |
| | | In App | `h2.app.in` | `H2Connection.In.App` | `_inletRequest` | `_inApp` |
| | | Out Stream | `h2.app.out` | `H2Connection.Out.Stream` | `_outletStream` | `_outStream` |
| | | Out Server | `h2.server.out` | `H2Connection.Out.Server` | `_outletRaw` | `_outServer` |
| | | Out Signal | `h2.signal.out` | `H2Connection.Out.Signal` | `_outletSignal` | `_outSignal` |

## User Stories

---

### TASK-001: Fix GroupByHostKeyStage Duplicate Name Bug
**Description:** As a developer, I want the GroupByHostKeyStage outlet to have a unique name so that Akka graph visualization and debugging work correctly.

**File:** `src/TurboHttp/Internal/Stages/GroupByHostKeyStage.cs`

**Current Bug:** Both inlet and outlet use `"groupby.hostkey.in"`.

**Required Change:** Rename outlet to `"GroupByHostKey.Out"` and inlet to `"GroupByHostKey.In"`.

**Acceptance Criteria:**
- [x] Inlet name is `"GroupByHostKey.In"`, outlet name is `"GroupByHostKey.Out"`
- [x] Field names changed to `_in` / `_out`
- [x] All references to old field names updated in the Logic class
- [x] All existing tests pass
- [x] Build compiles with 0 errors

---

### TASK-002: Rename Http10EncoderStage and Http10DecoderStage
**Description:** As a developer, I want HTTP/1.0 encoder/decoder stages to follow the new naming convention.

**Files:**
- `src/TurboHttp/Streams/Stages/Http10EncoderStage.cs`
- `src/TurboHttp/Streams/Stages/Http10DecoderStage.cs`

**Changes:**
| Stage | Old | New |
|-------|-----|-----|
| Http10EncoderStage | `"http10.encoder.in"` / `"http10.encoder.out"` | `"Http10Encoder.In"` / `"Http10Encoder.Out"` |
| Http10DecoderStage | `"http10.decoder.in"` / `"http10.decoder.out"` | `"Http10Decoder.In"` / `"Http10Decoder.Out"` |

Fields: `_inlet`/`_outlet` → `_in`/`_out`

**Acceptance Criteria:**
- [x] String names updated to PascalCase convention
- [x] Field names `_inlet`/`_outlet` → `_in`/`_out`
- [x] All references in Logic class updated
- [x] All existing RFC1945 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-003: Rename Http11EncoderStage and Http11DecoderStage
**Description:** As a developer, I want HTTP/1.1 encoder/decoder stages to follow the new naming convention.

**Files:**
- `src/TurboHttp/Streams/Stages/Http11EncoderStage.cs`
- `src/TurboHttp/Streams/Stages/Http11DecoderStage.cs`

**Changes:**
| Stage | Old | New |
|-------|-----|-----|
| Http11EncoderStage | `"http11.encoder.in"` / `"http11.encoder.out"` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| Http11DecoderStage | `"http11.decoder.in"` / `"http11.decoder.out"` | `"Http11Decoder.In"` / `"Http11Decoder.Out"` |

Fields: `_inlet`/`_outlet` → `_in`/`_out`

**Acceptance Criteria:**
- [x] String names updated
- [x] Field names updated
- [x] All existing RFC9112 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-004: Rename Http20EncoderStage and Http20DecoderStage
**Description:** As a developer, I want HTTP/2 encoder/decoder stages to follow the new naming convention. These are the most inconsistently named currently (`frameEncoder` vs `http20.tcp`).

**Files:**
- `src/TurboHttp/Streams/Stages/Http20EncoderStage.cs`
- `src/TurboHttp/Streams/Stages/Http20DecoderStage.cs`

**Changes:**
| Stage | Old | New |
|-------|-----|-----|
| Http20EncoderStage | `"frameEncoder.in"` / `"frameEncoder.out"` | `"Http20Encoder.In"` / `"Http20Encoder.Out"` |
| Http20DecoderStage | `"http20.tcp.in"` / `"http20.frame.out"` | `"Http20Decoder.In"` / `"Http20Decoder.Out"` |

Fields: `_inlet`/`_outlet` → `_in`/`_out`

**Acceptance Criteria:**
- [x] String names unified to match Http10/Http11 pattern
- [x] Field names updated
- [x] All existing RFC9113 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-005: Rename Http20StreamStage, Request2FrameStage, StreamIdAllocatorStage, PrependPrefaceStage
**Description:** As a developer, I want the remaining HTTP/2 pipeline stages to follow the convention.

**Files:**
- `src/TurboHttp/Streams/Stages/Http20StreamStage.cs`
- `src/TurboHttp/Streams/Stages/Request2FrameStage.cs`
- `src/TurboHttp/Streams/Stages/StreamIdAllocatorStage.cs`
- `src/TurboHttp/Streams/Stages/PrependPrefaceStage.cs`

**Changes:**
| Stage | Old In | New In | Old Out | New Out |
|-------|--------|--------|---------|---------|
| Http20StreamStage | `h2.stream.in` | `H2Stream.In` | `h2.stream.out` | `H2Stream.Out` |
| Request2FrameStage | `req.in` | `Request2Frame.In` | `req.out` | `Request2Frame.Out` |
| StreamIdAllocatorStage | `allocator.in` | `StreamIdAllocator.In` | `allocator.out` | `StreamIdAllocator.Out` |
| PrependPrefaceStage | `preface.in` | `PrependPreface.In` | `preface.out` | `PrependPreface.Out` |

Fields: `_inlet`/`_outlet` → `_in`/`_out` (StreamIdAllocatorStage already uses `_in`/`_out` ✓)

**Acceptance Criteria:**
- [x] All 4 stages renamed
- [x] Field names unified
- [x] All existing HTTP/2 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-006: Rename RequestEnricherStage, DecompressionStage, ConnectionStage
**Description:** As a developer, I want the pipeline infrastructure stages to follow the convention.

**Files:**
- `src/TurboHttp/Streams/Stages/RequestEnricherStage.cs`
- `src/TurboHttp/Streams/Stages/DecompressionStage.cs`
- `src/TurboHttp/IO/Stages/ConnectionStage.cs`

**Changes:**
| Stage | Old In | New In | Old Out | New Out |
|-------|--------|--------|---------|---------|
| RequestEnricherStage | `enricher.in` | `RequestEnricher.In` | `enricher.out` | `RequestEnricher.Out` |
| DecompressionStage | `decompression.in` | `Decompression.In` | `decompression.out` | `Decompression.Out` |
| ConnectionStage | `connection.in` | `Connection.In` | `connection.out` | `Connection.Out` |

Fields: `_inlet`/`_outlet` → `_in`/`_out`

**Acceptance Criteria:**
- [x] All 3 stages renamed
- [x] Field names unified
- [x] All existing stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-007: Rename Cookie Stages and Cache Storage Stage
**Description:** As a developer, I want cookie and cache storage stages to follow the convention.

**Files:**
- `src/TurboHttp/Streams/Stages/CookieInjectionStage.cs`
- `src/TurboHttp/Streams/Stages/CookieStorageStage.cs`
- `src/TurboHttp/Streams/Stages/CacheStorageStage.cs`

**Changes:**
| Stage | Old In | New In | Old Out | New Out |
|-------|--------|--------|---------|---------|
| CookieInjectionStage | `cookie.injection.in` | `CookieInjection.In` | `cookie.injection.out` | `CookieInjection.Out` |
| CookieStorageStage | `cookie.storage.in` | `CookieStorage.In` | `cookie.storage.out` | `CookieStorage.Out` |
| CacheStorageStage | `cache.storage.in` | `CacheStorage.In` | `cache.storage.out` | `CacheStorage.Out` |

Fields: `_inlet`/`_outlet` → `_in`/`_out`

**Acceptance Criteria:**
- [x] All 3 stages renamed
- [x] Field names unified
- [x] All existing RFC6265/RFC9111 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-008: Rename MergeSubstreamsStage
**Description:** As a developer, I want the internal merge stage to follow the convention.

**File:** `src/TurboHttp/Internal/Stages/MergeSubstreamsStage.cs`

**Changes:** `"merge.substreams.in"` → `"MergeSubstreams.In"`, `"merge.substreams.out"` → `"MergeSubstreams.Out"`, `_inlet`/`_outlet` → `_in`/`_out`

**Acceptance Criteria:**
- [x] String names and field names updated
- [x] All existing stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-009: Rename CacheLookupStage (FanOut)
**Description:** As a developer, I want the cache lookup fan-out stage to follow the convention.

**File:** `src/TurboHttp/Streams/Stages/CacheLookupStage.cs`

**Changes:**
| Port | Old | New | Old Field | New Field |
|------|-----|-----|-----------|-----------|
| In | `cache.lookup.in` | `CacheLookup.In` | `_in` ✓ | `_in` ✓ |
| Out Miss | `cache.lookup.out0.miss` | `CacheLookup.Out.Miss` | `_outMiss` ✓ | `_outMiss` ✓ |
| Out Hit | `cache.lookup.out1.hit` | `CacheLookup.Out.Hit` | `_outHit` ✓ | `_outHit` ✓ |

**Acceptance Criteria:**
- [x] String names updated (fields already correct)
- [x] All existing RFC9111 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-010: Rename ConnectionReuseStage (FanOut)
**Description:** As a developer, I want the connection reuse fan-out stage to follow the convention.

**File:** `src/TurboHttp/Streams/Stages/ConnectionReuseStage.cs`

**Changes:**
| Port | Old | New | Old Field | New Field |
|------|-----|-----|-----------|-----------|
| In | `connection.reuse.in.response` | `ConnectionReuse.In` | `_inletResponse` | `_in` |
| Out Response | `connection.reuse.out.response` | `ConnectionReuse.Out.Response` | `_outletResponse` | `_outResponse` |
| Out Signal | `connection.reuse.out.signal` | `ConnectionReuse.Out.Signal` | `_outletSignalItem` | `_outSignal` |

**Acceptance Criteria:**
- [x] String names and field names updated
- [x] All existing stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-011: Rename ExtractOptionsStage (FanOut)
**Description:** As a developer, I want the options extraction stage to follow the convention.

**File:** `src/TurboHttp/Streams/Stages/ExtractOptionsStage.cs`

**Changes:**
| Port | Old | New | Old Field | New Field |
|------|-----|-----|-----------|-----------|
| In | `options.in.request` | `ExtractOptions.In` | `_inletRequest` | `_in` |
| Out Request | `options.out.request` | `ExtractOptions.Out.Request` | `_outletRequest` | `_outRequest` |
| Out Signal | `options.out.signal` | `ExtractOptions.Out.Signal` | `_outletSignal` | `_outSignal` |

**Acceptance Criteria:**
- [x] String names and field names updated
- [x] All existing stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-012: Rename RedirectStage and RetryStage (FanOut)
**Description:** As a developer, I want the redirect and retry fan-out stages to follow the convention. These stages already have good field names — only string names need PascalCase.

**Files:**
- `src/TurboHttp/Streams/Stages/RedirectStage.cs`
- `src/TurboHttp/Streams/Stages/RetryStage.cs`

**Changes:**
| Stage | Port | Old | New |
|-------|------|-----|-----|
| RedirectStage | In | `redirect.in` | `Redirect.In` |
| | Out Final | `redirect.out0.final` | `Redirect.Out.Final` |
| | Out Redirect | `redirect.out1.redirect` | `Redirect.Out.Redirect` |
| RetryStage | In | `retry.in` | `Retry.In` |
| | Out Final | `retry.out0.final` | `Retry.Out.Final` |
| | Out Retry | `retry.out1.retry` | `Retry.Out.Retry` |

Fields: already `_in`/`_outFinal`/`_outRedirect` and `_outRetry` ✓ — no field changes needed.

**Acceptance Criteria:**
- [x] String names updated to PascalCase
- [x] All existing RFC9110 stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-013: Rename Http1XCorrelationStage (Custom Shape — fixes collision)
**Description:** As a developer, I want the HTTP/1.x correlation stage to have unique names so that it doesn't collide with the HTTP/2 correlation stage.

**File:** `src/TurboHttp/Streams/Stages/Http1XCorrelationStage.cs`

**Changes:**
| Port | Old (COLLISION) | New | Old Field | New Field |
|------|----------------|-----|-----------|-----------|
| In Request | `correlation.request.in` | `H1XCorrelation.In.Request` | `_requestIn` | `_inRequest` |
| In Response | `correlation.response.in` | `H1XCorrelation.In.Response` | `_responseIn` | `_inResponse` |
| Out | `correlation.out` | `H1XCorrelation.Out` | `_out` ✓ | `_out` ✓ |
| Out Signal | `correlation.signal.out` | `H1XCorrelation.Out.Signal` | `_outletSignal` | `_outSignal` |

Also update the custom `Http1XCorrelationShape` class if it references port names.

**Acceptance Criteria:**
- [x] No name collision with Http20CorrelationStage
- [x] String names and field names updated
- [x] Custom shape class updated
- [x] All existing HTTP/1.x correlation stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-014: Rename Http20CorrelationStage (FanIn — fixes collision)
**Description:** As a developer, I want the HTTP/2 correlation stage to have unique names.

**File:** `src/TurboHttp/Streams/Stages/Http20CorrelationStage.cs`

**Changes:**
| Port | Old (COLLISION) | New | Old Field | New Field |
|------|----------------|-----|-----------|-----------|
| In Request | `correlation.request.in` | `H2Correlation.In.Request` | `_requestIn` | `_inRequest` |
| In Response | `correlation.response.in` | `H2Correlation.In.Response` | `_responseIn` | `_inResponse` |
| Out | `correlation.out` | `H2Correlation.Out` | `_out` ✓ | `_out` ✓ |

**Acceptance Criteria:**
- [x] No name collision with Http1XCorrelationStage
- [x] String names and field names updated
- [x] All existing HTTP/2 correlation stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-015: Rename Http20ConnectionStage (Custom Shape)
**Description:** As a developer, I want the HTTP/2 connection stage to follow the convention. This is the most complex stage with 5 ports.

**File:** `src/TurboHttp/Streams/Stages/Http20ConnectionStage.cs`

**Changes:**
| Port | Old | New | Old Field | New Field |
|------|-----|-----|-----------|-----------|
| In Server | `h2.server.in` | `H2Connection.In.Server` | `_inletRaw` | `_inServer` |
| In App | `h2.app.in` | `H2Connection.In.App` | `_inletRequest` | `_inApp` |
| Out Stream | `h2.app.out` | `H2Connection.Out.Stream` | `_outletStream` | `_outStream` |
| Out Server | `h2.server.out` | `H2Connection.Out.Server` | `_outletRaw` | `_outServer` |
| Out Signal | `h2.signal.out` | `H2Connection.Out.Signal` | `_outletSignal` | `_outSignal` |

Also update the custom `Http20ConnectionShape` class.

**Acceptance Criteria:**
- [x] All 5 ports renamed (string + field)
- [x] Custom shape class updated
- [x] All existing HTTP/2 connection stream tests pass
- [x] Build compiles with 0 errors

---

### TASK-016: Update Stream Tests That Reference Port Names
**Description:** As a developer, I want all stream tests that reference inlet/outlet names or field names to be updated so they compile and pass.

**Scope:** Search all test projects for:
1. String references to old port names (e.g., `"http10.encoder.in"`)
2. Field references via reflection or direct access (e.g., `.stage._inlet`)
3. Custom shape property accesses

**Acceptance Criteria:**
- [x] `grep` for ALL old string names — zero matches in test code
- [x] `grep` for ALL old field names (`_inlet`, `_outlet`, `_inletRaw`, `_outletSignal`, etc.) — zero matches referencing stage fields
- [x] `dotnet test ./src/TurboHttp.sln` — all tests pass (626 stream tests; 10 pre-existing TurboHttp.Tests failures unrelated to this task)
- [x] Build compiles with 0 errors

---

### TASK-017: Update Engine.cs Graph Wiring
**Description:** As a developer, I want the Engine graph builder to use the new field names when wiring stages together.

**File:** `src/TurboHttp/Streams/Engine.cs`

**Scope:** The Engine builds the full pipeline graph and references stage inlets/outlets by field name. Update all references.

**Acceptance Criteria:**
- [ ] All stage field references in Engine.cs use new names
- [ ] All Http10Engine, Http11Engine, Http20Engine files updated
- [ ] `dotnet build` succeeds
- [ ] All stream tests pass

---

### TASK-018: Document Naming Convention in CLAUDE.md
**Description:** As a developer, I want the naming convention documented so that future stages follow it automatically.

**Required Change:** Add a "Stage Naming Convention" section to CLAUDE.md under "Key Patterns":

```markdown
### Stage Inlet/Outlet Naming
- String names: `StageName.In`, `StageName.Out`, `StageName.Out.Role` (PascalCase)
- Field names: `_in`/`_out` for FlowShape, `_inRole`/`_outRole` for multi-port
- No protocol prefix in names — stage class name already contains it
- Role names: Request, Response, Final, Retry, Redirect, Signal, Miss, Hit, Server, Stream, App
```

**Acceptance Criteria:**
- [ ] Convention documented in CLAUDE.md
- [ ] Includes examples for FlowShape, FanOutShape, FanInShape, and custom shapes
- [ ] Build compiles with 0 errors

---

## Functional Requirements

- FR-1: Every Inlet/Outlet string name must follow `StageName.Direction` or `StageName.Direction.Role` pattern in PascalCase
- FR-2: Every FlowShape stage must use `_in`/`_out` as field names
- FR-3: Every multi-port stage must use `_inRole`/`_outRole` as field names
- FR-4: No two stages may share the same Inlet or Outlet string name
- FR-5: The `GroupByHostKeyStage` outlet bug must be fixed (inlet and outlet must have different names)
- FR-6: All custom Shape classes must be updated to match new field/port names
- FR-7: All graph wiring in Engine files must be updated to use new field names
- FR-8: All test references must be updated

## Non-Goals

- No changes to stage behavior or logic
- No changes to graph topology or pipeline architecture
- No changes to Shape types (FlowShape stays FlowShape, etc.)
- No renaming of stage class names themselves (only port names)
- No new stages or removal of existing stages

## Technical Considerations

- **Rename safety:** Use IDE rename refactoring (or `replace_all`) for field renames to catch all references. String name changes are safe — they only affect Akka debug output, not behavior.
- **Custom Shape classes:** `Http1XCorrelationShape` and `Http20ConnectionShape` expose ports as properties. These properties must be updated to match new field names.
- **Engine wiring:** `Engine.cs` and the protocol engines (`Http10Engine`, `Http11Engine`, `Http20Engine`) wire stages together using `builder.From(stage._inlet)` patterns. Every field reference must be updated.
- **Test references:** Stream tests may access stage fields directly (e.g., `stage._inlet` in test setup). Search both `TurboHttp.StreamTests` and `TurboHttp.Tests` for references.
- **No behavioral impact:** Inlet/Outlet names are only used for Akka debug logging and graph visualization. Renaming them has zero runtime behavioral impact — only the debug output changes.

## Success Metrics

- All 26 stages follow the same naming convention
- Zero name collisions across all stages
- `GroupByHostKeyStage` bug fixed
- All 2,111+ existing tests pass
- `dotnet build` produces 0 errors
- Naming convention documented in CLAUDE.md

## Open Questions

- Should the convention be enforced via a Roslyn analyzer or code review checklist?
- Should we add a unit test that validates all Inlet/Outlet names follow the convention via reflection?
