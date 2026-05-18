# Client-Level Acceptance Tests with Fake Transports

**Date:** 2026-05-17
**Status:** Draft
**Goal:** Make `TurboHTTP.AcceptanceTests` prove the full client pipeline works without network I/O, closing the gap where integration-test-caught bugs (e.g. H2) go undetected by engine-level acceptance tests.

## Problem

Acceptance tests currently operate at the **engine level** — `Source.Single(request).Via(engine.CreateFlow().Join(fakeTransport))`. This skips the entire client layer: channels, Consumer actor, StreamOwner, PartitionHub response routing, PendingRequest lifecycle, timeout/cancellation management.

When H2 integration tests fail but acceptance tests pass, the bug is in the actor/routing layer — exactly the layer acceptance tests don't cover.

## Test Layering (Target State)

| Project | Responsibility | Entry point |
|---------|---------------|-------------|
| **TurboHTTP.Tests** | Protocol correctness | Direct unit: FrameDecoder, StateMachine, HPACK, etc. |
| **TurboHTTP.AcceptanceTests** | Full pipeline + protocol | `ITurboHttpClient.SendAsync()` → actors → engine → fake transport |
| **TurboHTTP.IntegrationTests** | Real network behavior | `ITurboHttpClient.SendAsync()` → actors → engine → real TCP/QUIC |

## Design

### 1. Internal Hook — TransportRegistry Override

Add an optional `TransportRegistry?` parameter through three internal types. No public API changes.

**`ClientStreamManager.RegisterConsumer`** — add field:

```csharp
internal sealed record RegisterConsumer(
    string Name,
    Guid ConsumerId,
    ChannelReader<HttpRequestMessage> RequestReader,
    ChannelWriter<HttpResponseMessage> ResponseWriter,
    Func<TurboRequestOptions> OptionsFactory,
    TurboClientOptions ClientOptions,
    PipelineDescriptor Pipeline,
    TransportRegistry? TransportOverride = null);
```

**`StreamOwner`** — constructor receives and stores it:

```csharp
private readonly TransportRegistry? _transportOverride;

public StreamOwner(TurboClientOptions clientOptions, PipelineDescriptor pipeline,
    TransportRegistry? transportOverride = null)
```

**`StreamOwner.MaterializeStream()`** — conditional at transport creation:

```csharp
TransportRegistry transports;
if (_transportOverride is not null)
{
    transports = _transportOverride;
}
else
{
    _tcpManager = Context.ActorOf(TransportFactory.CreateTcpConnectionManager(poolRegistry), "tcp-pool");
    _quicConnectionManager = Context.ActorOf(TransportFactory.CreateQuicConnectionManager(), "quic-pool");
    transports = new TransportRegistry()
        .Register(HttpVersion.Version10, TransportFactory.CreateTcpClient(_tcpManager, new Http10PoolingStrategy()))
        .Register(HttpVersion.Version11, TransportFactory.CreateTcpClient(_tcpManager, new Http11PoolingStrategy()))
        .Register(HttpVersion.Version20, TransportFactory.CreateTcpClient(_tcpManager, new Http2PoolingStrategy()))
        .Register(HttpVersion.Version30, TransportFactory.CreateQuicClient(_quicConnectionManager));
}
```

When `_transportOverride` is set, no TCP manager or QUIC manager actors are created.

**`TurboHttpClientFactory`** — internal overload:

```csharp
internal ITurboHttpClient CreateClient(string name, TransportRegistry? transportOverride)
```

This overload passes the registry through the `RegisterConsumer` message. Accessible to test projects via `InternalsVisibleTo`.

### 2. FakeResponse Builder

New static class in `TurboHTTP.Tests.Shared` providing a high-level API for building valid HTTP response bytes. Layered on top of the existing raw-byte factories from `EngineTestBase`.

