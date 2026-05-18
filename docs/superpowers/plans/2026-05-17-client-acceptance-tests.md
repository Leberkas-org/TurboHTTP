# Client-Level Acceptance Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate all acceptance tests from engine-level to client-level, so every test goes through `ITurboHttpClient.SendAsync()` → actors → engine → fake transport — proving the full pipeline works without network I/O.

**Architecture:** Add an optional `TransportRegistry?` parameter through the internal `StreamOwner` → `ClientStreamManager` → `TurboHttpClientFactory` chain so tests can inject fake transports. Build `ClientAcceptanceHelper` (mirrors integration `ClientHelper` but with fakes) and `ClientAcceptanceTestBase` as the new base class. Migrate all ~94 test files from `AcceptanceTestBase` to `ClientAcceptanceTestBase`.

**Tech Stack:** C# 12, xUnit v3, Akka.NET Streams, Microsoft.Extensions.DependencyInjection

**Spec:** `docs/superpowers/specs/2026-05-17-client-acceptance-tests-design.md`

---

## File Structure

### Production Changes (3 files)

| File | Change |
|------|--------|
| `src/TurboHTTP/Streams/Lifecycle/StreamOwner.cs` | Add `TransportRegistry? _transportOverride` field + constructor param, conditional in `MaterializeStream()` |
| `src/TurboHTTP/Streams/Lifecycle/StreamManager.cs` | Add `TransportRegistry? TransportOverride` to `RegisterConsumer` record, pass to `StreamOwner` constructor |
| `src/TurboHTTP/TurboHttpClientFactory.cs` | Add internal `CreateClient(string, TransportRegistry?)` overload |

### New Test Infrastructure (3 files)

| File | Purpose |
|------|---------|
| `src/TurboHTTP.Tests.Shared/FakeResponse.cs` | High-level HTTP response byte builder |
| `src/TurboHTTP.Tests.Shared/ClientAcceptanceHelper.cs` | Builds real `ITurboHttpClient` with fake transports via DI |
| `src/TurboHTTP.Tests.Shared/ClientAcceptanceTestBase.cs` | New base class for all acceptance tests |

### Migration (94 test files)

All files in `src/TurboHTTP.AcceptanceTests/` change base class from `AcceptanceTestBase` to `ClientAcceptanceTestBase` and replace engine-direct entry points with `SendClientAsync` / `SendClientH2Async` / `SendClientH3Async`.

---

## Task 1: StreamOwner Transport Override

Add the internal hook that allows tests to bypass real TCP/QUIC transport creation.

**Files:**
- Modify: `src/TurboHTTP/Streams/Lifecycle/StreamOwner.cs`

- [ ] **Step 1: Add `_transportOverride` field and constructor parameter**

In `StreamOwner`, add the field and update the constructor:

```csharp
private readonly TransportRegistry? _transportOverride;

public StreamOwner(TurboClientOptions clientOptions, PipelineDescriptor pipeline,
    TransportRegistry? transportOverride = null)
{
    _clientOptions = clientOptions;
    _pipeline = pipeline;
    _transportOverride = transportOverride;

    Initializing();
}
```

- [ ] **Step 2: Add conditional in `MaterializeStream()`**

Replace lines 121-150 (pool config through transport creation) with a conditional. When `_transportOverride` is set, skip creating TCP/QUIC manager actors entirely:

```csharp
TransportRegistry transports;
if (_transportOverride is not null)
{
    transports = _transportOverride;
}
else
{
    var poolRegistry = new PoolConfigRegistry(new TcpPoolConfig(
            1,
            _clientOptions.PooledConnectionIdleTimeout,
            _clientOptions.PooledConnectionLifetime,
            ReuseOnUpstreamFinish: true))
        .Register(PoolKeys.Http10, new TcpPoolConfig(
            MaxConnectionsPerHost: int.MaxValue,
            IdleTimeout: TimeSpan.Zero,
            ConnectionLifetime: TimeSpan.Zero,
            ReuseOnUpstreamFinish: false))
        .Register(PoolKeys.Http11, new TcpPoolConfig(
            _clientOptions.Http1.MaxConnectionsPerServer,
            _clientOptions.PooledConnectionIdleTimeout,
            _clientOptions.PooledConnectionLifetime,
            ReuseOnUpstreamFinish: true))
        .Register(PoolKeys.Http2, new TcpPoolConfig(
            _clientOptions.Http2.MaxConnectionsPerServer,
            _clientOptions.PooledConnectionIdleTimeout,
            _clientOptions.PooledConnectionLifetime,
            ReuseOnUpstreamFinish: false));

    _tcpManager = Context.ActorOf(TransportFactory.CreateTcpConnectionManager(poolRegistry), "tcp-pool");
    _quicConnectionManager = Context.ActorOf(TransportFactory.CreateQuicConnectionManager(), "quic-pool");

    transports = new TransportRegistry()
        .Register(HttpVersion.Version10, TransportFactory.CreateTcpClient(_tcpManager, new Http10PoolingStrategy()))
        .Register(HttpVersion.Version11, TransportFactory.CreateTcpClient(_tcpManager, new Http11PoolingStrategy()))
        .Register(HttpVersion.Version20, TransportFactory.CreateTcpClient(_tcpManager, new Http2PoolingStrategy()))
        .Register(HttpVersion.Version30, TransportFactory.CreateQuicClient(_quicConnectionManager));
}
```

