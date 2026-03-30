<!-- maggus-id: 20250325-140000-feature-023 -->

# Feature 023: Integration Test Depth – Interactions, Resilience, Request Compression & Handlers

## Introduction

Feature 021 brings all HTTP versions to the same **breadth** (each feature category × each version). Feature 023 adds the **depth**: How do BidiStages work together? What happens with broken responses? Does request compression work end-to-end? Can custom handlers extend the pipeline?

Currently, all 8 BidiStages are tested only in isolation. In the production pipeline they are stacked:
```
Tracing → Handler → Redirect → Cookie → Retry → ExpectContinue → Cache → ContentEncoding → Engine
```
Interactions between stages (e.g. Cookie + Redirect, Cache + Compression) are not covered. Likewise missing are tests for malformed server responses (resilience), request body compression, and the handler pipeline.

### Architecture Context

- **Components involved:** `src/TurboHttp.IntegrationTests/` (test files), `Shared/Routes.cs` (server routes), `Shared/ClientHelper.cs` (client factory with `configure` callback), `TurboHttpClientBuilderExtensions.cs` (Builder API)
- **New patterns:** `ClientHelper.CreateClient(port, version, configure: builder => ...)` is used intensively for the first time to configure RequestCompression, ExpectContinue, and custom handlers
- **Missing Builder API:** `WithRequestCompression(policy?)` and `WithExpectContinue(policy?)` are missing as extension methods – must be added
- **All 4 versions:** HTTP/1.0, HTTP/1.1, HTTP/2, TLS (per user decision)

## Goals

- Verify that BidiStage interactions (6+ combinations) work correctly across all HTTP versions
- Ensure that the client handles broken server responses gracefully (8+ resilience scenarios)
- End-to-end validation of request body compression (gzip/deflate/br) across all versions
- Verify custom handler pipeline (UseRequest/UseResponse/AddHandler) end-to-end
- Add `WithRequestCompression()` and `WithExpectContinue()` builder extensions

## Tasks

### TASK-023-001: Builder Extensions + Resilience Routes
**Description:** As a developer I want `WithRequestCompression()` / `WithExpectContinue()` builder extensions and new server routes for resilience tests so that subsequent tasks can configure their tests.

**Token Estimate:** ~40k tokens
**Predecessors:** none
**Successors:** TASK-023-004, TASK-023-005, TASK-023-006, TASK-023-007
**Parallel:** yes – can run alongside TASK-023-002, TASK-023-003, TASK-023-008

**Builder Extensions (in `TurboHttpClientBuilderExtensions.cs`):**
```csharp
public static ITurboHttpClientBuilder WithRequestCompression(
    this ITurboHttpClientBuilder builder, RequestCompressionPolicy? policy = null)
{
    builder.Services.Configure<TurboClientDescriptor>(builder.Name,
        d => { d.RequestCompressionPolicy = policy ?? RequestCompressionPolicy.Default; });
    return builder;
}

public static ITurboHttpClientBuilder WithExpectContinue(
    this ITurboHttpClientBuilder builder, Expect100Policy? policy = null)
{
    builder.Services.Configure<TurboClientDescriptor>(builder.Name,
        d => { d.Expect100Policy = policy ?? Expect100Policy.Default; });
    return builder;
}
```

**New Routes in `Routes.cs` → `RegisterResilienceRoutes()`:**
- `GET /resilience/content-length-mismatch` → `Content-Length: 1000` but sends only 500 bytes, then closes
- `GET /resilience/corrupt-gzip` → `Content-Encoding: gzip` but body is random bytes
- `GET /resilience/corrupt-br` → `Content-Encoding: br` but body is random bytes
- `GET /resilience/truncated-body/{kb}` → sends `kb` KB header, stops at 50% of body
- `GET /resilience/slow-headers/{ms}` → delay `ms` before sending headers
- `GET /resilience/slow-body/{ms}` → sends first half of body, delays `ms`, then sends rest
- `GET /resilience/invalid-header` → response with header containing invalid characters
- `GET /resilience/empty-response` → closes connection immediately (no status line)

