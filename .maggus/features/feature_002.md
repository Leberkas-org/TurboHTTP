# Feature 002: Integration Tests — Foundation & Basic Request/Response

## Introduction

The integration test infrastructure (4 Kestrel fixtures, 93 routes, TestKit base) exists, but there are **zero test classes**. This feature builds the shared test infrastructure and verifies basic request/response functionality for all 4 HTTP versions (1.0/1.1, 2.0, 3.0) plus TLS.

### Architecture Context

- **Components involved:**
  - `src/TurboHttp.IntegrationTests/Shared/` — Fixtures (KestrelFixture, KestrelH2Fixture, KestrelH3Fixture, KestrelTlsFixture), TestKit, Routes
  - `src/TurboHttp/Client/ITurboHttpClient.cs` — SendAsync API
  - `src/TurboHttp/Hosting/TurboClientServiceCollectionExtensions.cs` — DI registration
  - `src/TurboHttp/Middleware/TurboHttpClientBuilderExtensions.cs` — Builder pattern
- **New patterns:** xUnit Collection Definitions for fixture sharing, DI-based `ClientHelper` for consistent client setup
- **No architecture changes required**

## Goals

- Create xUnit Collection Definitions for all 4 fixtures
- Implement a DI-based `ClientHelper` (mirrors how end users consume the client)
- Basic GET/POST/PUT/DELETE/PATCH/HEAD tests for HTTP/1.x, HTTP/2, HTTP/3, and HTTPS
- Verify status codes, header echo, content types, empty bodies
- Make HTTP/3 tests filterable with `[Trait("Category", "Http3")]`

## Tasks

### TASK-002-001: Shared Test Infrastructure
**Description:** As a developer, I want reusable test infrastructure (Collections, ClientHelper), so that all subsequent integration tests have a consistent foundation.

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-002-002, TASK-002-003
**Parallel:** no — all subsequent tasks depend on this

**Acceptance Criteria:**
- [ ] `Shared/Collections.cs` with 4 Collection Definitions:
  - `[CollectionDefinition("Http1Integration")] public sealed class Http1IntegrationCollection : ICollectionFixture<KestrelFixture>`
  - `[CollectionDefinition("Http2Integration")] public sealed class Http2IntegrationCollection : ICollectionFixture<KestrelH2Fixture>`
  - `[CollectionDefinition("Http3Integration")] public sealed class Http3IntegrationCollection : ICollectionFixture<KestrelH3Fixture>`
  - `[CollectionDefinition("TlsIntegration")] public sealed class TlsIntegrationCollection : ICollectionFixture<KestrelTlsFixture>`
- [ ] `Shared/ClientHelper.cs` with static factory method:
  - `CreateClient(int port, Version version, Action<ITurboHttpClientBuilder>? configure = null)` → `ITurboHttpClient`
  - Internally: `ServiceCollection` + `AddTurboHttpClient` + `TurboClientOptions` with `DangerousAcceptAnyServerCertificate = true`
  - Creates its own `ActorSystem` per client (or shared per test class)
  - Implements `IAsyncDisposable` for cleanup (ActorSystem shutdown)
- [ ] Solution builds without errors: `dotnet build src/TurboHttp.IntegrationTests/`
- [ ] At least 1 smoke test (GET /hello via HTTP/1.1) proves the infrastructure works

### TASK-002-002: Basic Tests for HTTP/1.x and TLS
**Description:** As a developer, I want basic request/response tests for HTTP/1.0, HTTP/1.1, and HTTPS, so that baseline functionality per HTTP version is verified.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-002-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-002-003

**Acceptance Criteria:**
- [ ] `Http1BasicTests.cs` with `[Collection("Http1Integration")]` (~16 tests):
  - GET /hello → 200 "Hello World"
  - HEAD /hello → 200, empty body
  - GET /ping → 200 "pong"
  - `[Theory]` GET /status/{code} for 200, 201, 204, 301, 400, 404, 500, 503
  - GET /headers/echo with X-Custom-Header → echo back
  - GET /headers/set?X-Foo=bar → response header set
  - GET /multiheader → two X-Value headers (alpha, beta)
  - GET /empty-cl → Content-Length: 0, empty body
  - GET /edge/empty-body → 200 without Content-Length
  - GET /unknown-headers → X-Unknown-Foo/Bar present
  - POST /echo "Hello" → body echo
  - PUT /echo "World" → body echo
  - PATCH /echo "!" → body echo
  - GET /large/10 → 10240 bytes received correctly
  - `[Theory]` /any with GET, POST, PUT, DELETE, PATCH → method echo
