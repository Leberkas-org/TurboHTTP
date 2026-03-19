# Maggus Project Memory — TurboHttp

## Engine Async Boundaries (TASK-12-004, 2026-03-18)

### Three Fused Islands in Engine.cs
- **Island 1 (Pre-processing):** RequestEnricherStage → redirect merge → CookieInjectionStage → retry merge → CacheLookupStage — default fusion island (no explicit boundary)
- **Island 2 (Protocol Engine):** EngineCore + DecompressionStage — combined flow with `.WithAttributes(Attributes.CreateAsyncBoundary())`
- **Island 3 (Post-Processing):** CookieStorageStage → CacheStorageStage → RetryStage → CacheMerge → RedirectStage — built as `IGraph<PostProcessShape, NotUsed>` with `.Async()`
- Feedback loops (retry.Out1, redirect.Out1) cross from Island 3 back to Island 1 via Buffer(1) cycle-breakers
- `PostProcessShape` is a private sealed class in Engine.cs (2 inlets: response + cache hits, 3 outlets: final response + retry feedback + redirect feedback)
- No async boundaries inside individual protocol engines — they stay fused internally for low overhead
- Benchmark (loopback): HTTP/1.1 ~29 µs/req (33.9K req/sec), HTTP/2 ~22 µs/req (45.6K req/sec)

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

## Plan 4b — StreamRef Actor Protocol (TASK-4B-001)

### New Message Types (as of TASK-4B-001)
- `ConnectionActor.StreamRefsReady(ISinkRef<IDataItem> Sink, ISourceRef<IDataItem> Source)` — pushed by ConnectionActor to parent (HostPoolActor) after TCP connect
- `HostPoolActor.RegisterConnectionRefs(IActorRef Connection, ISinkRef<IDataItem> Sink, ISourceRef<IDataItem> Source)` — same as above, received by HostPoolActor
- `HostPoolActor.HostStreamRefsReady(HostKey Key, ISourceRef<IDataItem> Source)` — pushed by HostPoolActor to parent (PoolRouterActor) after MergeHub setup
- `PoolRouterActor.GetPoolRefs` — request message to get the pool's SinkRef+SourceRef pair
- `PoolRouterActor.PoolRefs(ISinkRef<ITransportItem> Sink, ISourceRef<IDataItem> Source)` — response to GetPoolRefs

### Removed Message Types
- `ConnectionActor.GetStreamRefs` → replaced by proactive push (no more request/reply for refs)
- `ConnectionActor.StreamRefsResponse` → replaced by `StreamRefsReady` pushed to parent
- `HostPoolActor.ConnectionResponse` → response path now stream-based (not actor messages)
- `PoolRouterActor.SendRequest` → routing now via SinkRef stream, not actor messages
- `PoolRouterActor.Response` → response path now stream-based
- `PoolRouterActor.ConnectionIdle` → idle tracking stays in HostPoolActor
- `PoolRouterActor.ConnectionFailed` → failure handling stays in HostPoolActor

### Build State After TASK-4B-001
- 3 expected CS errors in implementing code (HostPoolActor + ConnectionActor constructors)
- No errors in type definitions
- Implementing code will be fixed in TASK-4B-002/003/004

### TASK-4B-008 Complete (2026-03-14)
- 3 new tests added: CA-018, PR-003, ETE-001 — cover remaining acceptance criteria
- **CA-018** (`ConnectionActorTests`): `DataItem` pushed via `ConnectionActor` SinkRef → arrives in TCP outbound `Channel`
- **PR-003** (`PoolRouterActorTests`): `KeyedItem(HostKey)` routed to correct `HostPoolActor` via `PoolRouterActor`; uses `Source.Queue` + `PreMaterialize` for multi-item SinkRef push; `KeyedItem` helper exercises non-`ConnectItem` routing branch
- **ETE-001** (`ActorHierarchyStreamRefTests`, `TurboHttp.StreamTests/IO/`): full hierarchy — `ConnectItem` via SinkRef → HostPoolActor spawned (via `UnhandledMessage`); `DataItem` → ConnectionActor spawned (via `UnhandledMessage(CreateTcpRunner)`); `ClientConnected` → pending DataItem drains to TCP outbound
- Pre-existing tests already satisfied: CA-016, CA-017, HA-001, HA-002
- Build: 0 errors, 0 warnings; all 3 new tests green