**New Routes → `RegisterRequestCompressionRoutes()`:**
- `POST /compress/echo` → reads request body, echoes back uncompressed + returns `X-Content-Encoding: {received Content-Encoding}` header to prove compression was received
- `POST /compress/verify-gzip` → verifies body is valid gzip, decompresses, echoes
- `POST /compress/verify-deflate` → verifies body is valid deflate
- `POST /compress/verify-br` → verifies body is valid brotli

**Acceptance Criteria:**
- [ ] `WithRequestCompression()` and `WithExpectContinue()` added to `TurboHttpClientBuilderExtensions.cs`
- [ ] `Routes.RegisterResilienceRoutes()` with 8 routes added
- [ ] `Routes.RegisterRequestCompressionRoutes()` with 4 routes added
- [ ] All fixtures call both new registration methods
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` passes with zero warnings
- [ ] Existing tests still pass

**Files:**
- `src/TurboHttp/TurboHttpClientBuilderExtensions.cs` → add 2 extension methods
- `src/TurboHttp.IntegrationTests/Shared/Routes.cs` → add 2 route groups (12 routes)
- `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs` → call new registrations
- `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs` → call new registrations
- `src/TurboHttp.IntegrationTests/Shared/KestrelTlsFixture.cs` → call new registrations

---

### TASK-023-002: Feature Interaction Tests – HTTP/1.1 (Reference Implementation)
**Description:** As a developer I want tests that verify the interaction of multiple BidiStages so that it is ensured that pipeline composition works correctly.

**Token Estimate:** ~45k tokens
**Predecessors:** none
**Successors:** TASK-023-003
**Parallel:** yes – can run alongside TASK-023-001, TASK-023-008

**Test Scenarios (7 Tests):**

| # | DisplayName | Stages | What is being tested |
|---|-------------|--------|-----------------|
| 1 | `Interaction-001: Redirect preserves cookies across hops` | Cookie + Redirect | Set-Cookie → 302 → Cookie is sent to new target |
| 2 | `Interaction-002: Compressed response served from cache` | Cache + Compression | gzip response cached → second request from cache → correctly decompressed |
| 3 | `Interaction-003: Retry after redirect target returns 503` | Redirect + Retry | 302 → target returns 503 → retry → success |
| 4 | `Interaction-004: Cookie survives retry cycle` | Cookie + Retry | Set-Cookie → 503 → retry → cookie still in jar |
| 5 | `Interaction-005: Vary Accept-Encoding creates separate cache entries` | Cache + Vary + Compression | Request with gzip → cached, request with br → cached separately |
| 6 | `Interaction-006: Redirect chain with cookies accumulated` | Cookie + Redirect | 3-hop redirect, each hop sets cookie → all 3 cookies present at end |
| 7 | `Interaction-007: Cache hit bypasses retry logic` | Cache + Retry | Cacheable 200 → cached → second request from cache (no server contact) |

**Client Configuration:** `configure: b => b.WithCookies().WithCache(policy).WithRetry(policy).WithRedirect()`

**Acceptance Criteria:**
- [ ] `FeatureInteractionIntegrationTests.cs` created with 7 tests
- [ ] All tests use `[Collection("H11")]` and `new Version(1, 1)`
- [ ] DisplayNames follow `Interaction-001` pattern
- [ ] Tests configure multiple BidiStages via `ClientHelper.CreateClient(..., configure: ...)`
- [ ] All 7 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H11/FeatureInteractionIntegrationTests.cs` (NEW)

---

### TASK-023-003: Feature Interaction Tests – H10 + H2 + TLS
**Description:** As a developer I want feature interaction tests for HTTP/1.0, HTTP/2 and TLS so that interactions are verified across all versions.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-023-002 (reference pattern)
**Successors:** TASK-023-009
**Parallel:** no – needs reference from TASK-023-002

