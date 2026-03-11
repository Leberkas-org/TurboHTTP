# Iteration 01 — TASK-011: TurboClientStreamManager Activation

## Task
**ID:** TASK-011
**Title:** TurboClientStreamManager Activation
**Goal:** Materialize the full Akka.Streams graph in TurboClientStreamManager so that TurboHttpClient.SendAsync works end-to-end.

## Changes Made

### `src/TurboHttp/Client/TurboClientStreamManager.cs`
- Replaced commented-out graph code with full materialization
- Constructor now accepts `TurboClientOptions` alongside the existing `Func<TurboRequestOptions>` factory and `ActorSystem`
- Creates a `ClientManager` actor for TCP connection lifecycle
- Builds the pipeline via `Engine.CreateFlow(clientManager, clientOptions)` which conditionally inserts cookie/cache/redirect/retry/decompression stages based on feature flags
- Bridges System.Threading.Channels to Akka.Streams using `Source.Queue` (input) and `Sink.ForEach` (output)
- Background `PumpRequestsAsync` task reads from the channel reader and offers to the Akka queue
- Removed unused `StreamInstance` actor class

### `src/TurboHttp/Client/TurboHttpClient.cs`
- Updated `TurboClientStreamManager` constructor call to pass `clientOptions` as the first parameter

## Commands Run
1. `dotnet build --configuration Release src/TurboHttp.sln` → **Build succeeded** (0 errors, 2 pre-existing warnings)

## Acceptance Criteria Status
- [x] File modified: `src/TurboHttp/Client/TurboClientStreamManager.cs`
- [x] Uncommented and completed graph materialization code
- [x] Wired: ChannelSource → Engine.CreateFlow(options) → ChannelSink
- [x] Passes CookieJar, HttpCacheStore instances from TurboClientOptions or creates defaults (handled internally by Engine.BuildExtendedPipeline)
- [x] CookieJar — one per TurboHttpClient instance (created per CreateFlow call)
- [x] HttpCacheStore — one per TurboHttpClient instance (created per CreateFlow call)
- [x] RedirectHandler — one per pipeline (created per CreateFlow call)
- [x] ⚠️ BLOCKED: PerHostConnectionLimiter — no Akka.Streams stage exists to consume it. The standalone class exists and is tested, but no pipeline stage wires it in.
- [x] `dotnet build --configuration Release src/TurboHttp.sln` succeeds with zero errors

## Deviations
- **PerHostConnectionLimiter**: Marked as BLOCKED. The class exists at `src/TurboHttp/Protocol/PerHostConnectionLimiter.cs` with full unit tests, but there is no corresponding Akka.Streams stage to integrate it into the Engine pipeline. A future task should create a throttling/connection-limiting stage.
- **RequestEnricher placement**: The acceptance criteria described `ChannelSource → RequestEnricher → Engine.CreateFlow`. Since `Engine.CreateFlow` already includes `RequestEnricherStage` internally, adding another one externally would cause double-enrichment. The enricher is handled inside `Engine.CreateFlow` as designed.
