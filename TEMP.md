# TurboHttp — Master Task List for Ralph

**Author:** Claude
**Date:** 2026-03-09

---

## Overview

This document consolidates three independent work streams:

| # | Work Stream | Goal |
|---|-------------|------|
| **WS-1** | Remove `Http2ProtocolSession` | Delete the internal test-only session class; migrate all 25 dependent test files to real production classes |
| **WS-2** | Streams-Stage Integration Tests | Test the full Akka Streams pipeline (`HostConnectionPool → Engine → ConnectionStage → TCP`) against a real Kestrel server |
| **WS-3** | RFC Coverage Gaps | Fill concrete holes in the existing test suite where RFC sections have no dedicated tests |

These three work streams are **independent** and can be done in any order or in parallel.

---

---

# Work Stream 1 — Remove `Http2ProtocolSession`

**Goal:** Delete `Http2ProtocolSession.cs`. All 25 dependent test files will be migrated.
**Result:** Same RFC coverage, verified against real production classes only.

Source context: `TEST.md`
Stage tests: `src/TurboHttp.StreamTests/Http20/`
Unit tests: `src/TurboHttp.Tests/RFC9113/`

---

## Migration Strategy — Four Paths

| Path | When | Replacement |
|------|------|-------------|
| **A — FrameDecoder direct** | Test checks only frames or exceptions, no HTTP responses | `new Http2FrameDecoder().Decode(bytes)` |
| **B — CompletionDecoder** | Test checks `session.Responses` (full `HttpResponseMessage`) | `Http2CompletionDecoder.Process(bytes)` |
| **C — New Stage Test** | Test checks connection-level state (GOAWAY, MaxStreams, flood protection, flow control) | `Source.From(...).Via(stage).RunWith(...)` |
| **D — Delete** | Test checks only session-internal fields with no RFC basis | Test is removed without replacement |

---

## Phase A — FrameDecoder Direct (unit tests, mechanical migration)

Migration pattern:
```csharp
// Before:  session.Process(raw)  throws Http2Exception
// After:   decoder.Decode(raw)   throws Http2Exception

var decoder = new Http2FrameDecoder();
var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(raw));
Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
```

### `02_FrameParsingTests.cs` — FP-016..FP-020 (5 tests via session)

- [ ] **A-01** FP-016: SETTINGS on non-zero stream → replace Session.Process() with FrameDecoder.Decode() — RFC 9113 §6.5
- [ ] **A-02** FP-017: PING on non-zero stream → replace — RFC 9113 §6.7
- [ ] **A-03** FP-018: GOAWAY on non-zero stream → replace — RFC 9113 §6.8
- [ ] **A-04** FP-019: WINDOW_UPDATE stream 0 accepted → replace — RFC 9113 §6.9
- [ ] **A-05** FP-020: WINDOW_UPDATE stream N accepted → replace — RFC 9113 §6.9

### `04_SettingsTests.cs` — SETTINGS validation

- [ ] **A-06** SS-001..SS-005: Connection-Preface SETTINGS bytes — raw FrameDecoder.Decode() instead of session — RFC 9113 §3.5
- [ ] **A-07** SS-006: MaxFrameSize=16383 is PROTOCOL_ERROR — RFC 9113 §6.5.2
- [ ] **A-08** SS-007: MaxFrameSize=16777216 is PROTOCOL_ERROR — RFC 9113 §6.5.2
- [ ] **A-09** SS-009: InitialWindowSize overflow → FLOW_CONTROL_ERROR — RFC 9113 §6.5.2
- [ ] **A-10** SS-010: InitialWindowSize=2^31-1 accepted — RFC 9113 §6.5.2
- [ ] **A-11** SS-011: EnablePush=0 accepted — RFC 9113 §6.5.2
- [ ] **A-12** SS-012: EnablePush=1 accepted — RFC 9113 §6.5.2
- [ ] **A-13** SS-013: EnablePush=2 is PROTOCOL_ERROR — RFC 9113 §6.5.2
- [ ] **A-14** SS-019: Non-ACK SETTINGS → 1 ACK to send (check SettingsFrame output) — RFC 9113 §6.5
- [ ] **A-15** SS-020: SETTINGS ACK → no new ACK in return — RFC 9113 §6.5
- [ ] **A-16** SS-021: Three SETTINGS → three ACK frames (via decoder output count) — RFC 9113 §6.5
- [ ] **A-17** SS-022: Empty SETTINGS → ACK required — RFC 9113 §6.5
- [ ] **A-18** SS-023: Encoded SETTINGS ACK is a valid 9-byte frame — RFC 9113 §6.5
- [ ] **A-19** SS-027: Unknown SETTINGS parameter silently ignored — RFC 9113 §6.5
- [ ] **A-20** SS-028: Multiple parameters in one frame all applied — RFC 9113 §6.5
- [ ] **A-21** SS-029: InitialWindowSize increase + open stream overflow — RFC 9113 §6.9.2

### `07_ErrorHandlingTests.cs` — Error code mapping

- [ ] **A-22** EM-001..EM-010: Connection error → `IsConnectionError = true` (10 tests) — RFC 9113 §5.4.1
- [ ] **A-23** EM-011..EM-020: Stream error → `IsConnectionError = false`, correct StreamId (10 tests) — RFC 9113 §5.4.2
- [ ] **A-24** EM-021..EM-025: PROTOCOL_ERROR vs FLOW_CONTROL_ERROR vs FRAME_SIZE_ERROR (5 tests) — RFC 9113 §6.1 §6.4

### `14_DecoderErrorCodeTests.cs` — Wire format of error codes

- [ ] **A-25** DC-001..DC-010: Error code uint32 big-endian in RST_STREAM and GOAWAY (10 tests) — RFC 9113 §6.4 §6.6

### `08_GoAwayTests.cs` — GOAWAY frame-parsing portion

- [ ] **A-26** GA-Frame tests: GOAWAY frame field parsing (LastStreamId, ErrorCode, debug data) without `session.IsGoingAway` → FrameDecoder — RFC 9113 §6.8

### `11_DecoderStreamValidationTests.cs` — stream validation (exception-only tests)

- [ ] **A-27** SVL-Exception tests: All tests that only check Http2Exception → FrameDecoder direct — RFC 9113 §5.1 §6.1

### `09_ContinuationFrameTests.cs` — exception portion

- [ ] **A-28** CF-004: CONTINUATION on wrong stream → PROTOCOL_ERROR — RFC 9113 §6.10
- [ ] **A-29** CF-005: Non-CONTINUATION after HEADERS-without-END_HEADERS → PROTOCOL_ERROR — RFC 9113 §6.10
- [ ] **A-30** CF-006: Orphaned CONTINUATION → PROTOCOL_ERROR — RFC 9113 §6.10
- [ ] **A-31** CF-007..CF-016: Flood protection, partial blocks exception tests → FrameDecoder — RFC 9113 §6.10

### `03_StreamStateMachineTests.cs` — exception portion (DATA on idle stream etc.)

- [ ] **A-32** SM-Exception tests: All tests that only check Http2Exception on illegal stream transitions → FrameDecoder direct — RFC 9113 §5.1

---

## Phase B — CompletionDecoder (unit tests, full HTTP response)