### TASK-4B-007 Complete (2026-03-14)
- `Engine.cs`: all `clientManager` parameters renamed to `poolRouter`; production `BuildProtocolFlow` now calls `new ConnectionStage(poolRouter)` (was `clientManager`)
- `TurboClientStreamManager`: creates `PoolRouterActor(clientOptions.PoolConfig)` actor (was `Props.Create<ClientManager>()`)
- `TurboClientOptions`: added `PoolConfig PoolConfig { get; init; } = new PoolConfig()` (with `using TurboHttp.IO`)
- 27 integration test files: `_clientManager` field + `Props.Create<ClientManager>()` + `ConnectionStage(_clientManager)` → `_poolRouter` + `Props.Create(() => new PoolRouterActor())` + `ConnectionStage(_poolRouter)` throughout Http10/, Http11/, Http20/, Shared/
- `ClientManager` is no longer passed to `ConnectionStage` anywhere in the codebase (still used internally in `ConnectionActor`)
- Build: 0 errors, 0 warnings; 31 files changed

### TASK-4B-006 Complete (2026-03-14)
- `ConnectionPoolTypes.cs` deleted; `PoolConfig` moved to `src/TurboHttp/IO/PoolConfig.cs` in namespace `TurboHttp.IO`
- `ConnectionPoolStage.cs`, `ConnectionPoolIntegrationTests.cs`, `ConnectionPoolStageTests.cs` were already absent (removed in TASK-4B-004)
- `RoutedTransportItem` and `RoutedDataItem` were already removed in TASK-4B-004
- All actor files (`HostPoolActor`, `PoolRouterActor`, `ConnectionActor`) keep `using TurboHttp.IO.Stages;` for `IDataItem`, `ITransportItem`, etc.
- Build: 0 errors, 0 warnings; all tests remain green

### TASK-4B-005 Complete (2026-03-14)
- `ConnectionStage` fully rewritten: accepts `IActorRef poolRouter` (no TCP types); uses `GetStageActor(OnMessage)` + `Tell(GetPoolRefs(), stageActor.Ref)` to obtain PoolRefs without PipeTo
- `OnMessage` materializes `Source.Queue<ITransportItem>(256) → sinkRef.Sink` (Keep.Left, not tuple) and `sourceRef.Source → Sink.ForEach → _onResponse GetAsyncCallback`
- `_pendingReads` queue buffers outlet items when downstream not ready; `PostStop` disposes buffered DataItems
- Offer backpressure: `OfferAsync.ContinueWith(_ => _onOfferDone!())` pulls inlet after offer completes
- **StubRouter pattern**: test actor that responds to `GetPoolRefs` with pre-built SinkRef+SourceRef; avoids TCP infrastructure in stream tests
- New stream tests: CS-001 (ConnectItem reaches SinkRef), CS-002 (DataItem reaches outlet) — both green
- Integration test files still compile (constructor still takes `IActorRef`); runtime fix in TASK-4B-007
- Build: 0 warnings, 0 errors; 2180 unit + 412 stream tests all green

### TASK-4B-004 Complete (2026-03-14)
- `PoolRouterActor` fully rewritten: materializes `MergeHub.Source<IDataItem>` + `SourceRef<IDataItem>` + `SinkRef<ITransportItem>` in `PreStart`
- `Sink.ForEach<ITransportItem>(item => self.Tell(item)).RunWith(StreamRefs.SinkRef<ITransportItem>(), mat)` pattern routes items to actor thread safely
- `ConnectItem` → derives HostKey from TcpOptions (Schema="http", Host, Port); creates HostPoolActor child via factory; `Forward`s item
- `DataItem` → routes by `item.Key`; drops with warning if HostKey.Default (no known host)
- `GetPoolRefs` buffers senders in `_pendingReplies` until both refs ready; replies immediately if already ready
- `HostStreamRefsReady` → subscribes host SourceRef into router's MergeHub (`msg.Source.Source.RunWith(_mergeHubSink!, _mat!)`)
- `hostFactory` constructor parameter (optional) enables test injection of `TestProbe` refs instead of real HostPoolActors
- Old messages removed: `RegisterHost`, `SendRequest`, `Response` — this required deleting `ConnectionPoolStage.cs` and `ConnectionPoolStageTests.cs` (advancing TASK-4B-006 work)
- `RoutedTransportItem` and `RoutedDataItem` removed from `ConnectionPoolTypes.cs`
- New tests: PR-001 (GetPoolRefs returns valid refs), PR-002 (ConnectItem forwarded to correct HostPoolActor via factory)
- Build: 0 warnings, 0 errors; 2180 unit + 410 stream tests all green

