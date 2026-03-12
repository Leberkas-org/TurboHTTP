# Maggus Project Memory — TurboHttp

## Integration Test Patterns

### Http10 Integration Tests
- Location: `src/TurboHttp.IntegrationTests/Http10/`
- Pattern: Each test class uses `TestKit` + `IClassFixture<KestrelFixture>`
- HTTP/1.0 = new pipeline per request (connection closes after response)
- Helper `SendAsync()` materializes fresh `Http10Engine` + `ConnectionStage` per call
- Cookie tests use `CookieJar` manually: call `AddCookiesToRequest` before send, `ProcessResponse` after
- Redirect tests use `RedirectHandler` manually in a loop

### Http11 Integration Tests
- Location: `src/TurboHttp.IntegrationTests/Http11/`
- Same pattern as Http10: `TestKit` + `IClassFixture<KestrelFixture>` + `SendAsync()` helper
- Uses `Http11Engine` + `HttpVersion.Version11`
- Tests: 01_BasicTests (10), 02_ChunkedTests (8), 03_ConnectionTests (8), 04_RedirectTests (10 methods, 13 cases), 05_CookieTests (12), 06_RetryTests (10), 07_CacheTests (15)
- Connection routes: `/conn/default`, `/conn/close`, `/conn/keep-alive`, `/close`
- Redirect tests: same manual `RedirectHandler` loop as Http10, but with body-preservation workaround (fresh `ByteArrayContent` per hop since encoder disposes content streams)
- Retry tests: manual `RetryEvaluator` loop with request cloning per attempt; content cloned via `ReadAsByteArrayAsync`
- Cache tests: manual `HttpCacheStore` + `CacheFreshnessEvaluator` + `CacheValidationRequestBuilder` in `SendWithCacheAsync()` helper; Kestrel `MapGet` doesn't handle HEAD (405), POST to GET-only routes returns 405 (cacheable status 405 can re-store after invalidation)

### Kestrel Fixture Routes
- Cookie routes: `/cookie/set/{name}/{value}`, `/cookie/echo`, `/cookie/set-multiple`, `/cookie/delete/{name}`, `/cookie/set-expires/{name}/{value}/{seconds}`, `/cookie/set-path/{name}/{value}/{*path}`, `/cookie/set-and-redirect` (added TASK-021)
- Redirect routes: `/redirect/{code}/{*target}`, `/redirect/chain/{n}`, `/redirect/loop`, `/redirect/relative`, `/redirect/cross-origin`, `/redirect/cross-origin-auth` (added TASK-027), POST `/redirect/308` (added TASK-027)
- Retry routes: `/retry/408`, `/retry/503` (GET|HEAD|PUT|DELETE), `/retry/503-retry-after/{seconds}`, `/retry/503-retry-after-date`, `/retry/succeed-after/{n}`, POST `/retry/non-idempotent-503`
- Cache routes: `/cache/max-age/{s}`, `/cache/no-cache`, `/cache/no-store`, `/cache/etag/{id}`, `/cache/last-modified/{id}`, `/cache/vary/{header}`, `/cache/must-revalidate`, `/cache/s-maxage/{s}`, `/cache/expires`, `/cache/private`

### Http20 Integration Tests
- Location: `src/TurboHttp.IntegrationTests/Http20/`
- Pattern: `TestKit` + `IClassFixture<KestrelH2Fixture>` + custom `SendAsync()` helper
- **Cannot use `engine.CreateFlow().Join(transport)` pattern** — must build graph manually because Http20Engine's PrependPrefaceStage never receives ConnectItem (it's wired after the encoder, not before transport)
- Uses `Concat.Create<ITransportItem>(2)` to inject ConnectItem before PrependPrefaceStage
- Requires configurable window sizes: `new Http20ConnectionStage(2 * 1024 * 1024)` and `new PrependPrefaceStage(2 * 1024 * 1024)` for 1MB+ body transfers
- H2-specific routes on `KestrelH2Fixture`: `/h2/settings`, `/h2/many-headers`, `/h2/echo-binary`, `/h2/echo-path`, `/h2/large-headers/{kb}`, `/h2/cookie`, `/h2/priority/{kb}`
- Tests: 01_BasicTests (9), 02_MultiplexTests (6), 03_FlowControlTests (4), 04_SettingsPingTests, 05_PseudoHeaderTests, 06_RedirectTests, 07_CookieTests, 08_RetryTests, 09_CacheTests (5), 10_ContentEncodingTests (4), 11_ErrorHandlingTests (4)
- Cache tests: manual `HttpCacheStore` + `CacheFreshnessEvaluator` + `CacheValidationRequestBuilder` in `SendWithCacheAsync()` helper (same pattern as Http11); test IDs 20E-INT-046 through 20E-INT-050
- Error handling routes: `/h2/abort` (triggers RST_STREAM via ctx.Abort), `/h2/delay/{ms}` (delayed response)
- Error handling tests: RST_STREAM isolation in multiplexed pipeline, 500 as status code not exception, sequential reconnect, recovery after abort; test IDs 20E-INT-055 through 20E-INT-058
- Note: Http20StreamStage silently ignores RstStreamFrame (no case in switch) — aborted stream hangs but other streams complete. True GOAWAY testing not feasible with shared KestrelH2Fixture.

