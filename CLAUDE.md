# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TurboHttp is a high-performance HTTP client library for .NET built on Akka.Streams. It implements HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with full RFC compliance, including connection pooling, redirect handling, retry logic, and cookie management.

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

### Documentation Site (requires Node.js 20+)

```bash
# Install dependencies
cd docs && npm install

# Start local dev server (http://localhost:5173/TurboHttp/)
npm run docs:dev

# Build static site (output: docs/.vitepress/dist/)
npm run docs:build

# Preview production build locally
npm run docs:preview

# Regenerate LikeC4 SVG exports
npx likec4 export svg --output docs/public/diagrams docs/likec4
```

## Architecture

### Layered Design

```
Client Layer (TurboHttp/Client/)
    ITurboHttpClient, ITurboHttpClientFactory — DI-friendly factory pattern
    TurboHttpClientBuilder — fluent handler pipeline configuration
         ↓
Handlers Layer (TurboHttp/Handlers/)
    TurboHandler — delegating handler bridge to Akka pipeline
         ↓
Hosting Layer (TurboHttp/Hosting/)
    TurboClientServiceCollectionExtensions — DI registration
         ↓
Streams Layer (TurboHttp/Streams/)
    Akka.Streams GraphStages — Engine, Protocol Engines, Feature BidiStages
    Stages organised into: Encoding/, Decoding/, Features/, Routing/
         ↓
Protocol Layer (TurboHttp/Protocol/)
    Encoders/Decoders, HPACK/QPACK, RedirectHandler, RetryEvaluator, CookieJar
    RFC subfolders: RFC1945/, RFC6265/, RFC7541/, RFC9000/, RFC9110/, RFC9111/, RFC9112/, RFC9113/, RFC9114/, RFC9204/
         ↓
Pooling Layer (TurboHttp/Pooling/) — actors for connection lifecycle
    PoolRouter → HostPool → ConnectionActorBase (Http1/Http2/Http3ConnectionActor)
    ConnectionHandle, ConnectionState — zero data touches actor mailboxes
         ↓
Transport Layer (TurboHttp/Transport/) — data path, zero actor hops
    ConnectionStage ←→ Channel<byte> ←→ ClientByteMover ←→ TCP/QUIC
    ClientRunner, ClientManager, ClientState, QuicClientProvider
         ↓
Network (TCP / QUIC)
```

### Protocol Layer (`TurboHttp/Protocol/`)

**Encoders** — Serialise `HttpRequestMessage` to bytes:
- `Http10Encoder.Encode()`, `Http11Encoder.Encode()`, `Http2RequestEncoder.Encode()`
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

**HTTP/3 Frame Types** (`RFC9114/Http3Frame.cs`):
- Variable-length frame header using QUIC variable-length integers
- `Http3FrameEncoder`/`Http3FrameDecoder` — frame serialisation/parsing
- `Http3RequestEncoder`/`Http3ResponseDecoder` — request/response handling
- `Http3Settings`, `Http3ErrorCode`, `Http3StreamType` — protocol primitives
- `Http3ControlStream`, `Http3RequestStream`, `Http3UniStream` — stream types
- `Http3GoAwayHandler`, `Http3IdleTimeoutHandler`, `Http3MaxPushIdHandler` — connection management
- `Http3FieldValidator`, `Http3OriginValidator`, `Http3CertificateValidator` — validation

**QPACK (RFC 9204)**:
- `QpackDecoder`/`QpackDecoderInstructionWriter` — header compression for HTTP/3

**QUIC (RFC 9000)**:
- `QuicVarInt` — QUIC variable-length integer encoding/decoding

**Business Logic** (RFC 9110 / RFC 9112):
- `RedirectHandler` — RFC 9110 §15.4: 301/302/303/307/308 with correct method rewriting, HTTPS→HTTP protection, loop detection
- `RetryEvaluator` — RFC 9110 §9.2: idempotency-based retry, Retry-After parsing
- `ConnectionReuseEvaluator` — RFC 9112 §9: keep-alive/close decision, HTTP/1.0 opt-in
- `CookieJar` — RFC 6265: domain/path matching, Secure/HttpOnly/SameSite, Max-Age/Expires
- `ContentEncodingDecoder` — gzip/deflate/brotli decompression
- `PerHostConnectionLimiter` — per-host concurrency limits

**Caching** (RFC 9111):
- `HttpCacheStore` — RFC 9111 §3: thread-safe in-memory LRU cache with Vary support
- `CacheFreshnessEvaluator` — RFC 9111 §4.2: freshness lifetime, current age, s-maxage/max-age/Expires/heuristic
- `CacheValidationRequestBuilder` — RFC 9111 §4.3: conditional requests (If-None-Match, If-Modified-Since), 304 merge
- `CacheControlParser` — RFC 9111 §5.2: parses Cache-Control directives
- `CachePolicy`, `CacheEntry`, `CacheLookupResult` — supporting types