**Pattern:** Copy `FeatureInteractionIntegrationTests.cs`, change version/fixture/collection/display names.

**Acceptance Criteria:**
- [ ] `FeatureInteractionH10IntegrationTests.cs` → 7 tests, `new Version(1, 0)`, `[Collection("H10")]`
- [ ] `FeatureInteractionH2IntegrationTests.cs` → 7 tests, `new Version(2, 0)`, `[Collection("H2")]`
- [ ] `FeatureInteractionTlsIntegrationTests.cs` → 7 tests, `new Version(1, 1)`, `scheme: "https"`, `[Collection("TLS")]`
- [ ] DisplayNames: `Interaction-H10-001`, `Interaction-H2-001`, `Interaction-TLS-001`
- [ ] All 21 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H10/FeatureInteractionH10IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/H2/FeatureInteractionH2IntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/TLS/FeatureInteractionTlsIntegrationTests.cs` (NEW)

---

### TASK-023-004: Resilience Tests – HTTP/1.1 (Reference Implementation)
**Description:** As a developer I want tests for malformed server responses so that it is ensured the client responds gracefully (exception, timeout, or error message – no hang/crash).

**Token Estimate:** ~50k tokens
**Predecessors:** TASK-023-001 (routes must exist)
**Successors:** TASK-023-005
**Parallel:** yes – can run alongside TASK-023-002, TASK-023-003, TASK-023-006, TASK-023-008

**Test Scenarios (8 Tests):**

| # | DisplayName | Route | Expected Behavior |
|---|-------------|-------|---------------------|
| 1 | `Resilience-001: Content-Length mismatch causes exception or timeout` | `/resilience/content-length-mismatch` | Exception or timeout – no hang |
| 2 | `Resilience-002: Corrupt gzip data causes graceful failure` | `/resilience/corrupt-gzip` | Exception on body read – no crash |
| 3 | `Resilience-003: Corrupt brotli data causes graceful failure` | `/resilience/corrupt-br` | Exception on body read |
| 4 | `Resilience-004: Truncated body detected` | `/resilience/truncated-body/4` | Exception or short body |
| 5 | `Resilience-005: Slow headers within timeout succeed` | `/resilience/slow-headers/500` | Response OK (timeout 30s) |
| 6 | `Resilience-006: Slow body within timeout succeed` | `/resilience/slow-body/500` | Body fully received |
| 7 | `Resilience-007: Slow headers exceed timeout cause cancellation` | `/resilience/slow-headers/10000` | OperationCanceledException (timeout 3s) |
| 8 | `Resilience-008: Empty response causes exception` | `/resilience/empty-response` | Exception – no hang |

**Acceptance Criteria:**
- [ ] `ResilienceIntegrationTests.cs` created with 8 tests
- [ ] Tests use short timeouts (3-5s) for expected failures, 30s for expected success
- [ ] Each failure scenario verified with `Assert.ThrowsAnyAsync<Exception>` or `Assert.ThrowsAnyAsync<OperationCanceledException>`
- [ ] No test hangs indefinitely
- [ ] DisplayNames follow `Resilience-001` pattern
- [ ] All 8 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H11/ResilienceIntegrationTests.cs` (NEW)

---

### TASK-023-005: Resilience Tests – H10 + H2 + TLS
**Description:** As a developer I want resilience tests for all other HTTP versions.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-023-004 (reference pattern), TASK-023-001 (routes)
**Successors:** TASK-023-009
**Parallel:** no – needs reference from TASK-023-004

**Notes:**
- HTTP/1.0: All 8 scenarios applicable (same route infrastructure)
- HTTP/2: Content-Length mismatch and truncated body behave differently (streams, RST_STREAM). Tests may need adjustment – exception expected instead of timeout. `empty-response` in HTTP/2 context is a GOAWAY/RST_STREAM.
- TLS: All scenarios over HTTPS – identical behavior expected