Migration pattern:
```csharp
// Before
var session = new Http2ProtocolSession();
session.Process(headersBytes);
session.Process(dataBytes);
Assert.Equal(HttpStatusCode.OK, session.Responses[0].StatusCode);

// After
var decoder = new Http2CompletionDecoder();
decoder.Process(headersBytes);
decoder.Process(dataBytes);
var response = decoder.TryGetResponse(streamId: 1);
Assert.NotNull(response);
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

### `15_RoundTripHandshakeTests.cs` (~18 tests)

- [ ] **B-01** RT-HND-001..RT-HND-018: Round-trip handshake tests → Http2CompletionDecoder — RFC 9113 §3.5 §6.5

### `16_RoundTripMethodTests.cs` (~18 tests)

- [ ] **B-02** RT-MTH-001..RT-MTH-018: GET, POST, PUT, DELETE, HEAD round-trips → Http2CompletionDecoder — RFC 9113 §8.3

### `17_RoundTripHpackTests.cs` (~18 tests)

- [ ] **B-03** RT-HPK-001..RT-HPK-018: HPACK round-trips → Http2CompletionDecoder — RFC 7541 §2–7

### `06_HeadersTests.cs` — response portion

- [ ] **B-04** HDR-Response tests: Tests that check `session.Responses` → Http2CompletionDecoder — RFC 9113 §6.2

### `09_ContinuationFrameTests.cs` — response portion (CF-001..CF-003)

- [ ] **B-05** CF-001: HEADERS with END_HEADERS → full response — RFC 9113 §6.10
- [ ] **B-06** CF-002: HEADERS + CONTINUATION → full response after reassembly — RFC 9113 §6.10
- [ ] **B-07** CF-003: HEADERS + 3x CONTINUATION → full response — RFC 9113 §6.10

### `01_ConnectionPrefaceTests.cs` — session.Responses portion

- [ ] **B-08** CP-Session tests: Tests that check `session.ReceivedSettings` / `session.Responses` → Http2CompletionDecoder or FrameDecoder direct — RFC 9113 §3.4

---

## Phase C — New Stage Tests

Shared helper method in `StreamTestBase`:
```csharp
private static (IMemoryOwner<byte>, int) Chunk(byte[] data)
    => (new SimpleMemoryOwner(data), data.Length);

protected async Task<IReadOnlyList<Http2Frame>> DecodeFramesAsync(params byte[][] chunks)
{
    var source = Source.From(chunks.Select(Chunk));
    return await source
        .Via(Flow.FromGraph(new Stages.Http2FrameDecoderStage()))
        .RunWith(Sink.Seq<Http2Frame>(), Materializer);
}
```

### C-1: `Http2FlowControlStageTests.cs` — RFC 9113 §6.9

Replaces: `05_FlowControlTests.cs` + `13_DecoderStreamFlowControlTests.cs`

- [ ] **C-01** ST_20_FC_001: Connection-level WINDOW_UPDATE (stream 0) passes through stage — RFC 9113 §6.9
- [ ] **C-02** ST_20_FC_002: Stream-level WINDOW_UPDATE (stream N) passes through stage — RFC 9113 §6.9
- [ ] **C-03** ST_20_FC_003: WINDOW_UPDATE increment field decoded correctly — RFC 9113 §6.9.1
- [ ] **C-04** ST_20_FC_004: Maximum increment value (2^31-1) decoded correctly — RFC 9113 §6.9.1
- [ ] **C-05** ST_20_FC_005: WINDOW_UPDATE with zero increment decoded (value=0) — RFC 9113 §6.9.1
- [ ] **C-06** ST_20_FC_006: HEADERS then WINDOW_UPDATE in one chunk: order preserved — RFC 9113 §6.9
- [ ] **C-07** ST_20_FC_007: WINDOW_UPDATE split across two TCP chunks reassembled — RFC 9113 §6.9
- [ ] **C-08** ST_20_FC_008: Three WINDOW_UPDATE frames decoded as three distinct frames — RFC 9113 §6.9

### C-2: `Http2SecurityStageTests.cs` — RFC 9113 §6.4 / CVE-2023-44487

Replaces: `Http2SecurityTests.cs` + `Http2ResourceExhaustionTests.cs`

- [ ] **C-09** ST_20_SEC_001: 100 RST_STREAM frames decoded without truncation (Rapid Reset baseline) — RFC 9113 §6.4 / CVE-2023-44487
- [ ] **C-10** ST_20_SEC_002: 100 CONTINUATION frames after HEADERS-without-END_HEADERS decoded — RFC 9113 §6.10
- [ ] **C-11** ST_20_SEC_003: 100 empty DATA frames (END_STREAM=false) decoded without loss — RFC 9113 §6.1
- [ ] **C-12** ST_20_SEC_004: 50 SETTINGS frames decoded without suppression — RFC 9113 §6.5
- [ ] **C-13** ST_20_SEC_005: HEADERS + RST_STREAM on same stream both decoded — RFC 9113 §6.4
- [ ] **C-14** ST_20_SEC_006: Interleaved RST_STREAM from multiple streams: StreamId preserved — RFC 9113 §6.4
- [ ] **C-15** ST_20_SEC_007: 50 SETTINGS + 50 SETTINGS-ACK decoded in correct order — RFC 9113 §6.5
- [ ] **C-16** ST_20_SEC_008: Empty DATA + WINDOW_UPDATE decoded in correct order — RFC 9113 §6.1 §6.9

### C-3: `Http2ConcurrentStreamsStageTests.cs` — RFC 9113 §6.5.2

Replaces: `Http2MaxConcurrentStreamsTests.cs`

- [ ] **C-17** ST_20_MCS_001: SETTINGS with MAX_CONCURRENT_STREAMS=1 decoded correctly — RFC 9113 §6.5.2
- [ ] **C-18** ST_20_MCS_002: SETTINGS with MAX_CONCURRENT_STREAMS=100 decoded correctly — RFC 9113 §6.5.2
- [ ] **C-19** ST_20_MCS_003: SETTINGS with MAX_CONCURRENT_STREAMS=0 decoded (zero is legal) — RFC 9113 §6.5.2
- [ ] **C-20** ST_20_MCS_004: SETTINGS with MAX_CONCURRENT_STREAMS=2^32-1 decoded correctly — RFC 9113 §6.5.2
- [ ] **C-21** ST_20_MCS_005: Two SETTINGS updating MAX_CONCURRENT_STREAMS decoded in order — RFC 9113 §6.5
- [ ] **C-22** ST_20_MCS_006: SETTINGS with multiple params including MAX_CONCURRENT_STREAMS — RFC 9113 §6.5

### C-4: `Http2GoAwayStageTests.cs` — RFC 9113 §6.8

Replaces: `08_GoAwayTests.cs` state portion (`session.IsGoingAway`)

- [ ] **C-23** ST_20_GA_001: GOAWAY with NO_ERROR decoded correctly — RFC 9113 §6.8
- [ ] **C-24** ST_20_GA_002: GOAWAY with PROTOCOL_ERROR decoded correctly — RFC 9113 §6.8
- [ ] **C-25** ST_20_GA_003: GOAWAY with non-zero LastStreamId decoded correctly — RFC 9113 §6.8
- [ ] **C-26** ST_20_GA_004: GOAWAY with debug data: optional bytes accessible — RFC 9113 §6.8
- [ ] **C-27** ST_20_GA_005: GOAWAY split across two TCP chunks reassembled — RFC 9113 §6.8
- [ ] **C-28** ST_20_GA_006: HEADERS followed by GOAWAY decoded in order — RFC 9113 §6.8

---

## Phase D — Delete (no RFC basis)

- [ ] **D-01** Delete `Http2HighConcurrencyTests.cs` — checks only `session.ActiveStreamCount` (session field, not RFC)
- [ ] **D-02** Delete `Http2CrossComponentValidationTests.cs` — cross-validates session properties with no production equivalent
- [ ] **D-03** Review `Http2FuzzHarnessTests.cs` — rewrite session calls as FrameDecoder direct, or delete if no RFC value remains
- [ ] **D-04** Delete state portion of `03_StreamStateMachineTests.cs` — tests that only check `session.GetStreamState()` (no RFC requirement)

---

## Phase E — External files + delete Http2ProtocolSession

- [ ] **E-01** `Integration/TcpFragmentationTests.cs` — identify session calls and migrate to FrameDecoder or stage
- [ ] **E-02** `RFC9110/01_ContentEncodingGzipTests.cs` — identify session calls and migrate
- [ ] **E-03** `RFC9110/02_ContentEncodingDeflateTests.cs` — identify session calls and migrate
- [ ] **E-04** Verify build: `dotnet build src/TurboHttp.sln` — zero errors, zero session references
- [ ] **E-05** Delete `src/TurboHttp.Tests/Http2ProtocolSession.cs`
- [ ] **E-06** Run full test suite: `dotnet test src/TurboHttp.sln` — all tests green

---

## WS-1 Acceptance Criteria

- [ ] `dotnet test src/TurboHttp.Tests` — all tests green
- [ ] `dotnet test src/TurboHttp.StreamTests` — all tests green
- [ ] `Http2ProtocolSession.cs` no longer exists
- [ ] No `using` reference to `Http2ProtocolSession` anywhere in the solution (grep confirms 0 matches)
- [ ] RFC coverage equal or better than before — no RFC section lost
- [ ] All new stage tests have `DisplayName` starting with `"RFC-9113-§…:"`
- [ ] Stage tests use only `Flow.FromGraph(new Stages.*)` — no FrameDecoder direct calls inside stage test files

## WS-1 Open Questions

1. **`Http2FrameDecoder.Decode()` signature** — Returns `IReadOnlyList<Http2Frame>` or single frame? Check `src/TurboHttp/Protocol/Http2FrameDecoder.cs` before Phase A.
2. **`Http2CompletionDecoder` API** — Is `TryGetResponse(int streamId)` the correct method name? Check `Http2CompletionDecoder.cs` before Phase B.
3. **`SettingsParameter.MaxConcurrentStreams`** — Confirm enum value name.
4. **`GoAwayFrame.DebugData`** — Type is `ReadOnlyMemory<byte>` or `byte[]` after decode?
5. **`03_StreamStateMachineTests.cs`** — Read file before Phase A/D: which tests only throw Http2Exception (Path A), which only check `GetStreamState()` (Path D)?

---

---

# Work Stream 2 — Streams-Stage Integration Tests

## Problem Statement

The current test pyramid has a structural gap. Three projects exist, but they do not form a continuous chain:

| Layer | Project | What is actually tested |
|-------|---------|-------------------------|
| Protocol | `TurboHttp.Tests` | `Http11Encoder`, `Http11Decoder`, HPACK, etc. — pure algorithm, no I/O |
| Stream (fake) | `TurboHttp.StreamTests` | GraphStages wired with `EngineFakeConnectionStage` — no real TCP |
| Integration (raw) | `TurboHttp.IntegrationTests` | Real Kestrel via `Http11Connection`/`Http2Connection` helpers — Akka is **not involved at all** |

**What has never been tested end-to-end:**

```
HostConnectionPool
  └─ Source.Queue<HttpRequestMessage>
       └─ Engine.CreateFlow(clientManager, tcpOptions)
            └─ Partition(4) → Http11Engine BidiFlow
                 └─ ConnectionStage
                      └─ ClientManager actor
                           └─ ClientRunner actor
                                └─ TcpClientProvider.GetStream() → real TCP socket
                                     └─ Kestrel