The rest of `MaterializeStream()` (engine creation, KillSwitch, MergeHub, PartitionHub) stays unchanged.

- [ ] **Step 3: Verify compilation**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds. No tests affected yet — default parameter is `null`.

- [ ] **Step 4: Commit**

```
feat: add TransportRegistry override to StreamOwner for testability
```

---

## Task 2: ClientStreamManager Transport Forwarding

Forward the transport override from `RegisterConsumer` message to `StreamOwner` constructor.

**Files:**
- Modify: `src/TurboHTTP/Streams/Lifecycle/StreamManager.cs`

- [ ] **Step 1: Add `TransportOverride` to `RegisterConsumer` record**

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

- [ ] **Step 2: Pass `TransportOverride` to `StreamOwner` constructor**

In `HandleRegisterConsumer`, update the `StreamOwner` construction:

```csharp
var owner = Context.ActorOf(
    Akka.Actor.Props.Create(() => new StreamOwner(
        message.ClientOptions,
        message.Pipeline,
        message.TransportOverride)),
    sanitizedName);
```

- [ ] **Step 3: Verify compilation**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```
feat: forward TransportOverride through ClientStreamManager to StreamOwner
```

---

## Task 3: TurboHttpClientFactory Internal Overload

Add the internal `CreateClient` overload that tests will call to inject fake transports.

**Files:**
- Modify: `src/TurboHTTP/TurboHttpClientFactory.cs`

- [ ] **Step 1: Add internal overload**

Add this method to `TurboHttpClientFactory`, right after the existing public `CreateClient(string name)`:

```csharp
internal ITurboHttpClient CreateClient(string name, TransportRegistry? transportOverride)
{
    ThrowIfDisposed();

    var clientOptions = options.Get(name);
    var descriptor = descriptors.Get(name);
    var pipeline = BuildPipeline(clientOptions, descriptor);

    var consumerId = Guid.NewGuid();
    var consumerRequests = Channel.CreateUnbounded<HttpRequestMessage>(
        new UnboundedChannelOptions { SingleReader = true });
    var consumerResponses = Channel.CreateUnbounded<HttpResponseMessage>(
        new UnboundedChannelOptions { SingleWriter = true });

    var registration = new NamedClientConsumerRegistration(_manager, name, consumerId);

    var client = new TurboHttpClient(
        consumerRequests.Writer,
        consumerResponses.Reader,
        CreateRequestOptions(clientOptions),
        registration);

    _manager.Tell(new ClientStreamManager.RegisterConsumer(
        name,
        consumerId,
        consumerRequests.Reader,
        consumerResponses.Writer,
        () => client.CachedOptions,
        clientOptions,
        pipeline,
        transportOverride));

    return client;
}
```

- [ ] **Step 2: Refactor existing `CreateClient(string)` to delegate**

Replace the existing `CreateClient(string name)` body with a delegation:

```csharp
public ITurboHttpClient CreateClient(string name) => CreateClient(name, transportOverride: null);
```

- [ ] **Step 3: Verify compilation and run existing tests**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Expected: Build succeeds, all existing tests pass (no behavioral change — `null` override = production path).

- [ ] **Step 4: Commit**

```
feat: add internal CreateClient overload with TransportRegistry override
```

---

## Task 4: FakeResponse Builder

High-level API for building valid HTTP response bytes. Layered on existing `H2ResponseBuilder` / `H3ResponseBuilder`.

**Files:**
- Create: `src/TurboHTTP.Tests.Shared/FakeResponse.cs`

- [ ] **Step 1: Create `FakeResponse` with HTTP/1.x methods**