**Acceptance Criteria:**
- [ ] `/H10/ResilienceIntegrationTests.cs` → 8 tests adapted for HTTP/1.0
- [ ] `/H2/ResilienceIntegrationTests.cs` → 8 tests adapted for HTTP/2 (H2-specific error modes)
- [ ] `/TLS/ResilienceIntegrationTests.cs` → 8 tests over HTTPS
- [ ] DisplayNames: `Resilience-H10-001`, `Resilience-H2-001`, `Resilience-TLS-001`
- [ ] HTTP/2 tests may expect different exception types (e.g., `HttpRequestException` instead of `OperationCanceledException` for RST_STREAM)
- [ ] All 24 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H10/ResilienceIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/H2/ResilienceIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/TLS/ResilienceIntegrationTests.cs` (NEW)

---

### TASK-023-006: Request Compression Tests – HTTP/1.1 (Reference Implementation)
**Description:** As a developer I want end-to-end tests for request body compression (ContentEncodingBidiStage with RequestCompressionPolicy) so that it is verified the client correctly sends compressed bodies.

**Token Estimate:** ~40k tokens
**Predecessors:** TASK-023-001 (routes + builder extensions)
**Successors:** TASK-023-007
**Parallel:** yes – can run alongside TASK-023-002, TASK-023-004, TASK-023-008

**Client Configuration:** `configure: b => b.WithRequestCompression(new RequestCompressionPolicy { Encoding = "gzip" })`

**Test Scenarios (6 Tests):**

| # | DisplayName | Encoding | What is being tested |
|---|-------------|----------|-----------------|
| 1 | `ReqCompress-001: gzip request body sent and verified by server` | gzip | POST 4KB → server verifies gzip → echoes decompressed |
| 2 | `ReqCompress-002: deflate request body sent and verified` | deflate | POST 4KB → server verifies deflate |
| 3 | `ReqCompress-003: brotli request body sent and verified` | br | POST 4KB → server verifies brotli |
| 4 | `ReqCompress-004: small body below threshold NOT compressed` | gzip | POST 100 bytes → server sees no Content-Encoding |
| 5 | `ReqCompress-005: Content-Encoding header set correctly` | gzip | Verify `X-Content-Encoding` echo header == "gzip" |
| 6 | `ReqCompress-006: compressed request + decompressed response roundtrip` | gzip | POST gzip → response gzip → both sides correct |

**Acceptance Criteria:**
- [ ] `RequestCompressionIntegrationTests.cs` created with 6 tests
- [ ] Tests use `WithRequestCompression()` via `configure` callback
- [ ] Threshold test verifies 100-byte body is NOT compressed (< 1024 default)
- [ ] Roundtrip test sends compressed body AND receives compressed response
- [ ] DisplayNames follow `ReqCompress-001` pattern
- [ ] All 6 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H11/RequestCompressionIntegrationTests.cs` (NEW)

---

### TASK-023-007: Request Compression Tests – H10 + H2 + TLS
**Description:** As a developer I want request compression tests for all HTTP versions.

**Token Estimate:** ~35k tokens
**Predecessors:** TASK-023-006 (reference pattern), TASK-023-001 (routes + extensions)
**Successors:** TASK-023-009
**Parallel:** no – needs reference from TASK-023-006