```

---

## Architecture Notes

**`HostConnectionPool` constructor** — no injection point for `IClientProvider` today:
```csharp
public HostConnectionPool(TcpOptions options, ActorSystem system, Action<HttpResponseMessage> onResponse)
```

**`ClientManager.CreateTcpRunner`** — injection point exists here:
```csharp
public sealed record CreateTcpRunner(
    TcpOptions Options,
    IActorRef Handler,
    IClientProvider? StreamProvider = null);   // ← inject here for fault tests
```

**`ConnectionStage` constructor:**
```csharp
public ConnectionStage(IActorRef clientManager, TcpOptions options)
```

**`IClientProvider` interface:**
```csharp
public interface IClientProvider
{
    EndPoint? RemoteEndPoint { get; }
    Stream GetStream();
    void Close();
}
```

**Fault injection strategy:** Use a `TestClientManagerProxy` actor (TASK-INF-04) that intercepts `CreateTcpRunner` and injects a `FaultInjectingClientProvider` before forwarding to the real `ClientManager`. No production code changes needed.

---

## Where the tests live

All WS-2 tests go into **`src/TurboHttp.IntegrationTests/Streams/`**. This project already has `KestrelFixture` and `KestrelH2Fixture` — there is no reason to rebuild them in a different project.

**Existing infrastructure (reuse as-is, no changes):**
| What | Location |
|------|----------|
| `KestrelFixture` (HTTP/1.x Kestrel + all routes) | `src/TurboHttp.IntegrationTests/Shared/KestrelFixture.cs` |
| `KestrelH2Fixture` (HTTP/2 h2c Kestrel + h2 routes) | `src/TurboHttp.IntegrationTests/Shared/KestrelH2Fixture.cs` |
| `[Collection("Http11Integration")]` definition | `src/TurboHttp.IntegrationTests/Http11/Http11Collection.cs` |
| `[Collection("Http2Integration")]` definition | `src/TurboHttp.IntegrationTests/Http2/Http2Collection.cs` |

**New files to create (only what does not already exist):**

```
src/TurboHttp.IntegrationTests/
└── Streams/
    ├── Shared/
    │   ├── ActorSystemFixture.cs               [TASK-INF-01]
    │   ├── StreamsTestClient.cs                [TASK-INF-02]
    │   ├── StreamsTestClientH2.cs              [TASK-INF-03]
    │   ├── TestClientManagerProxy.cs           [TASK-INF-04]
    │   └── FaultInjectingClientProvider.cs     [TASK-INF-05]
    ├── ConnectionStage/
    │   ├── ConnectionStageConnectTests.cs      [TASK-CS-01]
    │   ├── ConnectionStageDataFlowTests.cs     [TASK-CS-02]
    │   └── ConnectionStageReconnectTests.cs    [TASK-CS-03]
    ├── Http10/
    │   └── Http10EngineIntegrationTests.cs     [TASK-ENG-01]
    ├── Http11/
    │   ├── Http11EngineIntegrationTests.cs     [TASK-ENG-02]
    │   └── Http11EngineKeepAliveTests.cs       [TASK-ENG-03]
    ├── Http20/
    │   ├── Http20EngineIntegrationTests.cs     [TASK-ENG-04]
    │   └── Http20EngineMultiplexingTests.cs    [TASK-ENG-05]
    ├── Pool/
    │   ├── HostConnectionPoolIntegrationTests.cs  [TASK-POOL-01]
    │   ├── HostConnectionPoolConcurrencyTests.cs  [TASK-POOL-02]
    │   ├── HostConnectionPoolResilienceTests.cs   [TASK-POOL-03]
    │   └── HostRoutingFlowIntegrationTests.cs     [TASK-POOL-04]
    └── Engine/
        └── EngineVersionRoutingIntegrationTests.cs [TASK-ENG-06]
