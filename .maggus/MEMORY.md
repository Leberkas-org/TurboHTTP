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

## TCP Error Tolerance Audit (TASK-AUD-004, 2026-03-15)

### Key Findings
- **`ConnectionActor.HandleDisconnected`** calls `Reconnect()` → `Connect()` **immediately** (zero delay) on `ClientDisconnected` or actor `Terminated`
- **Stream graph survives** (MergeHub at `HostPoolActor` + `PoolRouterActor` level insulates individual drops); **in-flight requests on the dropped connection are silently lost**
- **No failure propagation to parallel connections** — MergeHub isolation
- **No exponential backoff** — `PoolConfig.ReconnectInterval` (5s) and `MaxReconnectAttempts = 3` are dead config; `ConnectionFailed` is **never sent** from `ConnectionActor` to `HostPoolActor`
- `HostPoolActor.HandleFailure` is unreachable dead code in current implementation
- During reconnect gap, stale `_connectionQueues[conn.Actor]` entry can receive new requests that will throw `NullReferenceException` (from null `_outbound`)

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