**Acceptance Criteria:**
- [ ] `/H10/RequestCompressionIntegrationTests.cs` → 6 tests, `new Version(1, 0)`
- [ ] `/H2/RequestCompressionIntegrationTests.cs` → 6 tests, `new Version(2, 0)`
- [ ] `/TLS/RequestCompressionIntegrationTests.cs` → 6 tests, `scheme: "https"`
- [ ] DisplayNames: `ReqCompress-H10-001`, `ReqCompress-H2-001`, `ReqCompress-TLS-001`
- [ ] All 18 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H10/RequestCompressionIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/H2/RequestCompressionIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/TLS/RequestCompressionIntegrationTests.cs` (NEW)

---

### TASK-023-008: Custom Handler Pipeline Tests
**Description:** As a developer I want integration tests for the custom handler pipeline (`TurboHandler`, `UseRequest()`, `UseResponse()`, `AddHandler<T>()`), so that handler composition is verified end-to-end.

**Token Estimate:** ~50k tokens
**Predecessors:** none
**Successors:** TASK-023-009
**Parallel:** yes – can run alongside TASK-023-001 through TASK-023-007

**Test Scenarios – HTTP/1.1 (8 Tests):**

| # | DisplayName | Handler Type | What is being tested |
|---|-------------|-------------|-----------------|
| 1 | `Handler-001: UseRequest injects custom header` | `UseRequest` | Header X-Custom-Injected is set → `/headers/echo` confirms |
| 2 | `Handler-002: UseResponse adds header to response` | `UseResponse` | Response gets X-Handler-Added header |
| 3 | `Handler-003: AddHandler typed handler processes request` | `AddHandler<T>` | Custom TurboHandler subclass modifies request |
| 4 | `Handler-004: Multiple handlers execute in registration order` | `UseRequest` ×2 | Handler 1 sets X-First, handler 2 sets X-Second → both present |
| 5 | `Handler-005: Handler sees original request on response` | `UseResponse` | `ProcessResponse(original, response)` → original has original URL |
| 6 | `Handler-006: Handler works with redirect pipeline` | `UseRequest` + Redirect | Handler injects header → redirect → header still there after redirect? |
| 7 | `Handler-007: Handler works with compression pipeline` | `UseResponse` + Compression | Handler sees decompressed response |
| 8 | `Handler-008: Handler works with cookie pipeline` | `UseRequest` + Cookie | Handler + cookie injection → both headers present |

**HTTP/2 Variant (4 Tests):** Tests 1, 3, 4, 6 over HTTP/2 to verify that handlers are protocol-agnostic.

**Acceptance Criteria:**
- [ ] `/H11/HandlerPipelineIntegrationTests.cs` created (HTTP/1.1, 8 tests)
- [ ] `/H2/HandlerPipelineIntegrationTests.cs` created (HTTP/2, 4 selected tests)
- [ ] Custom `TestHeaderHandler : TurboHandler` class defined in test file
- [ ] Tests use `configure: b => b.UseRequest(...)`, `b.UseResponse(...)`, `b.AddHandler<T>()`
- [ ] DisplayNames follow `Handler-001` / `Handler-H2-001`
- [ ] All 12 tests pass
- [ ] Build passes with zero warnings

**Files:**
- `src/TurboHttp.IntegrationTests/H11/HandlerPipelineIntegrationTests.cs` (NEW)
- `src/TurboHttp.IntegrationTests/H2/HandlerPipelineIntegrationTests.cs` (NEW)

---

### TASK-023-009: Verification Gate
**Description:** As a developer I want to verify that all new tests are green, the build is clean, and no regressions exist.

**Token Estimate:** ~15k tokens
**Predecessors:** TASK-023-003, TASK-023-005, TASK-023-007, TASK-023-008
**Successors:** none
**Parallel:** no – final gate

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → zero errors, zero warnings
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` → all tests pass
- [ ] New test count: ~96 additional tests (7+21 interactions + 8+24 resilience + 6+18 compression + 12 handlers)
- [ ] 3 consecutive test runs pass (no flaky tests)
- [ ] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` → existing unit tests still pass (no regressions from builder extensions)
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` → existing stream tests still pass

**Files:** (read-only verification)

---

## Task Dependency Graph