```csharp
using System.Net;
using System.Text;

namespace TurboHTTP.Tests.Shared;

public static class FakeResponse
{
    private static readonly Dictionary<int, string> ReasonPhrases = new()
    {
        [200] = "OK", [201] = "Created", [204] = "No Content",
        [301] = "Moved Permanently", [302] = "Found", [304] = "Not Modified",
        [307] = "Temporary Redirect", [308] = "Permanent Redirect",
        [400] = "Bad Request", [401] = "Unauthorized", [403] = "Forbidden",
        [404] = "Not Found", [429] = "Too Many Requests",
        [500] = "Internal Server Error", [502] = "Bad Gateway", [503] = "Service Unavailable"
    };

    private static string GetReason(int status) =>
        ReasonPhrases.TryGetValue(status, out var reason) ? reason : "Unknown";

    public static byte[] Http10(int status, string? body = null,
        params (string Name, string Value)[] headers)
        => BuildHttp1("HTTP/1.0", status, body, headers);

    public static byte[] Http11(int status, string? body = null,
        params (string Name, string Value)[] headers)
        => BuildHttp1("HTTP/1.1", status, body, headers);

    public static byte[] Ok(string body) => Http11(200, body);
    public static byte[] NotFound() => Http11(404);

    public static byte[] H2(int status, string? body = null,
        params (string Name, string Value)[] headers)
    {
        var builder = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, status, headers.Length > 0 ? headers.Select(h => (h.Name, h.Value)).ToList() : null,
                endStream: body is null)
            .WindowUpdate(0, 1_048_576);

        if (body is not null)
        {
            builder.Data(1, body);
        }

        return builder.Build();
    }

    public static byte[] H3(int status, string? body = null,
        params (string Name, string Value)[] headers)
    {
        var builder = new H3ResponseBuilder()
            .Headers(0, status, headers.Length > 0 ? headers.Select(h => (h.Name, h.Value)).ToList() : null,
                endStream: body is null);

        if (body is not null)
        {
            builder.Data(0, body);
        }

        return builder.Build();
    }

    private static byte[] BuildHttp1(string version, int status, string? body,
        (string Name, string Value)[] headers)
    {
        var sb = new StringBuilder();
        sb.Append(version).Append(' ').Append(status).Append(' ').Append(GetReason(status)).Append("\r\n");

        foreach (var (name, value) in headers)
        {
            sb.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        var bodyBytes = body is not null ? Encoding.UTF8.GetBytes(body) : [];

        var hasContentLength = false;
        foreach (var (name, _) in headers)
        {
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                hasContentLength = true;
                break;
            }
        }

        if (!hasContentLength)
        {
            sb.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
        }

        sb.Append("\r\n");

        var headerBytes = Encoding.Latin1.GetBytes(sb.ToString());
        var result = new byte[headerBytes.Length + bodyBytes.Length];
        headerBytes.CopyTo(result, 0);
        bodyBytes.CopyTo(result, headerBytes.Length);
        return result;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```
feat: add FakeResponse builder for high-level HTTP response construction
```

---

## Task 5: ClientAcceptanceHelper

Mirrors integration `ClientHelper` but injects fake transports via the internal `CreateClient` overload.

**Files:**
- Create: `src/TurboHTTP.Tests.Shared/ClientAcceptanceHelper.cs`

- [ ] **Step 1: Create `ClientAcceptanceHelper`**

```csharp
using Akka.Actor;
using Akka.Configuration;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Shared;