- [ ] `TlsBasicTests.cs` with `[Collection("TlsIntegration")]` (~10 tests):
  - GET /hello via HTTPS → 200
  - POST /echo via HTTPS → body echo
  - HEAD /hello via HTTPS → 200, no body
  - `[Theory]` GET /status/{code} for 200, 400, 500 via HTTPS
  - GET /headers/echo via HTTPS
  - GET /large/10 via HTTPS
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http1BasicTests|TlsBasicTests"`

### TASK-002-003: Basic Tests for HTTP/2 and HTTP/3
**Description:** As a developer, I want basic request/response tests for HTTP/2 (h2c) and HTTP/3 (QUIC), so that multiplexing protocols are verified end-to-end.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-002-001
**Successors:** none
**Parallel:** yes — can run alongside TASK-002-002

**Acceptance Criteria:**
- [ ] `Http2BasicTests.cs` with `[Collection("Http2Integration")]` (~14 tests):
  - GET /hello → 200 "Hello World"
  - HEAD /hello → 200, no body
  - `[Theory]` GET /status/{code} for 200, 201, 204, 400, 500
  - POST /echo → body echo
  - GET /headers/echo with X-Custom → echo
  - GET /multiheader → two header values
  - GET /empty-cl → empty body
  - GET /large/10 → 10 KB
  - GET /h2/settings → "h2-ok"
  - GET /h2/many-headers → 20 custom headers decoded
  - POST /h2/echo-binary → binary data returned exactly
  - GET /h2/echo-path → :path pseudo-header in body
- [ ] `Http3BasicTests.cs` with `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]` (~12 tests):
  - GET /hello → 200 "Hello World"
  - POST /echo → body echo
  - `[Theory]` GET /status/{code} for 200, 400, 500
  - GET /headers/echo
  - GET /large/10
  - GET /h3/protocol → "HTTP/3" in body
  - GET /h3/settings → "h3-ok" + X-Protocol header
  - POST /h3/echo-binary → binary data over QUIC
  - GET /h3/many-headers → 20 custom headers
- [ ] All tests green: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http2BasicTests|Http3BasicTests"`

## Task Dependency Graph

```
TASK-002-001 ──→ TASK-002-002
             └──→ TASK-002-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-002-001 | ~75k | none | no | — |
| TASK-002-002 | ~80k | 001 | yes (with 003) | — |
| TASK-002-003 | ~80k | 001 | yes (with 002) | — |

**Total estimated tokens:** ~235k

## Functional Requirements

- FR-1: Each test class must share a fixture via xUnit Collection (no server restart per test)
- FR-2: Client creation must use DI (`AddTurboHttpClient` + `ITurboHttpClientFactory`)
- FR-3: HTTP/3 tests must carry `[Trait("Category", "Http3")]` for filtering on non-QUIC systems
- FR-4: Every test must use `CancellationToken` with timeout (no hanging)
- FR-5: Client cleanup (ActorSystem shutdown) must happen in `IAsyncLifetime.DisposeAsync()`
- FR-6: Tests must be excludable via `dotnet test --filter "Category!=Http3"` for HTTP/3

## Non-Goals

- No body handling tests (→ Feature 003)
- No redirect/retry tests (→ Feature 004)
- No cookie/cache tests (→ Feature 005)
- No edge case tests (→ Feature 006)
- No performance benchmarks

## Technical Considerations

- `KestrelFixture` uses `PortFinder.FindFreeLocalPort()` — tests need no fixed ports
- `KestrelH3Fixture` requires Windows 11+ or Linux with libmsquic — hence `[Trait]` for filtering
- `DangerousAcceptAnyServerCertificate = true` for TLS/QUIC fixtures (self-signed certs)
- xUnit parallelizeTestCollections=false in `xunit.runner.json` — tests run serially

## Success Metrics

- All 52 tests green on Windows 11 (including HTTP/3)
- All ~40 tests (excluding HTTP/3) green on systems without QUIC
- Client setup + teardown under 5 seconds per test class
- No flaky tests after 10 repeated runs

## Open Questions

_None — all questions resolved._