### TASK-4B-003 Complete (2026-03-14)
- `HostPoolActor` materializes `MergeHub.Source<IDataItem>` in `PreStart`, tells parent `HostStreamRefsReady` once SourceRef is ready
- `HandleRegisterConnectionRefs`: creates per-connection `Source.Queue<IDataItem>(128)`, wires queue → SinkRef → ConnectionActor outbound, wires ConnectionActor SourceRef → MergeHub, registers queue in `_connectionQueues`, calls `DrainPending`
- **Bug fixed**: removed `newConn.MarkBusy()` from spawn path in `HandleDataItem` — calling it before queue registration prevented `DrainPending` from routing (requires `Idle=true`)
- New tests: HA-001 (two SourceRefs → merged output), HA-002 (pending DataItem drained after RegisterConnectionRefs)
- **ActorRegistry pattern for ClientManager in tests**: `Context.GetActor<ClientManager>()` (from Servus.Akka) resolves via `ActorRegistry.For(system).Get<ClientManager>()`. In tests, register a TestProbe before creating any actor that calls `SpawnConnection()`: `ActorRegistry.For(Sys).Register<ClientManager>(probe.Ref)`. Requires `using Akka.Hosting;`. Then capture `CreateTcpRunner` directly from the probe's mailbox — do NOT rely on `UnhandledMessage` on the event stream.
- **OBSOLETE UnhandledMessage trick** (pre-TASK-002): `ConnectionActor` previously sent `CreateTcpRunner` to `Self` (HostPoolActor) which had no handler → `UnhandledMessage` on EventStream. This no longer works since `SpawnConnection()` now uses `Context.GetActor<ClientManager>()`.
- **HostPoolActorProxy pattern**: bidirectional proxy that routes child→parent messages to TestActor and external→proxy to child
- Build: 0 errors, 0 warnings; 2181 tests pass; PRA-004..007 pre-existing (PoolRouterActor SendRequest stub → TASK-4B-004)

### TASK-4B-002 Complete (2026-03-13)
- `ConnectionActor.HandleConnected` is now `async Task`: creates `Source.Queue<IDataItem>` + PreMaterialize, awaits `SourceRef`, creates `Sink.ForEachAsync`, awaits `SinkRef`, tells parent `RegisterConnectionRefs`
- `HandleSend(DataItem)` and `GetStreamRefs`/`StreamRefsResponse` handlers removed
- `PumpInbound` reads `_inbound` channel → `_responseQueue.OfferAsync(new DataItem(...))`
- Cascading TASK-4B-001 errors fixed with stubs: `ConnectionResponse` in HostPoolActor, `SendRequest`+`Response` in PoolRouterActor — all marked `// TODO TASK-4B-003/4B-004`
- ConnectionPoolStage restored to original logic (stubs allow it to compile)
- 11 CA tests all green (CA-016 and CA-017 added)
- Build: 0 warnings, 0 errors

### Parent Interception Pattern in Akka Tests
- `Context.Parent.Tell(...)` sends to hierarchical parent, NOT to TestActor
- Pattern: create a `ConnectionActorParent : ReceiveActor` that spawns `ConnectionActor` as child and `ReceiveAny(msg => forwardTo.Forward(msg))` — routes parent-bound messages to TestActor
- `TestProbe` type requires `using Akka.TestKit;` (not just `using Akka.TestKit.Xunit2;`)

## TurboClientOptions Policy Defaults (TASK-001)
- `RedirectPolicy`, `RetryPolicy`, `CachePolicy`, `ConnectionPolicy` all default to `null` (no initializer)
- DO NOT add `= *.Default` initializers — the documented contract requires null = "no policy configured"
- `PoolConfig` keeps its `= new PoolConfig()` default (not a policy)

## Build Notes
- `BenchmarkDotNet.Artifacts` also gitignored
- Engine.cs CS8509 warning fixed in TASK-048 (added default case to version switch)
- 02_FrameParsingTests.cs CS0219 warning fixed in TASK-048 (removed unused `newMax` variable)
- Zero warnings as of TASK-048

## Http2ProtocolSession Migration Pattern (TASK-PSS-001..006)

### Completed Migrations (ALL PSS-001..006 DONE)
- `03_StreamStateMachineTests.cs` → uses `Http2FrameDecoder`, `Http2Frame` subclasses, `HpackDecoder` directly
- `04_SettingsTests.cs` → uses `SettingsFrame`, `Http2FrameDecoder`, `SettingsParameter` directly
- `05_FlowControlTests.cs` → uses `WindowUpdateFrame`, `DataFrame`, `Http2FrameDecoder` directly (26 tests, RFC-9113-§6.9)
- `13_DecoderStreamFlowControlTests.cs` → uses `WindowUpdateFrame`, `DataFrame`, `Http2FrameDecoder` directly (6 tests, RFC-9113-§6.9)
- `07_ErrorHandlingTests.cs` → uses `RstStreamFrame`, `PingFrame`, `Http2FrameDecoder` directly (14 tests, RFC-9113-§6.4/§6.7)
- `08_GoAwayTests.cs` → uses `GoAwayFrame`, `Http2FrameDecoder` directly (17 tests, RFC-9113-§6.8)
- `Http2SecurityTests.cs` → flood enforcement helpers + `Http2FrameDecoder` (6 tests)
- `Http2FuzzHarnessTests.cs` → `AssertDecodeNeverCrashes()` wraps `Http2FrameDecoder.Decode()` (25 tests)
- `Http2ResourceExhaustionTests.cs` → explicit flood counters + `Http2FrameDecoder` (18 tests, down from 38)
- `Http2HighConcurrencyTests.cs` → independent decoder instances + explicit stream tracking (16 tests, down from 20)
- `Http2MaxConcurrentStreamsTests.cs` → `ExtractMaxConcurrentStreams()` + `EnforceMaxConcurrentStreams()` + `TrackStreamState()` (38 tests, down from 50)
- `Http2CrossComponentValidationTests.cs` → `DecodeHpackWithCompressionErrorWrapping()` + explicit enforcement (20 tests)