public sealed class ClientAcceptanceHelper : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private ClientAcceptanceHelper(ServiceProvider provider, ITurboHttpClient client)
    {
        _provider = provider;
        Client = client;
    }

    public ITurboHttpClient Client { get; }

    public static ClientAcceptanceHelper Create(
        TransportRegistry transports,
        Version version,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();

        var diSetup = DependencyResolverSetup.Create(services.BuildServiceProvider());
        var bootstrap = BootstrapSetup.Create();
        var system = ActorSystem.Create($"acceptance-{Guid.NewGuid()}", bootstrap.And(diSetup));

        services.AddSingleton(system);

        var builder = services.AddTurboHttpClient();

        var options = new TurboClientOptions
        {
            BaseAddress = new Uri("http://fake.test")
        };
        configureOptions?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TurboClientOptions>>(
            new FixedOptionsFactory(options)));

        configure?.Invoke(builder);

        var provider = services.BuildServiceProvider();

        var factory = (TurboHttpClientFactory)provider.GetRequiredService<ITurboHttpClientFactory>();
        var client = factory.CreateClient(string.Empty, transports);
        client.BaseAddress = options.BaseAddress;
        client.DefaultRequestVersion = version;
        client.Timeout = TimeSpan.FromSeconds(10);

        return new ClientAcceptanceHelper(provider, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        var system = _provider.GetService<ActorSystem>();
        if (system is not null)
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await _provider.DisposeAsync();
    }

    private sealed class FixedOptionsFactory(TurboClientOptions options) : IOptionsFactory<TurboClientOptions>
    {
        public TurboClientOptions Create(string name) => options;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```
feat: add ClientAcceptanceHelper for building clients with fake transports
```

---

## Task 6: ClientAcceptanceTestBase

New base class with `SendClientAsync` helpers that all acceptance tests will inherit.

**Files:**
- Create: `src/TurboHTTP.Tests.Shared/ClientAcceptanceTestBase.cs`

- [ ] **Step 1: Create `ClientAcceptanceTestBase`**

```csharp
using System.Net;
using TurboHTTP.Streams;

namespace TurboHTTP.Tests.Shared;

public abstract class ClientAcceptanceTestBase : AcceptanceTestBase
{
    protected async Task<HttpResponseMessage> SendClientAsync(
        Version version,
        HttpRequestMessage request,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var transports = new TransportRegistry()
            .Register(version, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, version, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected async Task<HttpResponseMessage> SendClientAsync(
        TransportRegistry transports,
        Version version,
        HttpRequestMessage request,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        await using var helper = ClientAcceptanceHelper.Create(
            transports, version, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected async Task<HttpResponseMessage> SendClientH2Async(
        HttpRequestMessage request,
        byte[] serverFrames,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateH2Connection(serverFrames);
        var transports = new TransportRegistry()
            .Register(HttpVersion.Version20, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, HttpVersion.Version20, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected async Task<HttpResponseMessage> SendClientH3Async(
        HttpRequestMessage request,
        byte[][] serverFrames,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateH3Connection(serverFrames);
        var transports = new TransportRegistry()
            .Register(HttpVersion.Version30, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, HttpVersion.Version30, configure, configureOptions);

        return await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    protected async Task<List<HttpResponseMessage>> SendClientManyAsync(
        Version version,
        IReadOnlyList<HttpRequestMessage> requests,
        Func<int, byte[], byte[]?> responseFactory,
        Action<ITurboHttpClientBuilder>? configure = null,
        Action<TurboClientOptions>? configureOptions = null)
    {
        var stage = CreateScriptedConnection(responseFactory);
        var transports = new TransportRegistry()
            .Register(version, stage.AsFlow());

        await using var helper = ClientAcceptanceHelper.Create(
            transports, version, configure, configureOptions);

        var responses = new List<HttpResponseMessage>();
        foreach (var request in requests)
        {
            var response = await helper.Client.SendAsync(request, TestContext.Current.CancellationToken);
            responses.Add(response);
        }

        return responses;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build --configuration Release src/TurboHTTP.slnx`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```
feat: add ClientAcceptanceTestBase with SendClientAsync helpers
```

---

## Task 7: Proof-of-Concept — Migrate H11/SmokeSpec

Migrate the simplest test to validate the entire infrastructure works end-to-end.

**Files:**
- Modify: `src/TurboHTTP.AcceptanceTests/H11/SmokeSpec.cs`

- [ ] **Step 1: Migrate H11/SmokeSpec**

Replace the entire file content:

```csharp
using System.Net;
using System.Text;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H11;

public sealed class SmokeSpec : ClientAcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task Smoke_should_send_get_request_to_hello_and_receive_200_with_hello_world_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
        {
            Version = HttpVersion.Version11
        };

        const string body = "Hello World";
        var responseBytes = FakeResponse.Http11(200, body);

        var response = await SendClientAsync(
            HttpVersion.Version11, request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", responseBody);
    }
}
```

Key changes from original:
- Base class: `AcceptanceTestBase` → `ClientAcceptanceTestBase`
- No `Engine` property needed
- No `Source.Single().Via().RunWith()` — uses `SendClientAsync`
- Request URI: `http://localhost/hello` → `http://fake.test/hello` (matches `BaseAddress`)
- Response bytes: manual string → `FakeResponse.Http11(200, body)`

- [ ] **Step 2: Run the migrated test**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -class "TurboHTTP.AcceptanceTests.H11.SmokeSpec"`
Expected: 1 test passes. This proves: DI → ActorSystem → ClientStreamManager → StreamOwner (with fake transport) → Engine → fake → response → Consumer → PartitionHub → PendingRequest → client.

- [ ] **Step 3: Commit**

```
feat: migrate H11/SmokeSpec to ClientAcceptanceTestBase (proof-of-concept)
```

---

## Task 8: Migrate H10/SmokeSpec

**Files:**
- Modify: `src/TurboHTTP.AcceptanceTests/H10/SmokeSpec.cs`

- [ ] **Step 1: Migrate to ClientAcceptanceTestBase**

Same pattern as H11 — change base class, replace direct Source.Single flow with `SendClientAsync`, use `HttpVersion.Version10` and `FakeResponse.Http10(...)`.

```csharp
using System.Net;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H10;

public sealed class SmokeSpec : ClientAcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9110-15.3")]
    public async Task Smoke_should_send_get_request_to_hello_and_receive_200_with_hello_world_body()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
        {
            Version = HttpVersion.Version10
        };

        var responseBytes = FakeResponse.Http10(200, "Hello World");

        var response = await SendClientAsync(
            HttpVersion.Version10, request, (_, _) => responseBytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", responseBody);
    }
}
```

- [ ] **Step 2: Run and verify**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -class "TurboHTTP.AcceptanceTests.H10.SmokeSpec"`
Expected: PASS