```
TASK-023-001 (Routes+Builder) ──┬─ TASK-023-004 (Resilience H11) ──┬─ TASK-023-005 (Resilience H10+H2+TLS) ──┐
                                │                                  │                                          │
                                ├─ TASK-023-006 (ReqCompress H11) ─┴─ TASK-023-007 (ReqCompress H10+H2+TLS) ─┤
TASK-023-002 (Interaction H11) ──┴─ TASK-023-003 (Interaction H10+H2+TLS) ─────────────────────────────────────┼─ TASK-023-009
                                                                                                               │
TASK-023-008 (Handler H11+H2) ──────────────────────────────────────────────────────────────────────────────────┘
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-023-001 | ~40k | none | yes (with 002, 008) | ✓ |
| TASK-023-002 | ~45k | none | yes (with 001, 008) | ✓ |
| TASK-023-003 | ~40k | 002 | no | ✓ |
| TASK-023-004 | ~50k | 001 | yes (with 002, 006, 008) | ✓ |
| TASK-023-005 | ~40k | 001, 004 | no | ✓ |
| TASK-023-006 | ~40k | 001 | yes (with 002, 004, 008) | ✓ |
| TASK-023-007 | ~35k | 001, 006 | no | ✓ |
| TASK-023-008 | ~50k | none | yes (with 001, 002) | ✓ |
| TASK-023-009 | ~15k | 003, 005, 007, 008 | no | ✓ |

**Total estimated tokens:** ~355k

## Functional Requirements

- FR-1: `WithRequestCompression(policy?)` extension method must set `RequestCompressionPolicy` in `TurboClientDescriptor`
- FR-2: `WithExpectContinue(policy?)` extension method must set `Expect100Policy` in `TurboClientDescriptor`
- FR-3: Feature interaction tests must activate at least 2 BidiStages simultaneously via `configure`
- FR-4: Resilience routes must send broken responses (Content-Length mismatch, corrupt compression, truncated body)
- FR-5: Resilience tests must NOT hang – each test has a timeout (max 30s for success, max 5s for expected failures)
- FR-6: Request compression tests must verify the server received compressed bytes (via echo header)
- FR-7: Handler tests must cover `UseRequest()`, `UseResponse()` and `AddHandler<T>()`
- FR-8: Handler ordering must be FIFO (registration order = execution order)
- FR-9: All tests follow the `DisplayName` pattern: `Category-VERSION-NNN: description`
- FR-10: All tests use `CancellationTokenSource` with explicit timeout

## Non-Goals

- HTTP/3 tests (QUIC not stable)
- Changes to production BidiStages (tests + builder extensions only)
- Performance/throughput measurements (Feature 021 has concurrency tests)
- TracingBidiStage tests (activity propagation is a separate topic)
- Connection pool lifecycle tests (idle eviction, pool exhaustion – separate feature)
- HEAD method fix (known bug in Http11DecoderStage – separate bugfix)

## Technical Considerations

- **Builder Extensions:** `WithRequestCompression()` and `WithExpectContinue()` are minimal API additions that follow the existing pattern (`WithCookies()`, `WithCache()`, etc.). No breaking change.
- **Resilience Route Implementation:** The `/resilience/content-length-mismatch` route must write the body manually (not via `Results.Content`) to create content-length mismatch. Pattern: `ctx.Response.ContentLength = 1000; await ctx.Response.Body.WriteAsync(new byte[500]); await ctx.Response.Body.FlushAsync();`
- **Request Compression Verification:** The server receives compressed bytes in the body. The `/compress/verify-gzip` route must use `GZipStream` to decompress and echo back the decompressed body.
- **HTTP/2 Resilience:** In HTTP/2, some errors are signaled as RST_STREAM instead of connection close. The H2 resilience tests may need to expect different exception types.
- **ClientHelper configure:** The existing `configure` parameter in `ClientHelper.CreateClient()` enables builder configuration without changing ClientHelper itself.
- **Test Isolation:** Feature interaction tests using cache need a unique URL per test (e.g. `/cache/max-age/60?t={testId}`) so tests don't interfere with each other.

## Success Metrics

- ~96 new integration tests across 14 new files
- All BidiStage combinations verified end-to-end at least once
- Zero resilience tests that hang (all with timeout)
- Request compression end-to-end verified for gzip, deflate and brotli
- Handler pipeline tested with 3 API variants

## Open Questions

_None – all questions resolved._
