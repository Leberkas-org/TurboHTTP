<div align="center">
  <img src="docs/logo/logo.svg" alt="TurboHttp" width="200" />
  <h1>TurboHttp</h1>
  <p><strong>High-performance HTTP client for .NET — built on Akka.Streams with automatic retries, caching, cookies, HTTP/2 multiplexing, and HTTP/3 (QUIC) support.</strong></p>

  [![Build](https://github.com/st0o0/TurboHttp/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/st0o0/TurboHttp/actions/workflows/build-and-release.yml)
  [![NuGet](https://img.shields.io/nuget/v/TurboHttp.svg)](https://www.nuget.org/packages/TurboHttp)
  [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
</div>

---

## Features

- **HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3** — full protocol support with automatic version negotiation (HTTP/3 via QUIC is implemented but not yet routed in production — see [Roadmap](#roadmap))
- **Immortal pipeline** — transport failures, protocol violations, and corrupt data are absorbed gracefully; the stream only completes on client disposal
- **Automatic retries** — idempotent methods (GET, PUT, DELETE) are retried automatically; respects `Retry-After` headers; POST is never retried
- **Built-in HTTP caching** — in-memory LRU cache with `Vary` support, conditional requests (ETag, Last-Modified), and freshness evaluation per RFC 9111
- **Cookie management** — automatic cookie storage and injection; domain/path matching, `Secure`/`HttpOnly`/`SameSite`, `Max-Age`/`Expires` per RFC 6265
- **Redirect following** — 301/302/303/307/308 with correct method rewriting, body preservation, loop detection, and cross-origin safety per RFC 9110
- **Content encoding** — unified request compression and response decompression (gzip, deflate, Brotli) in a single `ContentEncodingBidiStage`
- **Connection pooling** — per-host pools with idle eviction, automatic reconnect with exponential backoff, and per-host concurrency limits
- **HTTP/2 multiplexing** — multiple requests over a single TCP connection with HPACK header compression and flow control
- **Akka.Streams pipeline** — backpressure-aware, reactive processing with zero actor hops on the data path
- **Zero-allocation internals** — `Span<T>`, `IBufferWriter<byte>`, and `System.Threading.Channels` throughout
- **3,600+ tests** — RFC compliance tests, stream stage tests, integration tests, and benchmarks

---

## Installation

```bash
dotnet add package TurboHttp
```

Requires **.NET 10.0** or later.

---

## Quick Start

```csharp
using TurboHttp.Client;
using System.Net.Http;

// Create and configure the client
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
    options.DefaultRequestVersion = HttpVersion.Version20;
});

// Send a request
var request = new HttpRequestMessage(HttpMethod.Get, "/users");
var response = await client.SendAsync(request);

Console.WriteLine($"Status: {response.StatusCode}");
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine(body);
```

### Channel-based API

For high-throughput scenarios, use the channel-based API directly:

```csharp
await using var client = TurboHttpClientFactory.Create(options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
});

// Write requests
await client.RequestWriter.WriteAsync(new HttpRequestMessage(HttpMethod.Get, "/ping"));

// Read responses
var response = await client.ResponseReader.ReadAsync();
Console.WriteLine($"Status: {response.StatusCode}");
```

---

## Architecture

TurboHttp uses a layered design where **actors manage lifecycle** and **channels carry data** — no bytes ever touch an actor mailbox:

```
Client Layer       ITurboHttpClient (SendAsync / channel API)
      ↓
Handlers Layer     TurboHandler — delegating handler bridge
      ↓
Streams Layer      Akka.Streams GraphStages — Engine, Feature BidiStages, Protocol Engines
                   Features: Cache, Cookies, Redirect, Retry, ContentEncoding, Expect-Continue
      ↓
Protocol Layer     Encoders/Decoders, HPACK/QPACK, frame types
                   RFC 1945 · RFC 9112 · RFC 9113 · RFC 7541 · RFC 9114 · RFC 9204
      ↓
Pooling Layer      PoolRouter → HostPool → ConnectionActor (lifecycle only)
      ↓
Transport Layer    ConnectionStage ←→ Channel<byte> ←→ ClientByteMover ←→ TCP/QUIC
```

For interactive architecture diagrams, see the [documentation site](https://st0o0.github.io/TurboHttp/).

---

## Documentation

Full documentation — including feature guides, architecture overview, API reference, and a comparison with other HTTP clients — is available at **[https://st0o0.github.io/TurboHttp/](https://st0o0.github.io/TurboHttp/)**.

---

## RFC Compliance

| Standard | RFC | Coverage |
|----------|-----|----------|
| HTTP/1.0 | RFC 1945 | Encoder, decoder, connection handling |
| HTTP/1.1 | RFC 9112 | Chunked transfer, keep-alive, pipelining |
| HTTP/2 | RFC 9113 | Frames, streams, flow control, multiplexing |
| HPACK | RFC 7541 | Dynamic table, Huffman coding, sensitive headers |
| HTTP/3 | RFC 9114 | Frames, streams, control streams, GOAWAY |
| QPACK | RFC 9204 | Header compression for HTTP/3 |
| HTTP Semantics | RFC 9110 | Redirects, retries, content negotiation |
| Caching | RFC 9111 | Freshness, validation, `Vary`, conditional requests |
| Cookies | RFC 6265 | Domain/path matching, Secure/HttpOnly/SameSite |
| QUIC | RFC 9000 | Variable-length integer encoding |

---

## Building from Source

```bash
# Restore and build
dotnet restore ./src/TurboHttp.sln
dotnet build --configuration Release ./src/TurboHttp.sln

# Run all tests
dotnet test ./src/TurboHttp.sln

# Run benchmarks
dotnet run --configuration Release --project ./src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj
```

Requires **.NET 10.0 SDK** or later.

---

## Roadmap

- **HTTP/3 production routing** — HTTP/3 stages are implemented and individually tested; routing through the main engine pipeline is blocked until the full HTTP/3 path is hardened
- **NuGet package publishing** — packaging and distribution setup
- **Server-side implementation** — currently client-only

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) for branch naming conventions, PR requirements, how to run tests locally, and recommended branch protection settings.

---

## License

TurboHttp is licensed under the [MIT License](LICENSE).