- [ ] **Step 3: Commit**

```
feat: migrate H10/SmokeSpec to ClientAcceptanceTestBase
```

---

## Task 9: Migrate H2/SmokeSpec

**Files:**
- Modify: `src/TurboHTTP.AcceptanceTests/H2/SmokeSpec.cs`

- [ ] **Step 1: Migrate to ClientAcceptanceTestBase**

H2 uses `SendClientH2Async` instead of `SendClientAsync`, and `H2ResponseBuilder` for frame construction:

```csharp
using System.Net;
using TurboHTTP.Tests.Shared;

namespace TurboHTTP.AcceptanceTests.H2;

public sealed class SmokeSpec : ClientAcceptanceTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-8.1")]
    public async Task Basic_get_request_should_succeed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://fake.test/hello")
        {
            Version = HttpVersion.Version20
        };

        var serverFrames = new H2ResponseBuilder()
            .Settings()
            .SettingsAck()
            .Headers(1, 200, [("content-length", "11")], endStream: false)
            .Data(1, "Hello World")
            .Build();

        var response = await SendClientH2Async(request, serverFrames);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Hello World", body);
    }
}
```

Key changes:
- `SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames)` → `SendClientH2Async(request, serverFrames)`
- No engine creation needed — `ClientAcceptanceTestBase` handles it via `ClientAcceptanceHelper`
- The `(response, outboundFrames)` tuple is reduced to just `response` — outbound frame inspection is not available at client level (this is a protocol-level concern for `TurboHTTP.Tests`)

- [ ] **Step 2: Run and verify**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -class "TurboHTTP.AcceptanceTests.H2.SmokeSpec"`
Expected: PASS

- [ ] **Step 3: Commit**

```
feat: migrate H2/SmokeSpec to ClientAcceptanceTestBase
```

---

## Task 10: Migrate H3/SmokeSpec

**Files:**
- Modify: `src/TurboHTTP.AcceptanceTests/H3/SmokeSpec.cs`

- [ ] **Step 1: Migrate to ClientAcceptanceTestBase**

H3 uses `SendClientH3Async` with `H3ResponseBuilder`. Read the current file, apply the same pattern as H2: replace `SendH3EngineAsync` with `SendClientH3Async`, remove engine construction, change base class.

- [ ] **Step 2: Run and verify**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -class "TurboHTTP.AcceptanceTests.H3.SmokeSpec"`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
feat: migrate H3/SmokeSpec to ClientAcceptanceTestBase
```

---

## Task 11: Batch Migrate H11 Protocol Tests

Migrate all H11 tests that use `SendScriptedAsync` or direct `Source.Single().Via()` patterns.

**Files to migrate:**
- `src/TurboHTTP.AcceptanceTests/H11/ErrorHandlingSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/EdgeCaseSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/ConnectionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/ConcurrencySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/RequestFormatSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/ResilienceSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/CompressionSpec.cs`

**Migration pattern** (same for all — shown here for ErrorHandlingSpec):

1. Change base class: `AcceptanceTestBase` → `ClientAcceptanceTestBase`
2. Remove `private static Http11Engine Engine` property
3. Replace any local `SendScriptedAsync(request, factory)` methods that call `Engine.CreateFlow().Join(...)` with direct calls to `SendClientAsync(HttpVersion.Version11, request, factory)`
4. Replace any direct `Source.Single().Via(Engine.CreateFlow().Join(...))` patterns with `SendClientAsync`
5. Replace `CreateScriptedConnectionWithClose(...)` patterns: these need `SendClientAsync` with a factory that returns bytes then the test expects an exception
6. Update request URIs from `http://localhost/...` to `http://fake.test/...`

- [ ] **Step 1: Migrate each file following the pattern above**