### PSS-007 Now Unblocked
- All 6 PSS files migrated → `Http2ProtocolSession.cs` and `Http2StreamLifecycleState.cs` can be deleted

### What Http2FrameDecoder validates directly (throw on parse):
- SETTINGS on non-zero stream → `Http2Exception(ProtocolError)`
- SETTINGS ACK with non-empty payload → `Http2Exception(FrameSizeError)`
- SETTINGS payload not multiple of 6 → `Http2Exception(FrameSizeError)`
- MAX_FRAME_SIZE outside [16384, 16777215] → `Http2Exception(ProtocolError)` (default error code)
- WINDOW_UPDATE payload != 4 bytes → `Http2Exception(FrameSizeError)`
- WINDOW_UPDATE increment = 0 → `Http2Exception(ProtocolError)`

### What Http2FrameDecoder passes through (caller must validate):
- ENABLE_PUSH > 1 → caller throws `Http2Exception(ProtocolError)`
- INITIAL_WINDOW_SIZE > 0x7FFFFFFF → caller throws `Http2Exception(FlowControlError)`
- Stream state transitions (Idle/Open/Closed) — tested in `03_StreamStateMachineTests.cs`
- Window overflow (connection or stream send window > 2^31-1) → belongs to Http20ConnectionStage, NOT tested in decoder tests

### WINDOW_UPDATE decoder behavior
- Reserved high bit of increment field (0x80000000) is stripped: `(int)(value & 0x7FFFFFFFu)`
- Valid increment range after stripping: 1..0x7FFFFFFF
- Stream 0 = connection-level; stream N = stream-level

### Pattern: private static helper methods in test file enforce RFC rules
```csharp
private static void EnforceEnablePush(IReadOnlyList<(SettingsParameter, uint)> parameters) { ... }
private static void EnforceInitialWindowSize(IReadOnlyList<(SettingsParameter, uint)> parameters) { ... }
```
Tests decode first, then call helpers, then assert `Http2Exception`.

---

## Test Counts (TASK-048 Baseline — 2026-03-12)
- Unit tests (TurboHttp.Tests): 2158
- Stream tests (TurboHttp.StreamTests): 421
- Integration tests (TurboHttp.IntegrationTests): 234
  - Http10: 46, Http11: 89, Http20: 66, Cross/Client/TLS/Edge: 46 (some overlap in filter)
- New stage tests: 83 (Cookie 12, Decompression 10, Cache 24, Redirect 15, Retry 12, ConnReuse 10)
- **Total: 2803 all green**
- Flaky timeouts when running all 3 projects simultaneously (resource contention); each project passes 100% individually

## Benchmark Status
- `TurboHttp.Benchmarks` project has infrastructure only (Config.cs, Program.cs)
- No `[Benchmark]` methods defined — cannot measure performance baseline
- RFC compliance matrix: `RFC_COMPLIANCE.md` in repo root

## StreamTest Infrastructure Audit (TASK-006, plan_1, 2026-03-19)

- `docs/test-infrastructure-audit.md` — full audit of Http10/ and Http11/ StreamTests
- 77 of 117 tests are pure encode/decode wrapped in ActorSystem — conversion candidates
- 38 tests legitimately exercise stream pipeline behaviour (engine round-trip, correlation, timing)
- Conversion pattern: replace `Source.Single(req).Via(EncoderStage).RunWith(Sink, Materializer)` with direct `Http10Encoder.Encode()` call
- Example conversions: `src/TurboHttp.Tests/RFC1945/18_EncoderStageConversionExampleTests.cs` (4 tests)
- Measured overhead: ~342 ms per test class for ActorSystem.Create(); plain tests < 1 ms
- `StreamTestBase` and `EngineTestBase` both extend `Akka.TestKit.Xunit2.TestKit`

## IO Actor Test Base Class (TASK-002, plan_1, 2026-03-19)