### Streams Layer (`TurboHttp/Streams/`)

**Engines** (`IProtocolEngine`):
- `Engine` — version demultiplexer (Partition → Http*Engine → Merge)
- `Http10Engine`, `Http11Engine`, `Http20Engine`, `Http30Engine` — per-version routing flows
- `PipelineDescriptor` — describes pipeline topology
- `ProtocolCoreGraphBuilder` — constructs the full stream graph

**Stages** are organised into four subfolders under `Streams/Stages/`:

**Encoding/** — serialize requests to wire format:
- `Http10EncoderStage` / `Http11EncoderStage` / `Http20EncoderStage` / `Http30EncoderStage`
- `Request2FrameStage` (HTTP/2), `Http30Request2FrameStage` (HTTP/3)
- `PrependPrefaceStage` — HTTP/2 connection preface
- `QpackEncoderStreamStage` — QPACK encoder instructions (HTTP/3)

**Decoding/** — parse wire format to responses:
- `Http10DecoderStage` / `Http11DecoderStage` / `Http20DecoderStage` / `Http30DecoderStage`
- `Http20ConnectionStage` / `Http30ConnectionStage` — connection-level frame handling (SETTINGS/PING/GOAWAY)
- `Http20StreamStage` / `Http30StreamStage` — assemble frames into `HttpResponseMessage`
- `QpackDecoderStreamStage` — QPACK decoder instructions (HTTP/3)

**Features/** — cross-cutting BidiStages:
- `RedirectBidiStage` — RFC 9110 §15.4 redirect following
- `RetryBidiStage` — RFC 9110 §9.2 idempotent retry
- `CookieBidiStage` — RFC 6265 cookie injection/storage
- `CacheBidiStage` — RFC 9111 cache lookup/storage
- `DecompressionBidiStage` — gzip/deflate/brotli response decompression
- `RequestCompressionBidiStage` — request body compression
- `ExpectContinueBidiStage` — 100-continue handling
- `ConnectionReuseStage` — keep-alive/close decisions
- `HandlerBidiStage` — delegating handler bridge

**Routing/** — request routing and correlation:
- `RequestEnricherStage` — applies BaseAddress, DefaultRequestVersion, DefaultRequestHeaders
- `ExtractOptionsStage` — splits transport options from request
- `Http1XCorrelationStage` — FIFO request-response matching (HTTP/1.x)
- `Http20CorrelationStage` — stream-ID-based request-response matching (HTTP/2)
- `StreamIdAllocatorStage` — allocates client stream IDs (1, 3, 5, …)
- `GroupByHostKeyStage` / `HostKeyMergeBack` / `MergeSubstreamsStage` — per-host sub-stream routing

### Pooling Layer (`TurboHttp/Pooling/`)

**Actor hierarchy (lifecycle only)** — actors manage connection lifecycle; no data touches actor mailboxes:
- `PoolRouter` — routes to per-host actors
- `HostPool` — pools connections per host, enforces limits, handles reconnect/idle eviction
- `ConnectionActorBase` → `Http1ConnectionActor`, `Http2ConnectionActor`, `Http3ConnectionActor` — per-protocol connection lifecycle
- `ConnectionHandle` — record bundling writer/reader, passed from actors to `ConnectionStage`
- `ConnectionState` — per-connection metadata (Active, Idle, Reusable, HttpVersion, stream capacity)

### Transport Layer (`TurboHttp/Transport/`)

**Data path (zero actor hops)** — data flows through `System.Threading.Channels`:
- `ConnectionStage` — Akka `GraphStage` that writes/reads directly via `ConnectionHandle`
- `ClientByteMover` — async tasks per connection: TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP
- `ClientRunner` — per-connection actor that spawns `ClientByteMover` tasks and signals lifecycle events
- `ClientManager` — spawns `ClientRunner` instances
- `ClientState` — holds TCP stream, `System.IO.Pipelines.Pipe`, and channel reader/writers
- `QuicClientProvider` / `QuicOptions` / `TcpOptionsFactory` — transport-specific configuration

### Client Layer (`TurboHttp/Client/`)

- `ITurboHttpClient` — channel-based API (`ChannelWriter<HttpRequestMessage>` / `ChannelReader<HttpResponseMessage>`), `SendAsync`, `BaseAddress`, `DefaultRequestVersion`
- `ITurboHttpClientFactory` / `TurboHttpClientFactory` — DI-friendly factory pattern for named/typed clients
- `TurboHttpClientFactoryExtensions` — extension methods for factory registration
- `TurboClientOptions` — per-client configuration (timeouts, redirects, retries)
- `TurboClientStreamManager` — manages Akka stream lifecycle per client

### Handlers Layer (`TurboHttp/Handlers/`)

- `ITurboHttpClientBuilder` / `TurboHttpClientBuilder` — fluent API for composing handler pipeline
- `TurboHandler` — delegating handler bridge to Akka stream pipeline
- `TurboClientDescriptor` — describes a configured client instance

### Hosting Layer (`TurboHttp/Hosting/`)

- `TurboClientServiceCollectionExtensions` — `AddTurboHttpClient()` DI registration

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

### Stage Inlet/Outlet Naming

All `GraphStage` inlet/outlet string names follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase). C# field names mirror the same pattern.

**String name pattern:**

| Shape Type | Inlet pattern | Outlet pattern | Example |
|-----------|--------------|----------------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| FanOutShape (1 in, 2+ out) | `StageName.In` | `StageName.Out.Role` | `"Redirect.In"` / `"Redirect.Out.Final"` / `"Redirect.Out.Redirect"` |
| FanInShape (2+ in, 1 out) | `StageName.In.Role` | `StageName.Out` | `"Http20Correlation.In.Request"` / `"Http20Correlation.In.Response"` / `"Http20Correlation.Out"` |
| Custom multi-port | `StageName.In.Role` | `StageName.Out.Role` | `"Http20Connection.In.Server"` / `"Http20Connection.Out.Stream"` |

**C# field name pattern:**

| Shape Type | Inlet fields | Outlet fields |
|-----------|-------------|--------------|
| FlowShape (1 in, 1 out) | `_in` | `_out` |
| FanOutShape (1 in, 2+ out) | `_in` | `_outRole` (e.g., `_outFinal`, `_outSignal`) |
| FanInShape (2+ in, 1 out) | `_inRole` (e.g., `_inRequest`, `_inResponse`) | `_out` |
| Custom multi-port | `_inRole` | `_outRole` |

**Rules:**
- PascalCase throughout — matches C# idiom
- No protocol prefix — stage class name already contains it (e.g., `Http11Encoder` not `Http.Http11Encoder`)
- Drop `Stage` suffix in the string name (e.g., `Http11Encoder` not `Http11EncoderStage`)
- Role names are semantic: `Request`, `Response`, `Final`, `Retry`, `Redirect`, `Signal`, `Miss`, `Hit`, `Server`, `Stream`, `App`
- Every stage must have globally unique port names — no two stages may share the same string

## Workflow Rules

- **Do NOT commit** — Claude must never run `git commit` or `git add` unless the user explicitly asks for it. All commits are done manually by the developer.

## Code Style and Conventions

### C# Style
- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`
- Use `var` when type is apparent
- Default to `sealed` classes and records
- Do NOT add `#nullable enable` — nullable is enabled at project level in the csproj; file-level directives are unnecessary
- Never use `async void`, `.Result`, or `.Wait()`
- Always pass `CancellationToken` through async call chains
- Always use braces for control structures (even single-line)
- `TreatWarningsAsErrors` is enabled globally in `Directory.Build.props` — all warnings are build errors

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

> See [`RFC_COVERAGE.md`](RFC_COVERAGE.md) for the full RFC compliance matrix, gap table, and per-file mapping.

Tests live in `src/TurboHttp.Tests/` organised by RFC:

| Folder | RFC | Files | Unit Tests |
|--------|-----|-------|------------|
| `RFC1945/` (01–17) | HTTP/1.0 | 17 | 233 |
| `RFC9112/` (01–26) | HTTP/1.1 | 26 | 374 |
| `RFC9113/` (01–27) | HTTP/2 | 27 | 545 |
| `RFC7541/` (01–07) | HPACK | 7 | 419 |
| `RFC9110/` (01–02) | HTTP Semantics | 2 | 123 |
| `RFC9111/` (01–04) | Caching | 4 | 75 |
| `RFC6265/` (01–02) | Cookies | 2 | 66 |
| `RFC9114/` (01–32) | HTTP/3 | 32 | — |
| `RFC9204/` (01–11) | QPACK | 11 | — |
| `Hosting/` | Client Builder | 4 | — |

Stream tests: `src/TurboHttp.StreamTests/` — Akka graph construction and stage behaviour. Organised by RFC (mirroring `TurboHttp.Tests`):
- `RFC1945/` — HTTP/1.0 encoder/decoder/roundtrip stages, TCP fragmentation
- `RFC6265/` — Cookie injection and storage stage tests
- `RFC7541/` — HPACK stream integration tests
- `RFC9110/` — Decompression, redirect, retry stage tests
- `RFC9111/` — Cache lookup and storage stage tests
- `RFC9112/` — HTTP/1.1 encoder/decoder/chunked/correlation/pipeline/connection stages
- `RFC9113/` — HTTP/2 encoder/decoder/connection/stream/HPACK/pseudo-header/flow-control/correlation stages
- `RFC9114/` — HTTP/3 encoder/decoder/connection/stream/field-validation/origin-validation/certificate/idle-timeout stages
- `RFC9204/` — QPACK stream stage tests
- `Streams/` — stage infrastructure: connection, engine routing, enricher, buffer lifecycle, pipeline wiring
- `IO/` — ConnectionActor, HostPool, ConnectionState, ConnectionHandle, ClientByteMover, ClientRunner, QUIC tests
- File naming: RFC subfolder files use descriptive names (`Http11EncoderStageTests.cs`); `Streams/` uses `NN_` prefix for ordered tests
- Base classes: `StreamTestBase` (extends `TestKit`, creates `IMaterializer`), `EngineTestBase` (full engine round-trip helper), `IOActorTestBase` (actor lifecycle tests)

Integration tests: `src/TurboHttp.IntegrationTests/` — Kestrel fixtures (`KestrelFixture`, `KestrelH2Fixture`, `KestrelH3Fixture`, `KestrelTlsFixture`) with 60+ routes registered. `SmokeTests.cs` exists as initial end-to-end coverage.

## RFC Compliance

- **HTTP/1.0**: RFC 1945
- **HTTP/1.1**: RFC 9112 (message framing), RFC 9112 §9 (connection management)
- **HTTP/2**: RFC 9113 (protocol), RFC 7541 (HPACK)
- **HTTP Semantics**: RFC 9110 (redirects, retries, content negotiation)
- **HTTP/3**: RFC 9114 (protocol), RFC 9000 (QUIC variable-length integers)
- **QPACK**: RFC 9204 (header compression for HTTP/3)
- **Cookies**: RFC 6265
- **Caching**: RFC 9111 (freshness, validation, storage)

## Documentation Site

The documentation site lives in `docs/` and is built with [VitePress](https://vitepress.dev/) with interactive [LikeC4](https://likec4.dev/) architecture diagrams. Auto-deployed to GitHub Pages via `.github/workflows/docs.yml` on pushes to `main` that touch `docs/**` or `README.md`.

## Current Limitations

- **Limited integration test coverage**: Kestrel fixtures are defined with 60+ routes; `SmokeTests.cs` provides initial coverage but most routes are not yet exercised.
- **LikeC4 diagrams**: Require Node.js 20+ to render interactively. Regenerate SVGs via `npx likec4 export svg --output docs/public/diagrams docs/likec4`.


## Dependencies

- **Akka.Streams** 1.5.62 — actor-based stream processing
- **Servus.Akka** 0.3.10 — TCP abstraction layer
- **.NET 10.0** — target framework
- **xunit** 2.9.3 — test framework
- **Akka.TestKit.Xunit2** 1.5.62 — stream test helpers

# Custom Agents (`.claude/agents/`)

Six project-specific agents are available via `/agent-name` or the Agent tool:

| Agent | When to use |
|-------|-------------|
| `rfc-test-writer` | Generate a new RFC test file following exact project conventions |
| `akka-stage-builder` | Implement a new Akka.Streams `GraphStage` |
| `build-guardian` | Run full build + tests; produce RFC-breakdown coverage report |
| `namespace-refactorer` | Execute one TASK from `.maggus/plan_010.md` (move files, update namespaces, verify build+tests) |
| `stream-test-writer` | Generate `TurboHttp.StreamTests` files following `StreamTestBase` conventions |
| `stage-port-validator` | Read-only quality gate — scan all stages for port naming convention violations |

# Agent Guidance: dotnet-skills

IMPORTANT: Prefer retrieval-led reasoning over pretraining for any .NET work.
Workflow: skim repo patterns -> consult dotnet-skills by name -> implement smallest-change -> note conflicts.

Routing (invoke by name)
- C# / code quality: csharp-coding-standards, csharp-concurrency-patterns, csharp-api-design, csharp-type-design-performance, serialization, project-structure, package-management, local-tools
- Akka / distributed: akka-best-practices, akka-testing-patterns, akka-hosting-actor-patterns, akka-aspire-configuration, akka-management
- ASP.NET Core / Web (incl. Aspire): aspire-service-defaults, aspire-integration-testing, aspire-configuration, aspire-mailpit-integration, mjml-email-templates
- Data: efcore-patterns, database-performance
- DI / config: microsoft-extensions-dependency-injection, microsoft-extensions-configuration
- Testing: testcontainers, playwright-blazor, snapshot-testing, verify-email-snapshots, playwright-ci-caching

Quality gates (use when applicable)
- slopwatch: after substantial new/refactor/LLM-authored code
- crap-analysis: after tests added/changed in complex code

Specialist agents
- dotnet-concurrency-specialist, dotnet-performance-analyst, dotnet-benchmark-designer, akka-net-specialist, docfx-specialist, roslyn-incremental-generator-specialist

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