### Http20 Flow Control Limitations (TASK-034)
- `Http20ConnectionStage._connectionWindow` is NOT replenished after emitting WINDOW_UPDATE — responses must fit within the initial window allocation
- `HandleOutboundData` shares `_connectionWindow` with receive-side tracking (should be separate)
- POST bodies limited to ≤65535 bytes (server's default INITIAL_WINDOW_SIZE) because pipeline doesn't handle server WINDOW_UPDATE for outbound flow control
- Tests designed around these constraints: use 2MB window for large responses, keep POST ≤65535

### Http20 Production Code Fixes (TASK-032)
- **ConnectionStage**: Added `_pendingReads` buffer (was dropping data when outlet not available); added `Pull` after connected write (needed for multi-frame HTTP/2)
- **Http20ConnectionStage**: Configurable `initialRecvWindowSize` (default 65535); separate recv/send stream window tracking; skip WindowUpdate for 0-length DATA frames
- **PrependPrefaceStage**: Configurable `initialWindowSize` (default 65535); emits connection-level WINDOW_UPDATE when window > 65535

### TurboHttpClient Integration Tests (TASK-043)
- Location: `src/TurboHttp.IntegrationTests/Shared/01_TurboHttpClientTests.cs`
- Pattern: `TestKit` + `IClassFixture<KestrelFixture>` + `TurboHttpClient.SendAsync()` public API
- Tests go through the full pipeline: TurboHttpClient → TurboClientStreamManager → Engine → ConnectionStage → TCP
- Key infrastructure fixes made to Engine.cs to enable production mode:
  - `BuildConnectionFlow<TEngine>` injects ConnectItem via `Broadcast(2) + Take(1) + Concat + Buffer(1)` pattern
  - `Func<TurboRequestOptions>?` factory threading for dynamic BaseAddress/DefaultHeaders
- `/delay/{ms}` route added to KestrelFixture for timeout/cancellation tests
- Http30Engine stub fixed: uses `BidiFlow.FromFlows` with `NotSupportedException` instead of empty GraphDsl
- CLIENT-008 uses sequential (not parallel) requests — HTTP/1.1 single-pipeline limitation

### Version Negotiation Integration Tests (TASK-044)
- Location: `src/TurboHttp.IntegrationTests/Shared/02_VersionNegotiationTests.cs`
- Pattern: `TestKit` + `IClassFixture<KestrelFixture>` + `IClassFixture<KestrelH2Fixture>` (both fixtures)
- HTTP/1.0 and HTTP/1.1 tests use `TurboHttpClient.SendAsync()` (full client pipeline)
- HTTP/2 tests use `Http20Engine` directly with `SendH2Async()` helper (same pattern as Http20BasicTests)
- **Known limitation**: `Engine.BuildConnectionFlow<Http20Engine>` does NOT inject `PrependPrefaceStage`, so HTTP/2 through `TurboHttpClient` times out. HTTP/2 must be tested at engine level.
- **Known limitation**: `Http20StreamStage` assembles responses with default `Version = 1.1` — cannot assert `response.Version == 2.0` on HTTP/2 responses.
- Tests: VERNEG-001 (HTTP/1.0), VERNEG-002 (HTTP/1.1), VERNEG-003 (HTTP/2.0), VERNEG-004 (mixed demux), VERNEG-005 (DefaultRequestVersion override)

### TLS Integration Tests (TASK-046)
- Location: `src/TurboHttp.IntegrationTests/Shared/04_TlsTests.cs`
- Pattern: `TestKit` + `IClassFixture<KestrelTlsFixture>` + `SendTlsAsync()` helper
- Uses `TlsOptions` with `ServerCertificateValidationCallback = (_, _, _, _) => true` for self-signed cert
- `TargetHost = "localhost"` required (must match cert CN)
- Tests 1, 3, 4 make real TLS connections; tests 2, 5 test protocol handler logic only
- **KestrelTlsFixture fix**: `LoadCertificate()` → `LoadPkcs12()` (PFX contains private key, LoadCertificate expects DER/PEM)
- Pre-existing failures: RETRY-INT-001, COOKIE-INT-006 (10s timeouts, unrelated to TLS)

### Edge Case Integration Tests (TASK-047)
- Location: `src/TurboHttp.IntegrationTests/Shared/05_EdgeCaseTests.cs`
- Pattern: `TestKit` + `IClassFixture<KestrelFixture>` + `SendAsync()` helper with optional timeout
- New Kestrel routes added: `/edge/close-mid-response`, `/edge/large-header/{kb}`, `/edge/unknown-encoding`, `/edge/empty-body`
- **Http11Decoder enforces RFC 9112 §2.3 max line length**: 32KB header value triggers `HttpDecoderException`, cannot receive oversized headers
- **ContentEncodingDecoder throws for unknown encodings**: `Content-Encoding: x-custom` → `HttpDecoderException` per RFC 9110 §8.4; no passthrough mode
- **Non-routable IPs cause ActorSystem shutdown hangs**: TCP connections to non-routable IPs (e.g., 192.0.2.1) leave pending actors that block `Sys.Terminate()` beyond 10s timeout. Use loopback with closed ports for connection failure tests.
- Test count: 8 tests (EDGE-001 through EDGE-008), all passing

## Build Notes
- `COMMIT.md` is in `.gitignore` — use `git add -f COMMIT.md` to stage it
- `BenchmarkDotNet.Artifacts` also gitignored
- `.maggus/runs/` is in `.gitignore` — use `git add -f` to stage iteration logs
- Engine.cs CS8509 warning fixed in TASK-048 (added default case to version switch)
- 02_FrameParsingTests.cs CS0219 warning fixed in TASK-048 (removed unused `newMax` variable)
- Zero warnings as of TASK-048

## Test Counts (TASK-048 Baseline — 2026-03-12)
- Unit tests (TurboHttp.Tests): 2158
- Stream tests (TurboHttp.StreamTests): 421
- Integration tests (TurboHttp.IntegrationTests): 234
  - Http10: 46, Http11: 89, Http20: 66, Cross/Client/TLS/Edge: 46 (some overlap in filter)
- New stage tests: 83 (Cookie 12, Decompression 10, Cache 24, Redirect 15, Retry 12, ConnReuse 10)
- **Total: 2803 all green**
- Flaky timeouts when running all 3 projects simultaneously (resource contention); each project passes 100% individually

## Connection Pool (TASK-001 ✅, TASK-002 ✅)
- **ConnectionPoolStage**: `src/TurboHttp/IO/Stages/ConnectionPoolStage.cs` — custom GraphStage with sub-graph materialization
- **Types**: `src/TurboHttp/IO/Stages/ConnectionPoolTypes.cs` — `RoutedTransportItem`, `RoutedDataItem`, `PoolConfig`
- **Tests**: `src/TurboHttp.StreamTests/Stages/ConnectionPoolStageTests.cs` — 10 tests (8 foundation + 2 roundtrip)
- **Plan**: `.maggus/plan_3.md` — 12 tasks (TASK-001 through TASK-012)
- **Pattern**: Pool wraps/orchestrates multiple `ConnectionStage` instances as materialised sub-graphs (Source.Queue → ConnectionStage → Sink.ForEach)
- **Factory type**: `Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>>` — accepts both real ConnectionStage and test echo flows
- **Inner types**: `HostPool` (TcpOptions, List<ConnectionSlot>, ConnectionCounter), `ConnectionSlot` (Queue, Completion, Active, Idle, PendingRequestCount)
- **Sub-graph materialization**: Uses `GraphStageLogic.Materializer` (SubFusingActorMaterializer) + `GetAsyncCallback` for thread-safe response delivery
- **In-flight tracking**: `_inFlightCount` prevents premature CompleteStage when sub-graph responses pending
- **TcpOptions**: Uses `required` init properties, not positional constructor — use `new() { Host = "...", Port = 443 }`
- **Pre-existing failure**: `RFC-9113-ENG-004` in StreamTests (unrelated to pool work)

## Benchmark Status
- `TurboHttp.Benchmarks` project has infrastructure only (Config.cs, Program.cs)
- No `[Benchmark]` methods defined — cannot measure performance baseline
- RFC compliance matrix: `RFC_COMPLIANCE.md` in repo root