- `src/TurboHttp.StreamTests/IO/IoActorTestBase.cs` — abstract base class for all `HostPoolActor*` tests
- Provides: `TestOptions`, `Key10`/`Key11`/`Key20` (`RequestEndpoint`), `FakeConnectionActor` (inner class), `CreatePool(TestProbe, RequestEndpoint, TimeSpan?)`, `CreateHandle(IActorRef, RequestEndpoint)`, `SetupReadyPool(TestProbe, RequestEndpoint, TimeSpan?)`
- `HostPoolActorSelectConnectionTests` keeps its own `CreateHandle(Version)` overload (different signature — tests `SelectConnection` directly without a full pool actor)
- All four `HostPoolActor*` files inherit from `IoActorTestBase`; test count remains 17

## HostPoolActor Stale State Cleanup (TASK-5A-011, 2026-03-16)

### HandleFailure Changes
- `MarkDead()` is called before `_connections.Remove(conn)` — removal is immediate, not deferred to Reconnect timer
- `_activeHandle` is cleared in `HandleFailure` when the failed actor matches
- `HandleReconnect` simplified: just calls `SpawnConnection()` (no `Find()` needed)
- `HttpVersion` preservation across reconnects was dropped (replacement starts fresh)
- `HostPoolConfig` has `ConnectionFactory: Func<Props>?` for testability (bypasses `Context.GetActor<ClientManager>()`)
- Tests: `src/TurboHttp.StreamTests/IO/HostPoolActorTests.cs` (HPA-001, HPA-002)

### ConnectionActor Channel Lifecycle
- `_in` and `_out` are NOT readonly — reassigned on each reconnect
- `Reconnect()` calls `_in.Writer.TryComplete()` to signal stale `ConnectionHandle.InboundReader`
- `AttemptReconnect()` creates fresh `_in`/`_out` channels before calling `Connect()`
- This ensures: ConnectionStage detects end-of-stream → `_handle = null` → next ConnectItem re-acquires

## ConnectionActor Reconnect Logic (TASK-5A-010, 2026-03-16)

### Current State (poc2 branch)
- `ConnectionActor.Reconnect()` sends `HostPoolActor.ConnectionFailed(Self)` to parent
- Exponential backoff: `ReconnectInterval * 2^_reconnectAttempt` (capped at 60s)
- `_reconnectAttempt` increments on each failed reconnect, resets to 0 on `HandleConnected`
- `MaxReconnectAttempts` guard: logs warning and returns without scheduling when limit hit
- On reconnect success: `HandleConnected` sends `ConnectionReady(handle)` to parent
- Tests: `src/TurboHttp.StreamTests/IO/ConnectionActorTests.cs` (CA-001 through CA-005)
- `TestProbe` type requires `using Akka.TestKit;` (not just `using Akka.TestKit.Xunit2;`)

## TCP Error Tolerance Audit (TASK-AUD-004, 2026-03-15)

### Key Findings (SUPERSEDED by TASK-5A-010)
- Original finding: no backoff, ConnectionFailed never sent — FIXED in poc2
- See "ConnectionActor Reconnect Logic" section above for current state

## Connection Reuse Audit (TASK-AUD-003, 2026-03-15)

### Key Findings
- **`ConnectionReuseStage`** (`Streams/Stages/ConnectionReuseStage.cs`) exists and has 10 unit tests but is **NOT wired into Engine.cs** — dead code
- **No integration test class exists** in `src/TurboHttp.IntegrationTests/` beyond infrastructure (`KestrelFixture`, `Routes`, `TestKit`). The Http11/, Http20/ etc. test class directories from memory do NOT exist on disk in poc2 branch.
- Kestrel routes `/conn/keep-alive`, `/conn/close`, `/conn/default` are registered but never called by any test class
- **HTTP/1.1 keep-alive: ❌** — not empirically proven. Stage logic is correct (RFC 9112 compliant) but not integrated.
- **HTTP/2 multiplexing: ✅** (at stream/stage layer) — `Http20EngineRfcRoundTripTests` uses `SendH2EngineAsyncMany` to send 3 requests on streams 1,3,5 through one fake-TCP engine. Out-of-order correlation tested in `COR20-002`.
- **All stream tests use fake TCP stages** (`EngineFakeConnectionStage`, `H2EngineFakeConnectionStage`) — no real TCP socket involved.
- Full findings in `.maggus/PROGRESS_7.md`

## Engine.cs Wiring — Full Audit (TASK-AUD-001, 2026-03-15)

### Stages Wired in Engine.cs (direct)
| Stage | Role |
|-------|------|
| RequestEnricherStage | First in request chain |
| CookieInjectionStage | After redirect merge |
| CacheLookupStage | Last before engine core; Out0=miss, Out1=hit |
| DecompressionStage | First in response chain |
| CookieStorageStage | Stores Set-Cookie |
| CacheStorageStage | Stores cacheable responses |
| RetryStage | Out0=final, Out1→retry merge |
| RedirectStage | Out0=final, Out1→redirect merge |
| ConnectionStage | Transport bridge (production only) |

