<div align="center">
  <img src="docs/logo/logo.svg" alt="TurboHTTP" width="200" />
  <p><strong>High-performance HTTP client for .NET — built on Akka.Streams with automatic retries, caching, cookies, HTTP/2 multiplexing, and HTTP/3 (QUIC).</strong></p>

  [![CI](https://img.shields.io/github/actions/workflow/status/st0o0/TurboHTTP/ci.yml?label=CI)](https://github.com/st0o0/TurboHTTP/actions/workflows/ci.yml)
  [![Release](https://img.shields.io/github/actions/workflow/status/st0o0/TurboHTTP/release.yml?label=Release)](https://github.com/st0o0/TurboHTTP/actions/workflows/release.yml)
  [![NuGet](https://img.shields.io/nuget/v/TurboHTTP.svg)](https://www.nuget.org/packages/TurboHTTP)
  [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
</div>

---

## Why TurboHTTP?

TurboHTTP replaces `HttpClient` with a reactive, backpressure-aware HTTP pipeline built on [Akka.Streams](https://getakka.net/). Actors manage connection lifecycle while data flows through `System.Threading.Channels` — zero bytes ever touch an actor mailbox. The result: high throughput, low allocations, and a pipeline that never dies on transient errors.

---

## Features

### Protocol Support

- **HTTP/1.0 and HTTP/1.1** — chunked transfer encoding, keep-alive, pipelining
- **HTTP/2** — binary framing, stream multiplexing, HPACK header compression, flow control
- **HTTP/3 (QUIC)** — UDP-based transport, QPACK header compression, 0-RTT connection establishment

### Resilience

- **Immortal pipeline** — transport failures, protocol violations, and corrupt data are absorbed gracefully. The stream only completes when you dispose the client. No single bad request or broken connection can take down the pipeline.
- **Automatic retries** — idempotent methods (GET, PUT, DELETE, HEAD, OPTIONS) are retried automatically on transient failures. Respects `Retry-After` headers. POST and other non-idempotent methods are never retried.
- **Connection pooling** — per-host connection pools with configurable limits, idle eviction, and automatic reconnect with exponential backoff. Connections are reused transparently across requests.

### HTTP Features

- **Redirect following** — 301, 302, 303, 307, 308 with correct method rewriting (POST to GET on 303), body preservation on 307/308, loop detection, and HTTPS-to-HTTP downgrade protection. Configurable max redirects.
- **Cookie management** — automatic cookie storage and injection across requests. Supports domain/path matching, `Secure`, `HttpOnly`, `SameSite`, `Max-Age`, and `Expires`. Bring your own `ICookieJar` implementation or use the built-in `CookieJar`.
- **HTTP caching** — in-memory LRU cache with `Vary` support, conditional requests via `ETag`/`If-None-Match` and `Last-Modified`/`If-Modified-Since`, freshness evaluation (`max-age`, `s-maxage`, `Expires`, heuristic), and 304 response merging. Pluggable via `ICacheStore` for custom storage backends (Redis, disk, etc.).
- **Content encoding** — automatic gzip, deflate, and Brotli response decompression. Optional request body compression. Can be disabled per-client if you need raw compressed bytes.
- **100-Continue** — `Expect: 100-continue` handling for large request bodies.

### Performance

- **Zero-allocation internals** — `MemoryPool<byte>`, `Span<T>`, `ReadOnlyMemory<byte>`, and `System.Threading.Channels` throughout the hot path
- **HTTP/2 multiplexing** — multiple concurrent requests over a single TCP connection with header compression and per-stream flow control
- **Backpressure** — Akka.Streams backpressure propagates end-to-end from the network to the caller, preventing buffer bloat and memory exhaustion under load
- **Channel-based API** — for high-throughput scenarios, bypass `SendAsync` and write/read directly to `System.Threading.Channels` for pipelined I/O

### Extensibility

- **Handler pipeline** — compose custom request/response transforms via `TurboHandler` subclasses or inline delegates, ordered FIFO
- **Pluggable storage** — bring your own `ICookieJar` for custom cookie persistence or `ICacheStore` for external cache backends (Redis, disk, etc.)
- **Distributed tracing** — built-in OpenTelemetry-compatible tracing via `TracingBidiStage` for request/response lifecycle visibility
- **DI integration** — first-class `IServiceCollection` support with named and typed clients, `IOptionsMonitor` for runtime configuration changes
- **6,300+ tests** — unit tests, stream stage tests, acceptance tests, integration tests, API tests, and benchmarks

---

## Getting Started

### Installation

```bash
dotnet add package TurboHTTP
```

Requires **.NET 10.0** or later.

### Basic Usage

Register and inject via dependency injection:

```csharp
using TurboHTTP;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});

var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient();

var request = new HttpRequestMessage(HttpMethod.Get, "/users");
var response = await client.SendAsync(request);

Console.WriteLine($"Status: {response.StatusCode}");
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine(body);
```

### Dependency Injection

Register named or typed clients with `IServiceCollection`:

```csharp
services
    .AddTurboHttpClient("GitHub", options =>
    {
        options.BaseAddress = new Uri("https://api.github.com");
        options.ConnectTimeout = TimeSpan.FromSeconds(15);
        options.IdleTimeout = TimeSpan.FromSeconds(30);
    })
    .WithRedirect()
    .WithCookies()
    .WithDecompression()
    .WithRetry(retry => retry.MaxRetries = 3)
    .WithCache(cache => cache.MaxEntries = 1000);
```

Then inject and use:

```csharp
public class GitHubService(ITurboHttpClientFactory factory)
{
    private readonly ITurboHttpClient _client = factory.CreateClient("GitHub");

    public async Task<string> GetRepoAsync(string owner, string repo, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/repos/{owner}/{repo}");
        var response = await _client.SendAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

### Channel-based API

For high-throughput scenarios, bypass `SendAsync` and use channels directly:

```csharp
var services = new ServiceCollection();
services.AddTurboHttpClient(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<ITurboHttpClientFactory>();
var client = factory.CreateClient();

// Fire requests
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"));
await client.Requests.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/health"));

// Read responses as they arrive
await foreach (var response in client.Responses.ReadAllAsync())
{
    Console.WriteLine($"{response.RequestMessage!.RequestUri} -> {response.StatusCode}");
}
```

### Custom Cookie Jar

Implement `ICookieJar` to plug in your own cookie storage (e.g. encrypted, persistent, or shared across clients):

```csharp
public sealed class PersistentCookieJar : ICookieJar
{
    public void ProcessResponse(Uri requestUri, HttpResponseMessage response)
    {
        // parse Set-Cookie headers and persist to your backing store
    }

    public void AddCookiesToRequest(Uri requestUri, ref HttpRequestMessage request)
    {
        // load cookies from your backing store and add Cookie header
    }
}

services
    .AddTurboHttpClient("MyApi", options => { ... })
    .WithCookies(new PersistentCookieJar());
```

### Custom Cache Store

Implement `ICacheStore` to use Redis, disk, or any other backend instead of the built-in in-memory LRU cache:

```csharp
public sealed class RedisCacheStore : ICacheStore
{
    public ICacheEntry? Get(HttpRequestMessage request) { /* Redis lookup */ }
    public void Put(HttpRequestMessage request, HttpResponseMessage response,
        IMemoryOwner<byte> bodyOwner, int bodyLength,
        DateTimeOffset requestTime, DateTimeOffset responseTime) { /* Redis store */ }
    public void Invalidate(Uri uri) { /* Redis delete */ }
}

services
    .AddTurboHttpClient("MyApi", options => { ... })
    .WithCache(new RedisCacheStore(), cache => cache.MaxBodyBytes = 10_485_760);
```

### Custom Handlers

Extend the pipeline with custom request/response transforms:

```csharp
public sealed class AuthHandler : TurboHandler
{
    public override HttpRequestMessage ProcessRequest(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "my-token");
        return request;
    }
}

services
    .AddTurboHttpClient("MyApi", options => { ... })
    .AddHandler<AuthHandler>();
```

Or use inline delegates:

```csharp
services
    .AddTurboHttpClient("MyApi")
    .UseRequest(request =>
    {
        request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString());
        return request;
    });
```

### Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `BaseAddress` | `null` | Base URI for resolving relative request URIs |
| `ConnectTimeout` | 10s | Timeout for establishing a new TCP connection |
| `IdleTimeout` | 10s | Time a connection may remain idle before eviction |
| `ReconnectInterval` | 5s | Delay between reconnection attempts after failure |
| `MaxReconnectAttempts` | 10 | Max reconnection attempts before giving up |
| `MaxFrameSize` | 128 KiB | HTTP/2 maximum frame size in bytes |
| `ConnectionPolicy` | `null` | Per-host connection limits and HTTP/2 multiplexing settings |
| `DangerousAcceptAnyServerCertificate` | `false` | Skip TLS validation (dev/test only) |
| `ServerCertificateValidationCallback` | — | Custom TLS certificate validation logic |
| `ClientCertificates` | `null` | X.509 client certificates for mTLS |
| `EnabledSslProtocols` | `SslProtocols.None` | TLS protocol versions to enable (OS default if `None`) |

---

## Architecture

```
Client Layer       ITurboHttpClient (SendAsync / channel API)
      ↓
Feature Layer      Akka.Streams BidiStages — outermost to innermost:
                   Tracing → Handlers → Redirect → Cookie → Retry →
                   Expect-Continue → Cache → ContentEncoding → Alt-Svc
      ↓
Engine Layer       Engine (version router) → per-version engines
                   Each engine: unified ConnectionStage + NetworkBufferBatchStage
                   HTTP/1.0 · HTTP/1.1 · HTTP/2 · HTTP/3
      ↓
Protocol Layer     Encoding/decoding, HPACK/QPACK, frame types — all internal
                   to the unified ConnectionStage per version
      ↓
Transport Layer    TcpConnectionStage / QuicConnectionStage
                   ├─ TCP  → ConnectionManagerActor → Channel<byte> → ClientByteMover
                   └─ QUIC → ConnectionManagerActor → QUIC streams
```

For interactive architecture diagrams, see the [documentation site](https://turbohttp.st0o0.net/).

---

## Documentation

Full documentation — including feature guides, architecture deep-dives, and a comparison with `HttpClient` — is available at **[https://turbohttp.st0o0.net/](https://turbohttp.st0o0.net/)**.

---

## Building from Source

```bash
# Restore and build
dotnet restore ./src/TurboHTTP.slnx
dotnet build --configuration Release ./src/TurboHTTP.slnx

# Run tests by project
dotnet test --project ./src/TurboHTTP.Tests/TurboHTTP.Tests.csproj                # unit
dotnet test --project ./src/TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj    # stream stages
dotnet test --project ./src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj  # acceptance
dotnet test --project ./src/TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj  # integration (network)
dotnet test --project ./src/TurboHTTP.API.Tests/TurboHTTP.API.Tests.csproj        # public API surface

# Run benchmarks
dotnet run --configuration Release --project ./src/TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj
```

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming conventions, PR requirements, how to run tests locally, and recommended branch protection settings.

---

## License

TurboHTTP is licensed under the [MIT License](LICENSE).