```

**Collection setup for WS-2 tests:**
- HTTP/1.x tests: `[Collection("Http11Integration")]` → injects `KestrelFixture` (existing) + `ActorSystemFixture` via `IClassFixture<ActorSystemFixture>`
- HTTP/2 tests: `[Collection("Http2Integration")]` → injects `KestrelH2Fixture` (existing) + `ActorSystemFixture` via `IClassFixture<ActorSystemFixture>`

---

## Phase INF — New Test Infrastructure Only

### TASK-INF-01 — `ActorSystemFixture`

**File:** `src/TurboHttp.IntegrationTests/Streams/Shared/ActorSystemFixture.cs`

The only new shared fixture needed. Starts one `ActorSystem` with a `ClientManager` for a whole test class, avoiding the ~200 ms startup cost per test. Kestrel is already provided by the existing `KestrelFixture`/`KestrelH2Fixture` — do **not** start a second Kestrel here.

- [ ] Implement `IAsyncLifetime`
- [ ] `InitializeAsync`:
  - [ ] `ActorSystem.Create("streams-it-" + Guid.NewGuid().ToString("N")[..8])`
  - [ ] Resolve `ClientManager` ref using same Servus.Akka pattern as production code
- [ ] `DisposeAsync`:
  - [ ] `await CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.ClrExitReason.Instance)` with 5 s timeout
- [ ] Expose: `ActorSystem Sys`, `IActorRef ClientManager`

---

### TASK-INF-02 — `StreamsTestClient` (HTTP/1.x)

**File:** `src/TurboHttp.IntegrationTests/Streams/Shared/StreamsTestClient.cs`

Bridges `HostConnectionPool`'s fire-and-forget `Action<HttpResponseMessage>` callback to a per-request `Task<HttpResponseMessage>`. HTTP/1.1 delivers responses in order so a FIFO queue of `TaskCompletionSource` objects is sufficient.

- [ ] Implement `IAsyncDisposable`
- [ ] Constructor: `public StreamsTestClient(TcpOptions options, ActorSystem system)`
- [ ] Internal: `ConcurrentQueue<TaskCompletionSource<HttpResponseMessage>>` for pending requests
- [ ] `SendAsync`: enqueue TCS, call `_pool.Send(request)`, `await tcs.Task.WaitAsync(10s, ct)`
- [ ] `OnResponse` callback: `_pending.TryDequeue(out var tcs) → tcs.TrySetResult(response)`
- [ ] `DisposeAsync`: complete pool source queue, short drain await

---

### TASK-INF-03 — `StreamsTestClientH2` (HTTP/2)

**File:** `src/TurboHttp.IntegrationTests/Streams/Shared/StreamsTestClientH2.cs`

Same as INF-02 but for HTTP/2 out-of-order responses. Matches responses to requests via `response.RequestMessage` reference.

- [ ] Implement `IAsyncDisposable`
- [ ] Internal: `ConcurrentDictionary<HttpRequestMessage, TaskCompletionSource<HttpResponseMessage>>`
- [ ] `SendAsync`: register `(request → tcs)` before `_pool.Send(request)`
- [ ] `OnResponse`: look up `response.RequestMessage` → set matching TCS
- [ ] Prerequisite: verify `Http2StreamStage` sets `response.RequestMessage = request`; if not, fix that first

---

### TASK-INF-04 — `TestClientManagerProxy`

**File:** `src/TurboHttp.IntegrationTests/Streams/Shared/TestClientManagerProxy.cs`

Akka actor proxy that intercepts `CreateTcpRunner` and injects a custom `IClientProvider` — the only way to fault-inject without changing production code.

```
ConnectionStage → CreateTcpRunner → TestClientManagerProxy
                                         └─ injects StreamProvider
                                              └─ forwards to real ClientManager
```

- [ ] Implement as `ReceiveActor`
- [ ] Constructor: `(IActorRef realClientManager, Func<TcpOptions, IClientProvider> providerFactory)`
- [ ] Intercept `ClientManager.CreateTcpRunner`:
  ```csharp
  Receive<ClientManager.CreateTcpRunner>(msg =>
  {
      var enriched = msg with { StreamProvider = _providerFactory(msg.Options) };
      _realClientManager.Forward(enriched);
  });
  ```
- [ ] Pass all other messages to `_realClientManager` unchanged

---

### TASK-INF-05 — `FaultInjectingClientProvider`

**File:** `src/TurboHttp.IntegrationTests/Streams/Shared/FaultInjectingClientProvider.cs`

`IClientProvider` implementation that wraps a real TCP socket and injects configurable failures.

- [ ] Implement `IClientProvider`
- [ ] `FaultConfig` record:
  ```csharp
  public sealed record FaultConfig
  {
      public int DisconnectAfterBytes { get; init; } = int.MaxValue;
      public TimeSpan ConnectDelay    { get; init; } = TimeSpan.Zero;
      public bool FailOnConnect       { get; init; } = false;
      public int MaxConnectAttempts   { get; init; } = int.MaxValue;
  }
  ```
- [ ] `GetStream()`: honour `FailOnConnect`, `ConnectDelay`, `MaxConnectAttempts`; open real TCP socket; return `FaultInjectingStream` (private nested class)
- [ ] `FaultInjectingStream : Stream`: tracks cumulative bytes; throws `IOException` when `DisconnectAfterBytes` exceeded; delegates all else to underlying stream
- [ ] `RemoteEndPoint`: real socket remote endpoint
- [ ] `Close()`: close underlying socket

---

## Phase CS — ConnectionStage Tests

### TASK-CS-01 — `ConnectionStageConnectTests`

**File:** `src/TurboHttp.IntegrationTests/Streams/ConnectionStage/ConnectionStageConnectTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Tests that `ConnectionStage` correctly notifies its handler actor when a TCP connection is established or terminated. Uses the stage directly as a raw `Flow<bytes, bytes>`.

- [ ] **ST-CS-001**: After materialization, `ClientConnected` is sent to handler actor within 2 s
  - [ ] Assert: `connected.RemoteEndPoint` == `127.0.0.1:{Port}`
  - [ ] Assert: `connected.InboundReader` is not null
  - [ ] Assert: `connected.OutboundWriter` is not null
- [ ] **ST-CS-002**: After server-side close (`GET /close`), `ClientDisconnected` is sent to handler
  - [ ] Assert: `disconnected.RemoteEndPoint` matches
- [ ] **ST-CS-003**: Three independent stages pointing at the same server produce three independent `ClientConnected` messages on three separate handler actors

---

### TASK-CS-02 — `ConnectionStageDataFlowTests`

**File:** `src/TurboHttp.IntegrationTests/Streams/ConnectionStage/ConnectionStageDataFlowTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Tests that bytes pushed into the stage inlet reach Kestrel, and Kestrel's response bytes arrive on the outlet.

- [ ] **ST-CS-010**: Raw `GET /ping HTTP/1.1` bytes → outlet contains valid HTTP/1.1 200 response bytes
  - [ ] Encode request manually as bytes, push via `Source.Single`, collect via `Sink.Seq`
  - [ ] Decode outlet bytes with `Http11Decoder`, assert status == 200
- [ ] **ST-CS-011**: Three sequential requests on the same materialization all receive responses
- [ ] **ST-CS-012**: A 512 KB payload (`GET /large?kb=512`) is received completely without loss
  - [ ] Assert: total byte count of body == 512 * 1024
- [ ] **ST-CS-013**: Five requests with unique query strings are returned in the same order
  - [ ] `GET /echo?id=1` through `?id=5` — assert response order matches request order

---

### TASK-CS-03 — `ConnectionStageReconnectTests`

**File:** `src/TurboHttp.IntegrationTests/Streams/ConnectionStage/ConnectionStageReconnectTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Uses `TestClientManagerProxy` + `FaultInjectingClientProvider` to simulate network failures and verify reconnect behaviour.