### Stages NOT Wired in Engine.cs
- `ConnectionReuseStage` — exists, tested, NEVER referenced in Engine.cs (dead code)
- `ExtractOptionsStage` — exists, superseded by RequestEnricherStage pattern
- `GroupByHostKeyStage` — exists but Engine.cs uses built-in `.GroupBy()` DSL
- `MergeSubstreamsStage` — exists but Engine.cs uses built-in `.MergeSubstreams()` DSL

### ConnectionPoolStage
- Does NOT exist in codebase. The actor pool (PoolRouterActor) is integrated via `ConnectionStage(poolRouter)`.

### Key Architecture Note
Production mode: `GroupBy(HostKey.FromRequest, maxSubstreams)` → per-host substream → `BuildConnectionFlowPublic` (Broadcast+ConnectItem+Concat+Buffer+BidiFlow+ConnectionStage). Test mode: factory replaces ConnectionStage.

### Full audit documented in `.maggus/PROGRESS_7.md`

## TurboHttpClient.SendAsync End-to-End Status (TASK-AUD-005, 2026-03-16)

### Key Findings
- **`SendAsync()` IS fully wired** — graph materialization in `TurboClientStreamManager` is ACTIVE (lines 59-62), NOT commented out
- **CLAUDE.md "Current Limitations" section is OUTDATED** — the following claims are no longer true:
  - "Pipeline not fully wired" → ALL handlers ARE wired via stages in `BuildExtendedPipeline`
  - "Client graph not materialized" → Graph IS materialized
  - "TurboHttpClient.SendAsync does not work end-to-end yet" → SendAsync IS implemented with request ID correlation
  - "No business logic stages" → 9 business logic stages exist and are wired
- **"No end-to-end integration tests"** — this claim IS still accurate
- **All `TurboClientOptions` features flow through the pipeline**: RedirectPolicy, RetryPolicy, CachePolicy (via stages), CookieJar (always on), Decompression (always on), PoolConfig (via PoolRouterActor)
- **NOT wired**: `ConnectionReuseStage` (dead code), `PerHostConnectionLimiter` (unit-tested only), `ExtractOptionsStage` (superseded)
- **Zero integration test classes exist** in `src/TurboHttp.IntegrationTests/` (only KestrelFixture infrastructure)
- **All 11 stream-tested stages have NO integration tests** against real HTTP servers
- Full findings in `.maggus/PROGRESS_7.md`

## Plan 5a — Hybrid Migration (TASK-5A-003, 2026-03-16)

### ConnectionReady Message Pattern
- `ConnectionActor.ConnectionReady(ConnectionHandle)` — nested sealed record, sent to parent alongside existing `RegisterConnectionRefs`
- Dual-path coexistence: both old (RegisterConnectionRefs + PumpInbound) and new (ConnectionReady + direct channels) active simultaneously
- `ConnectionHandle` wraps the same `OutboundWriter`/`InboundReader` from `ClientRunner.ClientConnected` — no copies
- Tests CA-019 (message sent) and CA-020 (channel roundtrip) verify the new path
- CA-006 updated to consume `ConnectionReady` messages (prevents queue pollution between reconnect cycles)

### HostPoolActor ConnectionHandle Forwarding (TASK-5A-003)
- `PoolRouterActor.HandleEnsureHost` now `Forward`s `EnsureHost` to `HostPoolActor` (preserves original Sender)
- `HostPoolActor` handles `EnsureHost`: replies with `_activeHandle` immediately if available, else queues `Sender` in `_pendingHandleRequesters`
- `HostPoolActor` handles `ConnectionActor.ConnectionReady`: stores `_activeHandle`, flushes all pending requesters
- Backward compat: fire-and-forget callers ignore the reply
- Tests: HA-003 (handle after connect), HA-004 (immediate reply), HA-005 (multiple requesters)
- PoolRouterActor tests (PR-001/002/003) updated to consume forwarded `EnsureHost` before expecting `DataItem`

### ConnectionStage Direct Channel I/O (TASK-5A-004)
- `ConnectionStage` no longer uses `GlobalRefs` / `_globalRequestQueue` / MergeHub response subscription
- On `ConnectItem`: sends `EnsureHost` to `PoolRouter` with `_stageActor.Ref`, awaits `ConnectionHandle` reply via `GetAsyncCallback<ConnectionHandle>`
- On `DataItem`: writes `(Memory, Length)` directly to `ConnectionHandle.OutboundWriter` (System.Threading.Channels)
- Inbound: async pump task reads from `ConnectionHandle.InboundReader`, invokes `_onInboundData` callback into stage event loop
- `_onInboundComplete` handles channel completion (connection dropped) — clears handle so next ConnectItem re-acquires
- `CancellationTokenSource` manages pump lifecycle; `StopInboundPump()` on upstream finish / downstream finish / PostStop
- Tests: CS-001 (EnsureHost), CS-002 (inbound), CS-003 (outbound), CS-004 (full round-trip) — all use stubbed `Channel<(IMemoryOwner<byte>, int)>` pairs
- **6 pre-existing failures in ExtractOptionsStageTests** (Cannot push port twice) — unrelated to this task

