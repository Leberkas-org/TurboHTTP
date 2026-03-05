# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TurboHttp is a high-performance HTTP client library for .NET built on Akka.Streams. It implements HTTP/1.0, HTTP/1.1, and HTTP/2 with full RFC compliance, including connection pooling, redirect handling, retry logic, and cookie management.

## Build Commands

```bash
# Restore and build
dotnet restore ./src/TurboHttp.sln
dotnet build --configuration Release ./src/TurboHttp.sln

# Run all tests
dotnet test ./src/TurboHttp.sln

# Run specific test class
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~Http2DecoderBasicFrameTests"

# Run specific RFC section
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~RFC9113"

# Run benchmarks
dotnet run --configuration Release ./src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj
```

## Architecture

### Layered Design

```
Client Layer (TurboHttp/Client/)
    ITurboHttpClient — channel-based request/response API
         ↓
Streams Layer (TurboHttp/Streams/)
    Akka.Streams GraphStages — Engine, HostRoutingFlow, ConnectionStage
         ↓
Protocol Layer (TurboHttp/Protocol/)
    Encoders/Decoders, HPACK, RedirectHandler, RetryEvaluator, CookieJar
         ↓
I/O Layer (TurboHttp/IO/)
    ClientManager + ClientRunner + ClientByteMover (Akka actors + Channels)
         ↓
Network (TCP)
```

### Protocol Layer (`TurboHttp/Protocol/`)

**Encoders** — Serialise `HttpRequestMessage` to bytes:
- `Http10Encoder.Encode()`, `Http11Encoder.Encode()`, `Http2Encoder.Encode()`
- Use `ref Span<byte>` or `ref Memory<byte>` for zero-allocation patterns

**Decoders** — Stateful, handle partial frames across TCP boundaries:
- Maintain `_remainder` for incomplete messages
- `TryDecode()` for normal parsing, `TryDecodeEof()` for connection close
- `Reset()` to clear state between connections

**HPACK (RFC 7541)**:
- `HpackEncoder`/`HpackDecoder` maintain synchronised dynamic tables
- `HpackDynamicTable` — FIFO with 32-byte per-entry overhead
- `HuffmanCodec` — static Huffman encoding/decoding
- Sensitive headers (Authorization, Cookie) use NeverIndex automatically

**HTTP/2 Frame Types** (`Http2Frame.cs`):
- 9-byte header: length(24) + type(8) + flags(8) + stream(31)
- Subclasses: `DataFrame`, `HeadersFrame`, `ContinuationFrame`, `RstStreamFrame`, `SettingsFrame`, `PingFrame`, `GoAwayFrame`, `WindowUpdateFrame`, `PushPromiseFrame`
- `SerializedSize` for buffer pre-allocation, `WriteTo(ref Span<byte>)` for serialisation

**Business Logic** (RFC 9110 / RFC 9112):
- `RedirectHandler` — RFC 9110 §15.4: 301/302/303/307/308 with correct method rewriting, HTTPS→HTTP protection, loop detection
- `RetryEvaluator` — RFC 9110 §9.2: idempotency-based retry, Retry-After parsing
- `ConnectionReuseEvaluator` — RFC 9112 §9: keep-alive/close decision, HTTP/1.0 opt-in
- `CookieJar` — RFC 6265: domain/path matching, Secure/HttpOnly/SameSite, Max-Age/Expires
- `ContentEncodingDecoder` — gzip/deflate/brotli decompression
- `PerHostConnectionLimiter` — per-host concurrency limits

### Streams Layer (`TurboHttp/Streams/`)

- `Engine` — version demultiplexer (Partition → Http*Engine → Merge)
- `Http10Engine`, `Http11Engine`, `Http20Engine` — per-version routing flows
- `HostRoutingFlow` — partitions requests by host, maintains per-host connection pools
- `ConnectionStage` — TCP connection wrapper (Akka `GraphStage`)
- `HostConnectionPool` — manages concurrent connections per host

### I/O Layer (`TurboHttp/IO/`)

- `ClientManager` — Akka actor supervisor for TCP connections
- `ClientRunner` — per-connection lifecycle actor
- `ClientState` — shared state (System.Threading.Channels + buffers)
- `ClientByteMover` — async byte transfer between TCP and channels

### Client Layer (`TurboHttp/Client/`)

- `ITurboHttpClient` — channel-based API (`ChannelWriter<HttpRequestMessage>` / `ChannelReader<HttpResponseMessage>`), `SendAsync`, `BaseAddress`, `DefaultRequestVersion`

## Key Patterns

### Memory Management
- `ReadOnlyMemory<byte>` and `Span<T>` for buffer efficiency
- `IMemoryOwner<byte>` requires proper disposal
- `IBufferWriter<byte>` for zero-copy encoding output