- [ ] **ST-CS-020**: After forced disconnect (`DisconnectAfterBytes=200`), stage reconnects and subsequent request succeeds
  - [ ] Assert: `ClientDisconnected` then new `ClientConnected` arrive on handler actor
- [ ] **ST-CS-021**: Outbound messages queued before connect completes are flushed after connect
  - [ ] Use `FaultConfig { ConnectDelay = 500ms }`, send 2 requests before connect
  - [ ] Assert: both complete after ~500 ms
- [ ] **ST-CS-022**: When `MaxReconnectAttempts=1` and `FailOnConnect=true`, stream terminates with failure (not hangs)

---

## Phase ENG — Engine Integration Tests

### TASK-ENG-01 — HTTP/1.0 Engine Integration

**File:** `src/TurboHttp.IntegrationTests/Streams/Http10/Http10EngineIntegrationTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Wire `Http10Engine.CreateFlow()` with a real `ConnectionStage` against Kestrel. Tests the full BidiFlow end-to-end for HTTP/1.0.

- [ ] **ST-10-INT-001**: `GET /ping` → 200, `response.Version == HttpVersion.Version10`
- [ ] **ST-10-INT-002**: Connection is closed after response — `ClientDisconnected` arrives on handler actor
- [ ] **ST-10-INT-003**: Two sequential requests each open a new TCP connection — two `ClientConnected` messages
- [ ] **ST-10-INT-004**: `POST /echo` with body → response body matches request body exactly

---

### TASK-ENG-02 — HTTP/1.1 Engine Integration

**File:** `src/TurboHttp.IntegrationTests/Streams/Http11/Http11EngineIntegrationTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Wire `Http11Engine.CreateFlow()` with a real `ConnectionStage`. Tests core HTTP/1.1 request-response semantics.

- [ ] **ST-11-INT-001**: `GET /ping` → 200, `response.Version == HttpVersion.Version11`
- [ ] **ST-11-INT-002**: `POST /echo` with `application/octet-stream` body → body echoed, `Content-Length` set
- [ ] **ST-11-INT-003**: Custom request header `X-Test-Header: hello` arrives at server (verify via `/headers` route)
- [ ] **ST-11-INT-004**: `GET /status/404` → `response.StatusCode == HttpStatusCode.NotFound`
- [ ] **ST-11-INT-005**: `GET /status/500` → `response.StatusCode == HttpStatusCode.InternalServerError`
- [ ] **ST-11-INT-006**: `GET /large?kb=128` → response body length == 131072 bytes
- [ ] **ST-11-INT-007**: `GET /chunked` → body decoded correctly from chunked encoding (3 chunks concatenated)

---

### TASK-ENG-03 — HTTP/1.1 Keep-Alive Tests

**File:** `src/TurboHttp.IntegrationTests/Streams/Http11/Http11EngineKeepAliveTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Keep-alive deserves its own file. These tests prove that connections are reused correctly across requests, which is the most important HTTP/1.1 behaviour difference from HTTP/1.0.

- [ ] **ST-11-KA-001**: 5 sequential requests use only one TCP connection
  - [ ] Track `ClientConnected` count via handler actor — assert exactly 1
- [ ] **ST-11-KA-002**: After `GET /close` (server sends `Connection: close`), next request opens a new connection
  - [ ] Assert: `ClientDisconnected` received, then second `ClientConnected`
- [ ] **ST-11-KA-003**: 10 sequential requests all return 200 (keep-alive stability over extended use)
- [ ] **ST-11-KA-004**: Idle connection reused after 500 ms pause between requests
  - [ ] Send request 1, await, `Task.Delay(500)`, send request 2
  - [ ] Assert: still only 1 `ClientConnected`

---

### TASK-ENG-04 — HTTP/2 Engine Integration

**File:** `src/TurboHttp.IntegrationTests/Streams/Http20/Http20EngineIntegrationTests.cs`
**Fixtures:** `[Collection("Http2Integration")]` + `IClassFixture<ActorSystemFixture>`

Wire `Http20Engine.CreateFlow()` with a real `ConnectionStage` against h2c Kestrel. Tests the full HTTP/2 handshake and basic request-response.

- [ ] **ST-20-INT-001**: Connection preface sent on connect — first 24 bytes == h2c magic + SETTINGS frame header
  - [ ] Capture outbound bytes via `TestClientManagerProxy` / outbound channel
- [ ] **ST-20-INT-002**: Server SETTINGS triggers a SETTINGS ACK from the engine
  - [ ] Assert: SETTINGS frame with ACK flag in outbound bytes
- [ ] **ST-20-INT-003**: `GET /h2/ping` → 200, `response.Version == HttpVersion.Version20`
- [ ] **ST-20-INT-004**: `POST /h2/echo` with body → body echoed back
- [ ] **ST-20-INT-005**: GOAWAY from server causes engine flow to complete gracefully (no crash)
  - [ ] Assert: ActorSystem stays alive after GOAWAY

---

### TASK-ENG-05 — HTTP/2 Multiplexing

**File:** `src/TurboHttp.IntegrationTests/Streams/Http20/Http20EngineMultiplexingTests.cs`
**Fixtures:** `[Collection("Http2Integration")]` + `IClassFixture<ActorSystemFixture>`

HTTP/2 multiplexing is the key advantage over HTTP/1.1. Verifies that concurrent streams work correctly end-to-end.

- [ ] **ST-20-MUX-001**: 5 concurrent requests all return 200 with correct bodies
  - [ ] `await Task.WhenAll(5 x SendAsync(...))`
- [ ] **ST-20-MUX-002**: 10 concurrent requests to `/h2/stream/10` all return complete bodies (no mixing)
- [ ] **ST-20-MUX-003**: Slow responses (`/slow?ms=100`) arrive last but are matched to the correct original request
  - [ ] Mix slow and fast requests — assert response body matches request identity
- [ ] **ST-20-MUX-004**: Flow control: large response (100 KB) completes correctly despite small initial window
  - [ ] Assert: response arrives complete, WINDOW_UPDATE frames sent

---

### TASK-ENG-06 — Engine Version Routing Integration

**File:** `src/TurboHttp.IntegrationTests/Streams/Engine/EngineVersionRoutingIntegrationTests.cs`
**Fixtures:** `IClassFixture<ActorSystemFixture>` (no shared Kestrel collection — test class implements `IAsyncLifetime` and spins up both a `KestrelFixture` and a `KestrelH2Fixture` inline)

Wire the top-level `Engine.CreateFlow()` against both a HTTP/1.x Kestrel and a h2c Kestrel, routing requests by `request.Version`.

- [ ] **ST-ENG-001**: `Version=1.0` → routed through `Http10Engine` path, `response.Version == 1.0`
- [ ] **ST-ENG-002**: `Version=1.1` → routed through `Http11Engine` path, `response.Version == 1.1`
- [ ] **ST-ENG-003**: `Version=2.0` → routed through `Http20Engine` path, `response.Version == 2.0`
- [ ] **ST-ENG-004**: One request per version, sent together → all 3 complete with correct versions
- [ ] **ST-ENG-005**: Unknown version (e.g. `new Version(9, 0)`) falls through without unhandled exception

---

## Phase POOL — HostConnectionPool Tests

### TASK-POOL-01 — Pool Basics

**File:** `src/TurboHttp.IntegrationTests/Streams/Pool/HostConnectionPoolIntegrationTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Treats `HostConnectionPool` as a black box via `StreamsTestClient`. Verifies that the pool works correctly at all in a live scenario.