Read each file, apply the mechanical transformation. Tests that use `SendScriptedWithCaptureAsync` (e.g., `RequestFormatSpec`) lose the raw request capture — request byte inspection is a protocol-level concern. Migrate these to `SendClientAsync` and remove assertions on raw request bytes (move those assertions to `TurboHTTP.Tests` if not already covered).

Tests that use `SendDecompressingAsync` (e.g., `CompressionSpec`, `ResilienceSpec`) should be migrated to use `SendClientAsync` with `.WithDecompression()` configured via the `configure` parameter:

```csharp
var response = await SendClientAsync(
    HttpVersion.Version11, request, (_, _) => compressedResponseBytes,
    configure: builder => builder.WithDecompression());
```

- [ ] **Step 2: Run all H11 tests**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -namespace "TurboHTTP.AcceptanceTests.H11"`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
refactor: migrate H11 protocol tests to ClientAcceptanceTestBase
```

---

## Task 12: Batch Migrate H10 Protocol Tests

Same pattern as Task 11, but with `HttpVersion.Version10` and `FakeResponse.Http10(...)`.

**Files to migrate:**
- `src/TurboHTTP.AcceptanceTests/H10/ErrorHandlingSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/EdgeCaseSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/ConnectionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/ConcurrencySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/RequestFormatSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/ResilienceSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/CompressionSpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run all H10 tests**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -namespace "TurboHTTP.AcceptanceTests.H10"`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
refactor: migrate H10 protocol tests to ClientAcceptanceTestBase
```

---

## Task 13: Batch Migrate H2 Protocol Tests

Migrate all H2 tests that use `SendH2EngineAsync` or `SendH2EngineAsyncMany`.

**Files to migrate:**
- `src/TurboHTTP.AcceptanceTests/H2/ErrorHandlingSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/EdgeCaseSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/ConnectionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/ConcurrencySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/RequestFormatSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/ResilienceSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/CompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/MaxConcurrentStreamsSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/RequestCompressionSpec.cs`

**Migration pattern:**
1. Change base class to `ClientAcceptanceTestBase`
2. Replace `SendH2EngineAsync(CreateHttp20Engine().CreateFlow(), request, serverFrames)` with `SendClientH2Async(request, serverFrames)`
3. Drop the outbound frames return value — assertions on sent frames belong in `TurboHTTP.Tests`
4. Tests using `SendH2EngineAsyncMany` should use `SendClientManyAsync` with the H2 transport registered manually
5. Tests that configure engine options (e.g., `CreateHttp20Engine(o => o.MaxConcurrentStreams = 5)`) use the `configureOptions` parameter

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run all H2 tests**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -namespace "TurboHTTP.AcceptanceTests.H2"`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
refactor: migrate H2 protocol tests to ClientAcceptanceTestBase
```

---

## Task 14: Batch Migrate H3 Protocol Tests

Same as Task 13 but with `SendClientH3Async` and `H3ResponseBuilder`.

**Files to migrate:**
- `src/TurboHTTP.AcceptanceTests/H3/ErrorHandlingSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/EdgeCaseSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/ConnectionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/ConcurrencySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/RequestFormatSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/ResilienceSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/CompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/MaxStreamConcurrencySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/RequestCompressionSpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run all H3 tests**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -namespace "TurboHTTP.AcceptanceTests.H3"`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
refactor: migrate H3 protocol tests to ClientAcceptanceTestBase
```

---

## Task 15: Migrate Feature Tests — CacheSpec (All Protocols)

Feature tests are the most complex migration. They currently test BidiStages in isolation with `ResponseMapFake`. After migration, they go through the full client pipeline with features configured via builder.

**Files to migrate:**
- `src/TurboHTTP.AcceptanceTests/H10/CacheSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/CacheSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/CacheSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/CacheSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/CacheSpec.cs`

**Migration pattern** (shown for H11/CacheSpec):

The current test creates a `CacheBidiStage` directly and composes it with `ResponseMapFake`. After migration:
1. Configure caching via `builder.WithCache()`
2. Use `SendClientAsync` with a fake transport that returns HTTP/1.1 bytes with appropriate `Cache-Control` headers
3. For tests that send multiple requests to verify caching behavior, use `ClientAcceptanceHelper` directly to keep the client alive across requests

**Example — `Cache_should_serve_max_age_response_from_cache()`:**

Before (BidiStage level):
```csharp
var map = new ResponseMap().On("/cache/max-age/3600", _ => CacheableResponse("body", "max-age=3600"));
var store = new Cache(CachePolicy.Default);
var response1 = await SendAsync(map, request1, store);
var response2 = await SendAsync(map, request2, store);
```