### PoolRouterActor Data-Routing Removal (TASK-5A-005)
- Removed: `_globalRequestQueue` (Source.Queue), `_globalMergeHubSink`/`_globalResponseSource` (MergeHub), `_mat`, `PreStart()`, `GetGlobalRefs`/`GlobalRefs` messages, `HandleGetGlobalRefs()`, `HandleRegisterHostResponseSource()`, `HandleDataItem()`, `Receive<DataItem>`
- Kept: `EnsureHost` message + handler, `RegisterHostResponseSource` record type (HostPoolActor still sends it — will be removed in TASK-5A-006)
- `PoolRouterActor` reduced from 127 → 67 lines; only handles `EnsureHost` lifecycle forwarding
- PoolRouterActorTests: removed DataItem routing + GlobalRefs tests (PR-001/002/003 simplified, PR-004 deleted)
- ActorHierarchyStreamRefTests: removed `GetGlobalRefs` init check (no longer needed)
- `RegisterHostResponseSource` from HostPoolActor becomes unhandled/dead letter — acceptable during migration

### HostPoolActor Data-Routing Removal (TASK-5A-006)
- Removed: `_connectionQueues`, `_pending`, `_mat`, `_mergeHubSink`, `_responseSource`, `HandleDataItem()`, `HandleRegisterConnectionRefs()`, `DrainPending()`, `SelectConnectionWithQueue()`, MergeHub in PreStart, `RegisterHostResponseSource` tell, `StreamComplete` message, `Receive<RegisterConnectionRefs>`, `Receive<DataItem>`
- `RegisterHostResponseSource` removed from PoolRouterActor (no longer sent)
- HostPoolActorTests: HA-001 (MergeHub) and HA-002 (DataItem routing) removed; HA-003/004/005 updated
- `HostPoolActor` now ~100 lines; handles only: ConnectionIdle, ConnectionFailed, IdleCheck, Reconnect, MarkConnectionNoReuse, ConnectionReady, EnsureHost

### ConnectionActor Legacy Path Removal (TASK-5A-007)
- Removed: `PumpInbound()` async task, `_responseQueue` (ISourceQueueWithComplete<DataItem>), `_cts` (CancellationTokenSource), `HandleOutboundDataItem`/`Receive<DataItem>`, `RegisterConnectionRefs` send, Source.Queue materialization, `using Akka.Streams`/`using Akka.Streams.Dsl`
- Removed from HostPoolActor: `RegisterConnectionRefs` message type (sealed record)
- `ConnectionActor` now ~95 lines; handles only: ClientConnected, ClientDisconnected, Terminated
- On connect: sends `ConnectionReady(ConnectionHandle)` to parent (the ONLY message to parent)
- On reconnect: nulls `_runner`/`_outbound`/`_inbound`, calls `Connect()` — old channel handles become stale (ClientRunner completes channels on TCP drop)
- Tests: removed CA-003/011/013/016/017/018; updated CA-006; added CA-021 (DataItem not handled)
- **Dual-path coexistence is FULLY removed** — only the direct-channel path remains

### ConnectionReuseStage Feedback Wiring (TASK-5A-008)
- `Engine.BuildConnectionFlowPublic`: replaced `Sink.Ignore<IControlItem>()` with `MergePreferred<IOutputItem>` feedback loop — signal from `ConnectionReuseStage.Out1` routes back through transport via buffer (same cycle-breaking pattern as retry/redirect)
- `ConnectionStage.HandlePush()`: handles `ConnectionReuseItem` — sends `MarkConnectionNoReuse(connectionActor)` to ConnectionActor when `CanReuse=false`; no-op for `CanReuse=true`
- `ConnectionActor`: added `Receive<HostPoolActor.MarkConnectionNoReuse>` that forwards to `Context.Parent` (HostPoolActor)
- `HostPoolActor.EvictIdleConnections`: now also closes non-reusable idle connections; invalidates `_activeHandle` when evicted connection was active
- Signal path: ConnectionReuseStage → Buffer(1) → MergePreferred → ConnectionStage → ConnectionActor → HostPoolActor
- Tests: HA-006 (eviction), HA-007 (forwarding), CS-005 (CanReuse=false), CS-006 (CanReuse=true)

## Architecture Decision (TASK-DEC-001, 2026-03-16)

### Decision: Option A — Evolve Current Actor Pool (RECOMMENDED, awaiting user confirmation)