### Error Handling
- `HpackException` — RFC 7541 violations
- `Http2Exception` — HTTP/2 protocol errors
- `HttpDecoderException` — general decode failures
- `HttpDecodeError` enum for error classification
- `RedirectException` — redirect-specific errors

## Code Style and Conventions

### C# Style
- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`
- Use `var` when type is apparent
- Default to `sealed` classes and records
- Do NOT add `#nullable enable` — not used in this codebase
- Never use `async void`, `.Result`, or `.Wait()`
- Always pass `CancellationToken` through async call chains
- Always use braces for control structures (even single-line)

### API Design
- `Task<T>` instead of Future, `TimeSpan` instead of Duration
- Extend-only — do not modify existing public APIs
- Preserve wire format compatibility for serialisation
- Include unit tests with all changes

### Test Conventions
- Test classes: `public sealed class`, namespace matches RFC folder (e.g. `namespace TurboHttp.Tests.RFC9113;`)
- File naming: `NN_<ThemaTests>.cs` — two-digit prefix groups tests by RFC section
- Use `[Fact]` for single cases, `[Theory]` + `[InlineData]` for parameterised cases
- Use `DisplayName` attribute for RFC-tagged tests: `"RFC-section-cat-nnn: description"`
- Do NOT add `#nullable enable` at the top of test files

## Test Organisation

Tests live in `src/TurboHttp.Tests/` organised by RFC:

| Folder | RFC | Coverage |
|--------|-----|----------|
| `RFC1945/` (01–17) | HTTP/1.0 | Encoder, Decoder, RoundTrip |
| `RFC9112/` (01–21 + 3 preserved) | HTTP/1.1 | Encoder, Decoder, RoundTrip |
| `RFC9113/` (01–20 + preserved) | HTTP/2 | Decoder, RoundTrip, Encoder |
| `RFC7541/` (01–06 + HpackTests) | HPACK | Static table, encoding, dynamic table |
| `RFC9110/` (01–03) | Content encoding | gzip, deflate/brotli, integration |
| `Integration/` | Cross-layer | CookieJar, RedirectHandler, RetryEvaluator, ConnectionReuse, TcpFragmentation |

Stream tests: `src/TurboHttp.StreamTests/` — Akka graph construction and pool behaviour.

Integration tests: `src/TurboHttp.IntegrationTests/` (Http10/, Http11/, Http2/, Shared/) — real Kestrel server via `KestrelFixture` and `KestrelH2Fixture`.

## RFC Compliance

- **HTTP/1.0**: RFC 1945
- **HTTP/1.1**: RFC 9112 (message framing), RFC 9112 §9 (connection management)
- **HTTP/2**: RFC 9113 (protocol), RFC 7541 (HPACK)
- **HTTP Semantics**: RFC 9110 (redirects, retries, content negotiation)
- **Cookies**: RFC 6265

## Dependencies

- **Akka.Streams** 1.5.60 — actor-based stream processing
- **Servus.Akka** 0.3.10 — TCP abstraction layer
- **.NET 10.0** — target framework
- **xunit** 2.9.3 — test framework
- **Akka.TestKit.Xunit2** 1.5.62 — stream test helpers

# Agent Guidance: dotnet-skills

IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
Workflow: skim repo patterns -> consult dotnet-skills by name -> implement smallest-change -> note conflicts.

Routing (invoke by name)
- C# / code quality: modern-csharp-coding-standards, csharp-concurrency-patterns, api-design, type-design-performance
- ASP.NET Core / Web (incl. Aspire): aspire-service-defaults, aspire-integration-testing, transactional-emails
- Data: efcore-patterns, database-performance
- DI / config: dependency-injection-patterns, microsoft-extensions-configuration
- Testing: testcontainers-integration-tests, playwright-blazor-testing, snapshot-testing

Quality gates (use when applicable)
- dotnet-slopwatch: after substantial new/refactor/LLM-authored code
- crap-analysis: after tests added/changed in complex code

Specialist agents
- dotnet-concurrency-specialist, dotnet-performance-analyst, dotnet-benchmark-designer, akka-net-specialist, docfx-specialist

# C# Semantic Enforcement (csharp-lsp)

This repository requires semantic analysis for all C# changes.

Plugin:
- csharp-lsp @ claude-plugins-official

### When Mandatory

Activate `csharp-lsp` when:
- Modifying or creating *.cs files
- Changing *.csproj or solution structure
- Refactoring (rename, move, signature change)
- Performing cross-file or cross-namespace changes
- Modifying public APIs or protocol frame types

### Required Before Commit

For any C# modification:

1. Inspect affected types and their references.
2. Verify no downstream breakage.
3. Check diagnostics.
4. Ensure zero compile-time errors remain.

If C# files were modified and semantic validation was not performed,
the iteration is considered incomplete.

Log usage of csharp-lsp in the Flight Recorder.