After (client level):
```csharp
var callCount = 0;
var stage = CreateScriptedConnection((_, _) =>
{
    callCount++;
    return FakeResponse.Http11(200, $"body-{callCount}", ("Cache-Control", "max-age=3600"));
});
var transports = new TransportRegistry().Register(HttpVersion.Version11, stage.AsFlow());

await using var helper = ClientAcceptanceHelper.Create(
    transports, HttpVersion.Version11,
    configure: builder => builder.WithCache());

var response1 = await helper.Client.SendAsync(request1, ct);
var body1 = await response1.Content.ReadAsStringAsync(ct);

var response2 = await helper.Client.SendAsync(request2, ct);
var body2 = await response2.Content.ReadAsStringAsync(ct);

Assert.Equal(body1, body2); // Second request served from cache
```

The key difference: tests that need multiple requests through the same client instance must use `ClientAcceptanceHelper` directly instead of `SendClientAsync` (which creates + disposes a helper per call).

- [ ] **Step 1: Add `SendClientWithHelperAsync` helper to `ClientAcceptanceTestBase` for multi-request scenarios if not already sufficient, or just use `ClientAcceptanceHelper.Create()` directly in tests**

- [ ] **Step 2: Migrate CacheSpec for each protocol**

Each protocol variant follows the same pattern — only the version and response format differ:
- H10: `FakeResponse.Http10(...)`, `HttpVersion.Version10`
- H11: `FakeResponse.Http11(...)`, `HttpVersion.Version11`
- H2: `FakeResponse.H2(...)`, `HttpVersion.Version20`
- H3: `FakeResponse.H3(...)`, `HttpVersion.Version30`
- TLS: Same as H11 but with `https://fake.test` base address

- [ ] **Step 3: Run and verify**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -class "TurboHTTP.AcceptanceTests.H11.CacheSpec"`
Expected: All tests PASS

- [ ] **Step 4: Commit**

```
refactor: migrate CacheSpec to ClientAcceptanceTestBase (all protocols)
```

---

## Task 16: Migrate Feature Tests — CookieSpec (All Protocols)

Same pattern as CacheSpec. Configure cookies via `builder.WithCookies()`, use fake transport returning responses with `Set-Cookie` headers.

**Files:**
- `src/TurboHTTP.AcceptanceTests/H10/CookieSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/CookieSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/CookieSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/CookieSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/CookieSpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run and verify**
- [ ] **Step 3: Commit**

```
refactor: migrate CookieSpec to ClientAcceptanceTestBase (all protocols)
```

---

## Task 17: Migrate Feature Tests — RedirectSpec (All Protocols)

Configure redirects via `builder.WithRedirect()`, fake transport returns 301/302/307/308 with `Location` headers.

**Files:**
- `src/TurboHTTP.AcceptanceTests/H10/RedirectSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/RedirectSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/RedirectSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/RedirectSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/RedirectSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/RedirectSecuritySpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/RedirectSecuritySpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run and verify**
- [ ] **Step 3: Commit**

```
refactor: migrate RedirectSpec to ClientAcceptanceTestBase (all protocols)
```

---

## Task 18: Migrate Feature Tests — RetrySpec, OptionsSpec (All Protocols)

**Files:**
- `src/TurboHTTP.AcceptanceTests/H10/RetrySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/RetrySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/RetrySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/RetrySpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/RetrySpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/OptionsSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/OptionsSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/OptionsSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/OptionsSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/OptionsSpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run and verify**
- [ ] **Step 3: Commit**

```
refactor: migrate RetrySpec and OptionsSpec to ClientAcceptanceTestBase (all protocols)
```

---

## Task 19: Migrate Feature Interaction Tests (All Protocols)

These test multiple features working together (cookie+redirect, cache+retry, etc.). Each needs multiple features configured on the builder.

**Files:**
- `src/TurboHTTP.AcceptanceTests/H10/FeatureInteractionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/FeatureInteractionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/FeatureInteractionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/FeatureInteractionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/FeatureInteractionTlsSpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run and verify**
- [ ] **Step 3: Commit**

```
refactor: migrate FeatureInteractionSpec to ClientAcceptanceTestBase (all protocols)
```

---

## Task 20: Migrate Handler, ExpectContinue, RequestCompression Tests (All Protocols)

**Files:**
- `src/TurboHTTP.AcceptanceTests/H11/HandlerPipelineSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/HandlerPipelineSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/HandlerPipelineSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/ExpectContinueSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H10/RequestCompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H11/RequestCompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H2/RequestCompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/H3/RequestCompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/RequestCompressionSpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run and verify**
- [ ] **Step 3: Commit**