### Key Architecture Facts
- **Actor Pool IS the status quo** — PoolRouterActor → HostPoolActor → ConnectionActor is fully integrated via ConnectionStage
- **ConnectionStage protocol**: `GetGlobalRefs` → receives `GlobalRefs(RequestQueue, ResponseSource)`. ConnectItems go via `EnsureHost`. DataItems go via `OfferAsync` to global queue.
- **PoolRouterActor is single-threaded** — all DataItems from all hosts pass through one actor mailbox for key→host routing. Theoretical bottleneck, no benchmark evidence.
- **MergeHub at two levels** — PoolRouterActor (global response aggregation) and HostPoolActor (per-host response aggregation). Provides failure isolation.
- **3 actor hops per request** in hot path: ConnectionStage → PoolRouterActor → HostPoolActor → ConnectionActor

### Recommended Next Steps (6 tasks, ~2-3 days)
1. Wire ConnectionReuseStage into BuildConnectionFlowPublic (S)
2. Fix ConnectionActor.Reconnect() — send ConnectionFailed, add backoff (M)
3. Fix stale queue cleanup in HostPoolActor on ConnectionFailed (S)
4. Wire PerHostConnectionLimiter in HostPoolActor.SpawnConnection() (S)
5. Write integration tests against Kestrel fixtures (M)
6. Update CLAUDE.md "Current Limitations" section (S)

### Full analysis in `.maggus/ARCHITECTURE_DECISION.md`

## RFC Test Reorganisation (plan_6)

### Status (as of TASK-RFC-004)
- `Integration/RedirectHandlerTests.cs` → `RFC9110/01_RedirectHandlerTests.cs` ✅
- `Integration/RetryEvaluatorTests.cs` → `RFC9110/02_RetryEvaluatorTests.cs` ✅
- `Integration/ConnectionReuseEvaluatorTests.cs` → `RFC9112/22_ConnectionReuseTests.cs` ✅
- `Integration/PerHostConnectionLimiterTests.cs` → `RFC9112/23_PerHostLimiterTests.cs` ✅
- `Integration/CookieJarTests.cs` → `RFC6265/01_CookieJarTests.cs` ✅
- `Integration/CrossFeatureIntegrityTests.cs` → DELETED ✅
- `Integration/HttpDecodeErrorMessagesTests.cs` → DELETED ✅
- `Integration/Phase60ValidationGateTests.cs` → DELETED ✅
- `Integration/TurboClientOptionsTests.cs` → DELETED ✅
- **`src/TurboHttp.Tests/Integration/` folder DOES NOT EXIST** ✅
- Namespace for RFC9110 pair: `TurboHttp.Tests.RFC9110`
- Namespace for RFC9112 pair: `TurboHttp.Tests.RFC9112`
- Namespace for RFC6265: `TurboHttp.Tests.RFC6265`
- New folder `src/TurboHttp.Tests/RFC6265/` contains 59 cookie tests (CM-001..CM-042)

### RFC9110 Test Coverage
- `01_RedirectHandlerTests.cs` — RFC 9110 §15.4 redirects (51 tests, RH-001..051)
- `02_RetryEvaluatorTests.cs` — RFC 9110 §9.2 retries (40 tests, RE-001..040)
- `03_ContentEncodingIntegrationTests.cs` — stacked encodings (27 tests)

## RFC9113 Single-Layer Test Pattern (TASK-005, plan_1, 2026-03-19)

- Frame structure tests: use `encoder.Encode(request, streamId)` → assert on frame object properties (EndStream, EndHeaders, IsType)
- Header content tests: use `encoder.EncodeToHpackBlock(request)` → `HpackDecoder.Decode()` for verification
- Flow control tests: `frames.OfType<DataFrame>().Sum(df => df.Data.Length)` instead of manual byte parsing
- HPACK-specific tests (NeverIndex, dynamic table, raw byte walker): belong in RFC7541/, not RFC9113/
- 35 tests moved from `RFC9113/23_EncoderSensitiveHeaderTests.cs` → `RFC7541/07_SensitiveHeaderTests.cs`
- Multi-layer helpers (`Encode()` serialize-to-bytes, `ExtractFirstHeaderBlock()`, byte-level `DecodeHeaderList()`) replaced with `EncodeToFrames()` and `DecodeHeaders()` single-layer helpers

## H2EngineFakeConnectionStage Unlock Constraint (TASK-003, 2026-03-18)

- The fake stage gates server frame delivery: 1 unlock per `ConnectItem` or `DataItem` received on In
- `IControlItem` (e.g. `StreamAcquireItem`, `MaxConcurrentStreamsItem`) does NOT unlock — adding unlock for these causes race regression (server response arrives before client DATA written to OutboundChannel)
- For multi-frame server responses (e.g. HEADERS + DATA), concatenate into single byte[] so only 1 unlock is consumed
- GET requests produce exactly 2 DataItems: client HEADERS + SETTINGS ACK (no ConnectItem without ExtractOptionsStage)
- POST requests produce 3+ DataItems: client HEADERS + SETTINGS ACK + client DATA