```csharp
public static class FakeResponse
{
    // HTTP/1.0 and 1.1 — returns complete response bytes
    public static byte[] Http10(int status, string? body = null,
        params (string Name, string Value)[] headers)
    public static byte[] Http11(int status, string? body = null,
        params (string Name, string Value)[] headers)

    // HTTP/2 — returns array of frame bytes (HEADERS + DATA)
    public static byte[][] H2(int status, string? body = null,
        params (string Name, string Value)[] headers)

    // HTTP/3 — returns array of frame bytes
    public static byte[][] H3(int status, string? body = null,
        params (string Name, string Value)[] headers)

    // Shortcuts
    public static byte[] Ok(string body) => Http11(200, body);
    public static byte[] NotFound() => Http11(404);
}
```

Raw-byte factories (`CreateScriptedConnection`, `CreateH2Connection`, `CreateH3Connection`) remain available for edge cases and malformed response testing.

### 3. ClientAcceptanceHelper

New helper in `TurboHTTP.Tests.Shared` that mirrors `ClientHelper` from integration tests but injects fake transports.

```csharp
public sealed class ClientAcceptanceHelper : IAsyncDisposable
{
    public ITurboHttpClient Client { get; }

    public static ClientAcceptanceHelper Create(
        TransportRegistry transports,
        Version version,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        // 1. Build ServiceCollection + ActorSystem (same pattern as ClientHelper)
        // 2. Replace IOptionsFactory<TurboClientOptions> with fixed options
        // 3. Call factory.CreateClient(name, transports) using internal overload
        // 4. Set BaseAddress to synthetic URI (http://fake.test/)
        // 5. Set DefaultRequestVersion to requested version
        // 6. Return helper owning ServiceProvider + ActorSystem + Client
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose client, terminate ActorSystem, dispose ServiceProvider
    }
}
```

No port, host, or scheme parameters — there's no real server. `BaseAddress` uses a synthetic URI so the engine can route requests by endpoint.

### 4. ClientAcceptanceTestBase

New base class in `TurboHTTP.Tests.Shared` that replaces `AcceptanceTestBase` as the base for all acceptance tests.

```csharp
public abstract class ClientAcceptanceTestBase : AcceptanceTestBase
{
    // Inherits raw-byte factories from EngineTestBase via AcceptanceTestBase

    // Single request through full client pipeline
    protected async Task<HttpResponseMessage> SendClientAsync(
        Version version,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)

    // Pre-built TransportRegistry (multi-version or custom setup)
    protected async Task<HttpResponseMessage> SendClientAsync(
        TransportRegistry transports,
        Version version,
        HttpRequestMessage request,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)

    // H2 frame-level variant
    protected async Task<HttpResponseMessage> SendClientH2Async(
        HttpRequestMessage request,
        params byte[][] serverFrames)

    // H3 frame-level variant
    protected async Task<HttpResponseMessage> SendClientH3Async(
        HttpRequestMessage request,
        params byte[][] serverFrames)

    // Multi-request variant
    protected async Task<List<HttpResponseMessage>> SendClientManyAsync(
        Version version,
        IReadOnlyList<HttpRequestMessage> requests,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null)
}
```

**What this exercises vs existing engine-level entry point:**

| Aspect | Engine-level (`AcceptanceTestBase`) | Client-level (`ClientAcceptanceTestBase`) |
|--------|-------------------------------------|------------------------------------------|
| Entry point | `Source.Single(request).Via(engineFlow)` | `client.SendAsync(request)` |
| Channels | No | Yes — request/response channels |
| Consumer actor | No | Yes — request enrichment, response routing |
| StreamOwner actor | No | Yes — pipeline materialization, KillSwitch |
| PartitionHub | No | Yes — response partitioning by ConsumerId |
| PendingRequest lifecycle | No | Yes — rent, stamp, await, return |
| Timeout / cancellation | No | Yes — CTS pool, CancelAfter |
| Feature pipeline | Partial (engine-joined) | Full (through DI builder config) |

### 5. Migration Strategy

All ~120 existing acceptance tests migrate from `AcceptanceTestBase` to `ClientAcceptanceTestBase`. Test assertions stay identical — only the entry point changes.

