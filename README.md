<div align="center">
  <img src="docs/logo/logo.svg" alt="TurboHTTP" width="200" />
  <p><strong>High-performance HTTP client and server for .NET — built on Akka.Streams with full protocol support from HTTP/1.0 through HTTP/3 (QUIC).</strong></p>

  [![CI](https://img.shields.io/github/actions/workflow/status/Leberkas-org/TurboHTTP/ci.yml?label=CI)](https://github.com/Leberkas-org/TurboHTTP/actions/workflows/ci.yml)
  [![Release](https://img.shields.io/github/actions/workflow/status/Leberkas-org/TurboHTTP/release.yml?label=Release)](https://github.com/Leberkas-org/TurboHTTP/actions/workflows/release.yml)
  [![NuGet](https://img.shields.io/nuget/v/TurboHTTP.svg)](https://www.nuget.org/packages/TurboHTTP)
  [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
</div>

---

## Why TurboHTTP?

TurboHTTP is a reactive, backpressure-aware HTTP stack built on [Akka.Streams](https://getakka.net/). Actors manage connection lifecycle while data flows through `System.Threading.Channels` — zero bytes ever touch an actor mailbox. Both the client and the server share the same protocol layer, transport, and stream pipeline, giving you a symmetric architecture from HTTP/1.0 through HTTP/3. The result: high throughput, low allocations, and a pipeline that never dies on transient errors.

---

## Features

### Protocol

- **HTTP/1.0 and HTTP/1.1** — chunked transfer, keep-alive, pipelining, h2c upgrade detection
- **HTTP/2** — binary framing, stream multiplexing, HPACK compression, per-stream flow control
- **HTTP/3 (QUIC)** — UDP transport, QPACK compression, 0-RTT connection establishment
- **Dynamic protocol negotiation** — ALPN and HTTP/2 preface detection for automatic version selection

### Client

- **Immortal pipeline** — transport failures, protocol violations, and corrupt data are absorbed gracefully; the stream only completes when you dispose the client
- **Automatic retries** — idempotent methods retried on transient failures with `Retry-After` support
- **Connection pooling** — per-host pools with configurable limits, idle eviction, and exponential backoff reconnect
- **Redirect following** — 301/302/303/307/308 with correct method rewriting, body preservation, loop detection, and HTTPS downgrade protection
- **Cookie management** — automatic storage and injection with domain/path matching, `Secure`, `HttpOnly`, `SameSite` support; pluggable via `ICookieJar`
- **HTTP caching** — LRU cache with `Vary`, conditional requests (`ETag`, `Last-Modified`), freshness evaluation, and 304 merging; pluggable via `ICacheStore`
- **Content encoding** — automatic gzip, deflate, and Brotli decompression; optional request compression
- **100-Continue** — `Expect: 100-continue` handling for large request bodies
- **Alt-Svc** — alternative service discovery and connection migration

### Server

- **Standalone HTTP server** — no Kestrel dependency, built entirely on Akka.Streams
- **ASP.NET-style middleware pipeline** — composable `TurboRequestDelegate` middleware with `Use`, `Map`, and `Run`
- **Entity gateway** — route HTTP requests to Akka.NET actors with ask/tell semantics, response mapping, and timeout support
- **Routing and model binding** — attribute-based and fluent route registration with JSON body binding, query string binding, and parameter validation
- **TLS/HTTPS** — SNI-based certificate selection, client certificate modes (require/allow/deny), renegotiation, and `ITlsHandshakeFeature`
- **Connection management** — `MaxConcurrentConnections` per listener, connection logging with wire-level hex dumps
- **Per-protocol server options** — separate `Http1ServerOptions`, `Http2ServerOptions`, `Http3ServerOptions` with RFC-aligned defaults

### Performance

- **Zero-allocation hot paths** — `MemoryPool<byte>`, `Span<T>`, `ReadOnlyMemory<byte>`, and `System.Threading.Channels` throughout
- **HTTP/2 multiplexing** — multiple concurrent requests over a single TCP connection with per-stream flow control
- **Backpressure** — Akka.Streams backpressure propagates end-to-end from network to caller
- **Channel-based API** — bypass `SendAsync` and write/read directly to `System.Threading.Channels` for pipelined I/O

### Extensibility

- **Handler pipeline** — custom request/response transforms via `TurboHandler` subclasses or inline delegates
- **Pluggable storage** — bring your own `ICookieJar` or `ICacheStore` for custom persistence backends
- **OpenTelemetry tracing** — built-in `TracingBidiStage` for request/response lifecycle visibility
- **DI integration** — `IServiceCollection` support with named/typed clients and `IOptionsMonitor` for runtime config changes
- **Comprehensive test suite** — unit, stream stage, acceptance, integration, API surface, and benchmark tests

---

## Getting Started

```bash
dotnet add package TurboHTTP
```

Requires **.NET 10.0** or later.

### Client

```csharp
var services = new ServiceCollection();
services.AddTurboHttpClient("GitHub", options =>
{
    options.BaseAddress = new Uri("https://api.github.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
})
.WithRedirect()
.WithCookies()
.WithRetry(retry => retry.MaxRetries = 3);

var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<ITurboHttpClientFactory>().CreateClient("GitHub");

var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/users"));
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

### Server

```csharp
var services = new ServiceCollection();
services.AddTurboServer(server =>
{
    server.Listen("https://localhost:5001", listen =>
    {
        listen.UseHttps();
        listen.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

services.AddTurboRouting(routes =>
{
    routes.MapGet("/hello", () => Results.Ok("Hello from TurboHTTP!"));
    routes.MapTurboEntity<OrderActor>("/orders/{id}")
        .Ask(HttpMethod.Get, msg => new GetOrder(msg.RouteValues["id"]))
        .Tell(HttpMethod.Post, msg => new CreateOrder(msg.Body));
});
```

For more examples — channel API, custom handlers, cookie jars, cache stores, entity gateway patterns — see the [documentation site](https://turbohttp.leberkas.org/).

---

## Architecture

### Client

```
ITurboHttpClient (SendAsync / channel API)
    |
Feature Pipeline    Tracing > Handlers > Redirect > Cookie > Retry >
                    Expect-Continue > Cache > ContentEncoding > Alt-Svc
    |
Engine              Version router > per-version client engines
                    HTTP/1.0 | HTTP/1.1 | HTTP/2 | HTTP/3
    |
Protocol            Encoding/decoding, HPACK/QPACK, frame types
    |
Transport           TCP (ConnectionManagerActor > Channel<byte> > ClientByteMover)
                    QUIC (ConnectionManagerActor > QUIC streams)
```

### Server

```
Transport           TcpListenerStage / QuicListenerStage
    |
Connection          ConnectionActor > protocol negotiation (ALPN / preface detection)
    |
Protocol            Per-version server engines
                    HTTP/1.0 | HTTP/1.1 | HTTP/2 | HTTP/3
    |
Context             TurboHttpContext (request, response, features, connection info)
    |
Middleware          Pipeline stages: logging > routing > entity dispatch > handlers
    |
Application         TurboRequestDelegate / Actor entity gateway (ask/tell)
```

For interactive architecture diagrams, see the [documentation site](https://turbohttp.leberkas.org/).

---

## Building from Source

```bash
dotnet restore ./src/TurboHTTP.slnx
dotnet build --configuration Release ./src/TurboHTTP.slnx

# Tests (xUnit v3 — use dotnet run, not dotnet test)
dotnet run --project ./src/TurboHTTP.Tests/TurboHTTP.Tests.csproj
dotnet run --project ./src/TurboHTTP.IntegrationTests.Client/TurboHTTP.IntegrationTests.Client.csproj
dotnet run --project ./src/TurboHTTP.IntegrationTests.Server/TurboHTTP.IntegrationTests.Server.csproj
dotnet run --project ./src/TurboHTTP.IntegrationTests.End2End/TurboHTTP.IntegrationTests.End2End.csproj
dotnet run --project ./src/TurboHTTP.AcceptanceTests/TurboHTTP.AcceptanceTests.csproj

# Benchmarks
dotnet run --configuration Release --project ./src/TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj
```

---

## Documentation

Full documentation — including feature guides, architecture deep-dives, and API references — is available at **[turbohttp.leberkas.org](https://turbohttp.leberkas.org/)**.

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming conventions, PR requirements, and how to run tests locally.

---

## License

TurboHTTP is licensed under the [MIT License](LICENSE).