```
refactor: migrate Handler, ExpectContinue, RequestCompression tests to ClientAcceptanceTestBase
```

---

## Task 21: Migrate TLS and Proxy Tests

TLS tests use `https://` URIs — configure `BaseAddress` accordingly. Proxy tests use `CreateProxyConnection()`.

**Files:**
- `src/TurboHTTP.AcceptanceTests/TLS/SmokeSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/ErrorHandlingSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/ResilienceSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/ConnectionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/CompressionSpec.cs`
- `src/TurboHTTP.AcceptanceTests/TLS/IntegrationSpec.cs`
- `src/TurboHTTP.AcceptanceTests/Proxy/ProxyConnectSpec.cs`
- `src/TurboHTTP.AcceptanceTests/Proxy/ProxyRelaySpec.cs`

- [ ] **Step 1: Migrate each file**
- [ ] **Step 2: Run and verify**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -namespace "TurboHTTP.AcceptanceTests.TLS"`
Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj -- -namespace "TurboHTTP.AcceptanceTests.Proxy"`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
refactor: migrate TLS and Proxy tests to ClientAcceptanceTestBase
```

---

## Task 22: Migrate Diagnostics and Shared Tests

**Files:**
- `src/TurboHTTP.AcceptanceTests/Diagnostics/LoggingBridgeSpec.cs`

Shared utility tests (`Shared/BehaviorStackSpec.cs`, `Shared/H2ResponseBuilderSpec.cs`, etc.) do NOT inherit from `AcceptanceTestBase` — they stay unchanged.

- [ ] **Step 1: Migrate LoggingBridgeSpec if it uses `AcceptanceTestBase`; skip if it uses its own base**
- [ ] **Step 2: Run full suite**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj`
Expected: All tests PASS

- [ ] **Step 3: Commit**

```
refactor: migrate remaining acceptance tests to ClientAcceptanceTestBase
```

---

## Task 23: Cleanup — Remove Unused Engine-Level Helpers

After all tests are migrated, remove the now-unused methods from `AcceptanceTestBase`.

**Files:**
- Modify: `src/TurboHTTP.Tests.Shared/AcceptanceTestBase.cs`

- [ ] **Step 1: Remove `SendScriptedAsync` and `SendScriptedWithCaptureAsync` from `AcceptanceTestBase`**

These are replaced by `SendClientAsync` in `ClientAcceptanceTestBase`. Keep `AcceptanceTestBase` as a base class (it still provides engine factory methods via `CreateHttp10Engine()` etc.) and `SendWithFakeAsync` if still referenced.

- [ ] **Step 2: Check for remaining references to removed methods**

Run: `grep -r "SendScriptedAsync\|SendScriptedWithCaptureAsync" src/TurboHTTP.AcceptanceTests/`
Expected: No matches (all migrated)

Also check `TurboHTTP.Tests` — if it uses these methods, keep them or move them to a different base.

- [ ] **Step 3: Run full test suite**

Run: `dotnet run --project src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj`
Run: `dotnet run --project src/TurboHTTP.Tests/TurboHTTP.Tests.csproj`
Expected: All tests PASS in both projects

- [ ] **Step 4: Commit**

```
refactor: remove unused engine-level Send helpers from AcceptanceTestBase
```

---

## Notes for Implementers

### URI Convention
All acceptance tests should use `http://fake.test/...` as the base URI (matching `ClientAcceptanceHelper`'s default `BaseAddress`). TLS tests use `https://fake.test/...`.

### Multi-Request Tests
Tests that need to send multiple requests through the **same client instance** (cache validation, cookie persistence, redirect chains) must use `ClientAcceptanceHelper.Create()` directly rather than `SendClientAsync` (which creates/disposes a helper per call).

### Request Version
The `HttpRequestMessage.Version` property must match the `Version` passed to `ClientAcceptanceHelper.Create()`. Mismatched versions will cause `EndpointDispatchStage` to route to the wrong engine.

### Outbound Frame Inspection
Engine-level tests that inspect outbound frames (via `SendH2EngineAsync` returning `IReadOnlyList<Http2Frame>`) lose this capability at client level. Move frame-level assertions to `TurboHTTP.Tests` if not already covered there.

### Feature Test Pattern
Feature tests that previously composed BidiStages with `ResponseMapFake` now configure features via the builder (`WithCache()`, `WithCookies()`, etc.) and provide HTTP response bytes via the fake transport. The fake transport must return bytes with appropriate headers (e.g., `Cache-Control`, `Set-Cookie`, `Location`) to trigger feature behavior.