- [ ] **ST-POOL-001**: Single request → correct 200 response
- [ ] **ST-POOL-002**: 20 sequential requests → all 200, no timeout
- [ ] **ST-POOL-003**: Pool handles both GET and POST from the same instance
- [ ] **ST-POOL-004**: Pool survives a 2 s idle period and serves the next request without reconnect failure
- [ ] **ST-POOL-005**: `DisposeAsync` completes within 3 s without hanging

---

### TASK-POOL-02 — Pool Concurrency

**File:** `src/TurboHttp.IntegrationTests/Streams/Pool/HostConnectionPoolConcurrencyTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

Can the pool distribute concurrent load across its `Balance`-ed connections?

- [ ] **ST-POOL-010**: 20 parallel requests via `Task.WhenAll` → all 200
- [ ] **ST-POOL-011**: 10 bursts of 5 parallel requests (50 total) → all succeed
- [ ] **ST-POOL-012**: 4 concurrent large responses (128 KB each) → all complete with correct lengths
- [ ] **ST-POOL-013**: Pool does not crash when flooded with more than 256 pending requests (queue capacity)
  - [ ] Assert: excess requests eventually complete or are rejected cleanly — no unhandled exceptions

---

### TASK-POOL-03 — Pool Resilience

**File:** `src/TurboHttp.IntegrationTests/Streams/Pool/HostConnectionPoolResilienceTests.cs`
**Fixtures:** `[Collection("Http11Integration")]` + `IClassFixture<ActorSystemFixture>`

- [ ] **ST-POOL-020**: After forced mid-stream disconnect, next request succeeds (pool auto-reconnects)
  - [ ] `FaultConfig { DisconnectAfterBytes = 500 }`
- [ ] **ST-POOL-021**: Pool reconnects after server restart on same port
  - [ ] Stop Kestrel, restart it, assert subsequent requests succeed
- [ ] **ST-POOL-022**: Requests sent during `ConnectDelay=1s` are buffered and delivered once connected
  - [ ] Send 3 requests before connect completes — all 3 complete after ~1 s
- [ ] **ST-POOL-023**: `DisposeAsync` during an in-flight `GET /slow?ms=500` → no unhandled `AggregateException`

---

### TASK-POOL-04 — HostRoutingFlow Integration

**File:** `src/TurboHttp.IntegrationTests/Streams/Pool/HostRoutingFlowIntegrationTests.cs`
**Fixtures:** `IClassFixture<ActorSystemFixture>` (test class implements `IAsyncLifetime` directly — starts two inline `WebApplication` instances, no new fixture class)

Requires two Kestrel instances on separate ports. The test class itself implements `IAsyncLifetime`, starts both apps in `InitializeAsync`, and tears them down in `DisposeAsync`. Server A responds to `/ping` with `"server-a"`; server B responds with `"server-b"`. No helper fixture class is needed.

- [ ] **ST-ROUTE-001**: Request to server A → response body == `"server-a"`
- [ ] **ST-ROUTE-002**: Request to server B → response body == `"server-b"`
- [ ] **ST-ROUTE-003**: Alternating requests to A and B (5 each) → no cross-contamination
- [ ] **ST-ROUTE-004**: New host seen for the first time creates a new pool dynamically
- [ ] **ST-ROUTE-005**: 10 concurrent requests split between A and B → all succeed

---

## WS-2 Risks

| # | Risk | Resolution |
|---|------|------------|
| R1 | H2 response matching — `onResponse` has no stream ID | Verify `Http2StreamStage` sets `response.RequestMessage`. Fix if not, before INF-05. |
| R2 | `HostConnectionPool` has no `DisposeAsync` | Call `_queue.Complete()`. `ISourceQueueWithComplete` supports this. |
| R3 | Kestrel h2c and HTTP/1.x cannot share a port | Use two separate fixtures for version-routing tests. |
| R4 | Two parallel collections may share Servus.Akka static state | Add `[assembly: CollectionBehavior(DisableTestParallelization = true)]` to `TurboHttp.StreamTests`. |
| R5 | `FaultInjectingClientProvider` cannot be injected into `HostConnectionPool` directly | Use `TestClientManagerProxy` (INF-04) — no production code changes required. |
| R6 | Reconnect tests are slow if `ReconnectInterval` is too long | Always set `ReconnectInterval = TimeSpan.FromMilliseconds(100)` in test `TcpOptions`. |

---

## WS-2 Recommended Implementation Order

```
Week 1: INF-01 → INF-02 → INF-03 → INF-04 → INF-05
Week 2: CS-01, CS-02, ENG-01, ENG-02, ENG-03  (parallel)
Week 3: CS-03, ENG-04, ENG-05, POOL-01, POOL-02
Week 4: POOL-03, POOL-04, ENG-06
```

---

---

# Work Stream 3 — RFC Coverage Gaps

## Overview of Gaps Found

After analysing 79+ test files (~2,100 tests) across all RFC folders, the following concrete gaps were identified:

| RFC | Section | Topic | Severity | Test file to create |
|-----|---------|-------|----------|---------------------|
| 9110 | §4–8 | HTTP status code semantics & content negotiation | **High** | `RFC9110/04_StatusCodeSemanticsTests.cs` |
| 9110 | §5 | Authentication header semantics | Medium | `RFC9110/05_AuthenticationHeaderTests.cs` |
| 9110 | §8 | Content negotiation (Accept-*) | **High** | `RFC9110/06_ContentNegotiationTests.cs` |
| 9113 | §6.5 | PRIORITY frame handling | Medium | `RFC9113/22_PriorityFrameTests.cs` |
| 9113 | §6.6 | PUSH_PROMISE frame handling | Medium | `RFC9113/23_PushPromiseTests.cs` |
| 9113 | §8.3 | CONNECT method tunneling | Low | `RFC9113/24_ConnectMethodTests.cs` |
| 9111 | §4.4 | Partial content (206) and Range caching | Medium | `RFC9111/06_PartialContentTests.cs` |
| 9111 | §3.2 | Private vs. shared cache scope | Low | `RFC9111/07_CacheScopeTests.cs` |
| 9112 | §9.4 | Pipelining request ordering | Medium | `RFC9112/22_PipeliningOrderTests.cs` |

---

## TASK-RFC-01 — HTTP Status Code Semantics (RFC 9110 §4–8)

**File:** `src/TurboHttp.Tests/RFC9110/04_StatusCodeSemanticsTests.cs`

The existing RFC9110 folder covers only content encoding (§8.4). The broader semantics of status codes — what each code *means* for the client — are not tested directly. These tests verify that the decoder correctly preserves status-code metadata and that the encoder correctly sets it.

- [ ] **RFC-9110-SC-001**: 200 OK — `response.StatusCode == HttpStatusCode.OK`, `response.ReasonPhrase == "OK"`
- [ ] **RFC-9110-SC-002**: 201 Created — `response.StatusCode == HttpStatusCode.Created`
- [ ] **RFC-9110-SC-003**: 204 No Content — `response.StatusCode == HttpStatusCode.NoContent`, body is empty
  - [ ] Assert: no `Content-Length` or `Content-Length: 0` in wire format
- [ ] **RFC-9110-SC-004**: 206 Partial Content — `response.StatusCode == HttpStatusCode.PartialContent`
  - [ ] Assert: `Content-Range` header is preserved in the decoded response
- [ ] **RFC-9110-SC-005**: 304 Not Modified — `response.StatusCode == HttpStatusCode.NotModified`
  - [ ] Assert: response body is empty even if `Content-Length` is present (RFC 9110 §15.4.5)
- [ ] **RFC-9110-SC-006**: 400 Bad Request — `response.StatusCode == HttpStatusCode.BadRequest`
- [ ] **RFC-9110-SC-007**: 401 Unauthorized — `response.StatusCode == HttpStatusCode.Unauthorized`
  - [ ] Assert: `WWW-Authenticate` header is preserved
- [ ] **RFC-9110-SC-008**: 403 Forbidden — `response.StatusCode == HttpStatusCode.Forbidden`
- [ ] **RFC-9110-SC-009**: 404 Not Found — `response.StatusCode == HttpStatusCode.NotFound`
- [ ] **RFC-9110-SC-010**: 405 Method Not Allowed — `response.StatusCode == HttpStatusCode.MethodNotAllowed`
  - [ ] Assert: `Allow` header is preserved (RFC 9110 §15.5.6)
- [ ] **RFC-9110-SC-011**: 408 Request Timeout — `response.StatusCode == HttpStatusCode.RequestTimeout`
- [ ] **RFC-9110-SC-012**: 409 Conflict — `response.StatusCode == HttpStatusCode.Conflict`
- [ ] **RFC-9110-SC-013**: 429 Too Many Requests — status code 429 is preserved (non-standard, widely used)
  - [ ] Assert: `Retry-After` header is preserved
- [ ] **RFC-9110-SC-014**: 500 Internal Server Error — `response.StatusCode == HttpStatusCode.InternalServerError`
- [ ] **RFC-9110-SC-015**: 502 Bad Gateway — `response.StatusCode == HttpStatusCode.BadGateway`
- [ ] **RFC-9110-SC-016**: 503 Service Unavailable — `response.StatusCode == HttpStatusCode.ServiceUnavailable`
  - [ ] Assert: `Retry-After` header (if present) is preserved
- [ ] **RFC-9110-SC-017**: 504 Gateway Timeout — `response.StatusCode == HttpStatusCode.GatewayTimeout`
- [ ] **RFC-9110-SC-018**: Unknown status code (e.g. 299) — decoder preserves numeric code without throwing
  - [ ] Assert: `response.StatusCode == (HttpStatusCode)299`

> All tests use raw byte construction: `BuildRawResponse("HTTP/1.1 {code} {phrase}\r\nContent-Length: 0\r\n\r\n")` and verify via `Http11Decoder.TryDecode()`.

---

## TASK-RFC-02 — Content Negotiation (RFC 9110 §12)

**File:** `src/TurboHttp.Tests/RFC9110/05_ContentNegotiationTests.cs`

Content negotiation headers (`Accept`, `Accept-Encoding`, `Accept-Language`, `Accept-Charset`) are sent by the client in requests and must be encoded correctly. Server-driven negotiation affects which `Content-Type` and `Content-Encoding` the server returns.

- [ ] **RFC-9110-CN-001**: `Accept: application/json` is serialised into the wire format correctly
  - [ ] Encode a request with `Accept: application/json`, decode bytes, assert header value
- [ ] **RFC-9110-CN-002**: `Accept: text/html, application/json;q=0.9` — quality values preserved
- [ ] **RFC-9110-CN-003**: `Accept: */*` — wildcard preserved
- [ ] **RFC-9110-CN-004**: `Accept-Encoding: gzip, br, deflate` — all tokens present in wire format
- [ ] **RFC-9110-CN-005**: `Accept-Encoding: identity;q=0` — identity explicitly excluded
- [ ] **RFC-9110-CN-006**: `Accept-Language: en-US, de;q=0.8` — language tags and quality preserved
- [ ] **RFC-9110-CN-007**: Decoder preserves `Content-Type` header from response unchanged
  - [ ] `Content-Type: application/json; charset=utf-8` — semicolon + params preserved
- [ ] **RFC-9110-CN-008**: Decoder preserves `Vary` response header (signals which request headers affected negotiation)
- [ ] **RFC-9110-CN-009**: Multiple `Accept` headers are folded into one (RFC 9110 §5.3.1)
- [ ] **RFC-9110-CN-010**: `Accept` header with only whitespace tokens is encoded as `Accept: */*` fallback

---

## TASK-RFC-03 — Authentication Header Semantics (RFC 9110 §11)

**File:** `src/TurboHttp.Tests/RFC9110/06_AuthenticationHeaderTests.cs`

Authentication headers are security-sensitive. The encoder must never strip `Authorization`; the decoder must preserve `WWW-Authenticate` and `Proxy-Authenticate` without modification.

- [ ] **RFC-9110-AUTH-001**: `Authorization: Bearer <token>` is encoded in the wire format
- [ ] **RFC-9110-AUTH-002**: `Authorization: Basic <b64>` is encoded correctly
- [ ] **RFC-9110-AUTH-003**: `Authorization` header is marked as sensitive in HPACK (NeverIndex) for HTTP/2
  - [ ] Use `HpackEncoder.Encode()` and assert sensitivity flag is set
- [ ] **RFC-9110-AUTH-004**: Decoder preserves `WWW-Authenticate: Bearer realm="api"` from response
- [ ] **RFC-9110-AUTH-005**: Decoder preserves `Proxy-Authenticate` header
- [ ] **RFC-9110-AUTH-006**: Decoder preserves `Authorization-Info` header (RFC 9110 §11.3)
- [ ] **RFC-9110-AUTH-007**: `Proxy-Authorization` is encoded in requests and marked sensitive in HPACK
- [ ] **RFC-9110-AUTH-008**: Multiple `WWW-Authenticate` challenges on one 401 response are all preserved

---

## TASK-RFC-04 — HTTP/2 PRIORITY Frame (RFC 9113 §6.5 / §5.3)

**File:** `src/TurboHttp.Tests/RFC9113/22_PriorityFrameTests.cs`

`PriorityFrame` exists in `Http2Frame.cs` but has no test coverage. These tests verify the wire format encode/decode round-trip.

- [ ] **RFC-9113-PR-001**: `PriorityFrame` with weight=16, exclusive=false, dependsOn=0 → serialised to correct 5-byte payload
- [ ] **RFC-9113-PR-002**: `PriorityFrame` with weight=255 (max) → correct wire format
- [ ] **RFC-9113-PR-003**: `PriorityFrame` with exclusive=true → bit 31 of dependency field is set
- [ ] **RFC-9113-PR-004**: `PriorityFrame` with non-zero stream dependency → `dependsOn` field preserved
- [ ] **RFC-9113-PR-005**: Decode a raw PRIORITY frame bytes → produces `PriorityFrame` with correct fields
- [ ] **RFC-9113-PR-006**: A stream cannot depend on itself (stream A dependsOn A) → `Http2Exception` with `PROTOCOL_ERROR`
- [ ] **RFC-9113-PR-007**: PRIORITY frame on stream 0 → `PROTOCOL_ERROR` (RFC 9113 §6.3)
- [ ] **RFC-9113-PR-008**: PRIORITY frame with wrong length (not 5 bytes) → `FRAME_SIZE_ERROR`
- [ ] **RFC-9113-PR-009**: PRIORITY frame is decoded correctly by `Http2FrameDecoderStage` (stage test)

---

## TASK-RFC-05 — HTTP/2 PUSH_PROMISE Frame (RFC 9113 §6.6)

**File:** `src/TurboHttp.Tests/RFC9113/23_PushPromiseTests.cs`

`PushPromiseFrame` exists in `Http2Frame.cs` but has no test coverage.

- [ ] **RFC-9113-PP-001**: `PushPromiseFrame` with promised stream ID = 2 and a valid HPACK header block → correct wire format
  - [ ] Assert: 9-byte frame header + 4-byte promised-stream-ID field + header block
- [ ] **RFC-9113-PP-002**: Decode raw PUSH_PROMISE bytes → `PushPromiseFrame` with correct `PromisedStreamId` and header bytes
- [ ] **RFC-9113-PP-003**: `PushPromiseFrame` with `END_HEADERS` flag set → no CONTINUATION needed
- [ ] **RFC-9113-PP-004**: `PushPromiseFrame` without `END_HEADERS` → CONTINUATION frame expected
- [ ] **RFC-9113-PP-005**: PUSH_PROMISE on an odd-numbered (client-initiated) stream → the server associates it with that stream
- [ ] **RFC-9113-PP-006**: PUSH_PROMISE with promised stream ID = 0 → `PROTOCOL_ERROR` (RFC 9113 §6.6)
- [ ] **RFC-9113-PP-007**: PUSH_PROMISE with an even promised stream ID (server-initiated) → correct (e.g. 2, 4)
- [ ] **RFC-9113-PP-008**: `SerializedSize` accounts for 4-byte promised-stream-ID prefix correctly

> **Pre-check required:** Verify whether server push is actually implemented in `Http2ConnectionStage`. If it is not, mark RFC-9113-PP-001 to PP-004 as "encoder/decoder unit tests only" and skip stage-level tests.

---

## TASK-RFC-06 — HTTP/2 CONNECT Method Tunneling (RFC 9113 §8.3)

**File:** `src/TurboHttp.Tests/RFC9113/24_ConnectMethodTests.cs`

The CONNECT method in HTTP/2 uses a special pseudo-header form (`:method: CONNECT`, `:authority:`, no `:path`, no `:scheme`). The encoder must produce this and the decoder must handle it.

- [ ] **RFC-9113-CT-001**: `CONNECT example.com:443` request encoded to HEADERS with `:method: CONNECT`, `:authority: example.com:443`
  - [ ] Assert: no `:path` pseudo-header present
  - [ ] Assert: no `:scheme` pseudo-header present
- [ ] **RFC-9113-CT-002**: CONNECT HEADERS frame with `:path` present → `PROTOCOL_ERROR` (RFC 9113 §8.3)
- [ ] **RFC-9113-CT-003**: CONNECT HEADERS frame with `:scheme` present → `PROTOCOL_ERROR`
- [ ] **RFC-9113-CT-004**: CONNECT request without `:authority` → `PROTOCOL_ERROR`
- [ ] **RFC-9113-CT-005**: After CONNECT HEADERS, DATA frames carry tunnel payload — decoder passes them through without interpreting as HTTP

---

## TASK-RFC-07 — HTTP/1.1 Pipelining Request Order (RFC 9112 §9.4)

**File:** `src/TurboHttp.Tests/RFC9112/22_PipeliningOrderTests.cs`

Pipelining sends multiple requests without waiting for each response. Responses *must* arrive in request order (RFC 9112 §9.3.2). The existing `Http11RoundTripPipeliningTests.cs` may cover some of this at encoder/decoder level; these tests add explicit ordering guarantees.

- [ ] **RFC-9112-PL-001**: Two pipelined requests decoded from a single buffer → responses arrive in request order
  - [ ] Build two request byte sequences, two response byte sequences concatenated
  - [ ] Assert: first decoded response matches first request
- [ ] **RFC-9112-PL-002**: Five pipelined requests in one buffer → all five decoded in order
- [ ] **RFC-9112-PL-003**: Pipelined requests split across TCP fragments → correctly reassembled and decoded in order
  - [ ] Split the response buffer at a byte boundary mid-header of the second response
- [ ] **RFC-9112-PL-004**: Pipelining with mixed body sizes (first 0-byte, second 100-byte) → both decoded correctly in order
- [ ] **RFC-9112-PL-005**: Pipelining with chunked second response → chunked body decoded before moving to third response

---

## TASK-RFC-08 — HTTP Caching Partial Content (RFC 9111 §4.4 / RFC 9110 §14.2)

**File:** `src/TurboHttp.Tests/RFC9111/06_PartialContentTests.cs`

A 206 Partial Content response (serving a byte range) has distinct caching rules. The existing RFC9111 tests do not cover this case.

- [ ] **RFC-9111-PC-001**: A 206 response with `Content-Range: bytes 0-99/500` is stored in cache
- [ ] **RFC-9111-PC-002**: A 206 response can only satisfy a range request; a full request must not return a 206 from cache
- [ ] **RFC-9111-PC-003**: Two non-overlapping 206 responses for the same resource → both stored (if implementation supports range merging)
- [ ] **RFC-9111-PC-004**: A `Range:` request for bytes already in cache → served from cache without re-validation
- [ ] **RFC-9111-PC-005**: `Content-Range: */500` (unsatisfied range) → not cached
- [ ] **RFC-9111-PC-006**: 206 response without `Content-Range` header → treated as complete response (undefined range), stored as 200

---

## TASK-RFC-09 — HTTP Caching Scope: Private vs. Shared (RFC 9111 §3.2)

**File:** `src/TurboHttp.Tests/RFC9111/07_CacheScopeTests.cs`

`Cache-Control: private` means the response must not be stored by a shared cache. TurboHttp is a client-side (private) cache, so `private` directives should be accepted. `s-maxage` applies only to shared caches and should be ignored.

- [ ] **RFC-9111-CS-001**: `Cache-Control: private` → response IS stored in the client cache (private caches may store it)
- [ ] **RFC-9111-CS-002**: `Cache-Control: private="Set-Cookie"` → response stored, but the named field header is not retained in cache (RFC 9111 §5.2.2.7)
- [ ] **RFC-9111-CS-003**: `Cache-Control: s-maxage=3600` → ignored by private (client) cache; falls back to `max-age` or heuristic
- [ ] **RFC-9111-CS-004**: `Cache-Control: no-store` takes precedence over `private` → response not stored
- [ ] **RFC-9111-CS-005**: Response with `Authorization` header + `Cache-Control: public` → stored (RFC 9111 §3.5)
- [ ] **RFC-9111-CS-006**: Response with `Authorization` header and no cache override → not stored by shared cache (N/A for private, document test expectation clearly)

---

## WS-3 Recommended Implementation Order

```
High priority (no test coverage at all):
  TASK-RFC-01  Status code semantics
  TASK-RFC-02  Content negotiation
  TASK-RFC-04  PRIORITY frame

Medium priority (partial coverage or important RFC section):
  TASK-RFC-03  Authentication headers
  TASK-RFC-05  PUSH_PROMISE
  TASK-RFC-07  Pipelining order
  TASK-RFC-08  Partial content caching

Lower priority:
  TASK-RFC-06  CONNECT tunneling
  TASK-RFC-09  Cache scope
```

---

## WS-3 Acceptance Criteria

A test task is done when:

- [ ] All test methods compile (`dotnet build`)
- [ ] All test methods pass (`dotnet test --filter "FullyQualifiedName~<ClassName>"`)
- [ ] Test names follow: `[Fact(DisplayName = "RFC-<section>-<cat>-<nnn>: description")]`
- [ ] No `#nullable enable` at the top of the file
- [ ] Test class is `public sealed class`
- [ ] No network I/O — all RFC unit tests use raw byte construction only

---

*End of master task list*