**Migration pattern:**

Before:
```csharp
public sealed class SmokeSpec : AcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Smoke_should_send_get_and_receive_200()
    {
        var response = await SendScriptedAsync(
            CreateHttp11Engine(), request, (_, _) => responseBytes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

After:
```csharp
public sealed class SmokeSpec : ClientAcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    public async Task Smoke_should_send_get_and_receive_200()
    {
        var response = await SendClientAsync(
            HttpVersion.Version11, request, (_, _) => responseBytes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

**Phase order:**

1. **Infrastructure** — Internal hook, `FakeResponse`, `ClientAcceptanceHelper`, `ClientAcceptanceTestBase`
2. **Proof-of-concept** — Migrate `H11/SmokeSpec`, verify full pipeline materializes and round-trips
3. **H11 tests** — Simplest protocol, most tests, validates the pattern at scale
4. **H10 tests** — Simple, few tests
5. **H2 tests** — Multiplexed, exercises PartitionHub more heavily
6. **H3 tests** — QUIC frame format, similar to H2
7. **Feature tests** — Cache, Cookies, Compression, Redirect, Retry, ErrorHandling, Resilience
8. **Cleanup** — `AcceptanceTestBase` stays as a base class (provides engine factories + raw-byte helpers). `SendScriptedAsync` / `SendScriptedWithCaptureAsync` are removed once all tests use `SendClientAsync`. The raw-byte factories from `EngineTestBase` remain — `ClientAcceptanceTestBase` inherits them.

Folder structure stays unchanged — tests remain in `H10/`, `H11/`, `H2/`, `H3/`, etc.

### 6. Design Clarifications

**ActorSystem lifecycle:** `ClientAcceptanceHelper` creates its own `ActorSystem` per helper instance (same pattern as `ClientHelper`). Each test gets an isolated actor system that is terminated on dispose.

**DI + internal overload:** `ClientAcceptanceHelper.Create()` builds a `ServiceCollection`, registers TurboHttpClient via `AddTurboHttpClient()`, resolves `ITurboHttpClientFactory` from DI, then calls the internal `CreateClient(name, transportOverride)` overload. The test project has `InternalsVisibleTo` access.

**AcceptanceTestBase after migration:** Stays as an intermediate base class in the inheritance chain (`ClientAcceptanceTestBase : AcceptanceTestBase : EngineTestBase : StreamTestBase`). It keeps the engine factory methods (`CreateHttp11Engine()` etc.) which may still be useful for isolated engine-level debugging. The `SendScriptedAsync` methods are removed once all tests are migrated.

### 7. Files Changed (Production)

| File | Change |
|------|--------|
| `TurboHTTP/Streams/Lifecycle/StreamOwner.cs` | Add `TransportRegistry? transportOverride` constructor param + conditional in `MaterializeStream()` |
| `TurboHTTP/Streams/Lifecycle/StreamManager.cs` | Add `TransportRegistry? TransportOverride` to `RegisterConsumer` record, forward to `StreamOwner` |
| `TurboHTTP/TurboHttpClientFactory.cs` | Add internal `CreateClient(string, TransportRegistry?)` overload |

### 8. Files Changed (Acceptance Tests — Migration)

All test classes in `TurboHTTP.AcceptanceTests/` change base class from `AcceptanceTestBase` to `ClientAcceptanceTestBase` and replace `SendScriptedAsync(CreateHttpXXEngine(), ...)` calls with `SendClientAsync(HttpVersion.VersionXX, ...)`.

### 9. Files Added (Test Infrastructure)

| File | Purpose |
|------|---------|
| `TurboHTTP.Tests.Shared/FakeResponse.cs` | High-level HTTP response byte builder |
| `TurboHTTP.Tests.Shared/ClientAcceptanceHelper.cs` | Builds real `ITurboHttpClient` with fake transports |
| `TurboHTTP.Tests.Shared/ClientAcceptanceTestBase.cs` | Base class for all acceptance tests |
