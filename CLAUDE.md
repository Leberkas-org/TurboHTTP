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
    ITurboHttpClient — channel-based request/response API
         ↓
Streams Layer (TurboHttp/Streams/)
    Akka.Streams GraphStages — Engine, ConnectionStage, Protocol Engines
         ↓
Protocol Layer (TurboHttp/Protocol/)
    Encoders/Decoders, HPACK, RedirectHandler, RetryEvaluator, CookieJar
         ↓
I/O Layer (TurboHttp/IO/) — Hybrid: actors for lifecycle, Channels for data
    ┌─ Lifecycle (actor hierarchy) ──────────────────────────────┐
    │  PoolRouterActor → HostPoolActor → ConnectionActor         │
    │  (spawn, supervise, reconnect, idle eviction, per-host     │
    │   limits — no data touches actor mailboxes)                │
    └────────────────────────────────────────────────────────────┘
    ┌─ Data path (zero actor hops) ──────────────────────────────┐
    │  ConnectionStage ←→ Channel<byte> ←→ ClientByteMover ←→ TCP│
    │  (ConnectionHandle bundles ChannelWriter + ChannelReader)  │
    └────────────────────────────────────────────────────────────┘
         ↓
Network (TCP)
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

- `Engine` — version demultiplexer (Partition → Http*Engine → Merge)
- `Http10Engine`, `Http11Engine`, `Http20Engine` — per-version routing flows (`IHttpProtocolEngine`)
- `ConnectionStage` — TCP connection wrapper (Akka `GraphStage`)
- `RequestEnricherStage` — applies BaseAddress, DefaultRequestVersion, DefaultRequestHeaders
- `ExtractOptionsStage` — splits `HttpRequest(Options, RequestMessage)` into transport + request

**HTTP/1.x Stages**:
- `Http10EncoderStage` / `Http11EncoderStage` — serialize `HttpRequestMessage` to bytes
- `Http10DecoderStage` / `Http11DecoderStage` — parse bytes to `HttpResponseMessage`
- `CorrelationHttp1XStage` — FIFO request-response matching

**HTTP/2 Stages**:
- `StreamIdAllocatorStage` — allocates client stream IDs (1, 3, 5, …)
- `Request2FrameStage` — `(HttpRequestMessage, streamId)` → `Http2Frame` list
- `Http20EncoderStage` — serialise `Http2Frame` to bytes
- `Http20DecoderStage` — parse bytes to `Http2Frame` (stateful buffer)
- `Http20ConnectionStage` — bidirectional flow control, SETTINGS/PING/GOAWAY handling
- `Http20StreamStage` — assemble frames into `HttpResponseMessage` (HPACK decode, decompression)
- `PrependPrefaceStage` — inject HTTP/2 connection preface on first connect
- `CorrelationHttp20Stage` — stream-ID-based request-response matching

### I/O Layer (`TurboHttp/IO/`)

**Hybrid architecture**: actors manage connection lifecycle; data flows through `System.Threading.Channels` with zero actor mailbox hops.

**Actor hierarchy (lifecycle only)**:
- `PoolRouterActor` — routes `EnsureHost` to per-host actors, creates `HostPoolActor` on demand
- `HostPoolActor` — pools connections per host, enforces `PerHostConnectionLimiter`, handles reconnect/idle eviction, delivers `ConnectionHandle` to requesters
- `ConnectionActor` — owns TCP socket lifecycle, creates `Channel<(IMemoryOwner<byte>, int)>` pairs, spawns `ClientRunner`, sends `ConnectionReady(ConnectionHandle)` to parent on connect, exponential backoff on reconnect

**Data path (zero actor hops)**:
- `ConnectionHandle` — record bundling `OutboundWriter`, `InboundReader`, `HostKey`, and `ConnectionActor` ref; passed from actors to `ConnectionStage`
- `ConnectionStage` — Akka `GraphStage` that writes outbound bytes directly to `ConnectionHandle.OutboundWriter` and reads inbound bytes from `ConnectionHandle.InboundReader` via async pump
- `ClientByteMover` — three static async tasks per connection: TCP→Pipe, Pipe→InboundChannel, OutboundChannel→TCP
- `ClientRunner` — per-connection actor that spawns the `ClientByteMover` tasks and signals lifecycle events
- `ClientManager` — actor that spawns `ClientRunner` instances
- `ClientState` — holds TCP stream, `System.IO.Pipelines.Pipe`, and channel reader/writers

**Connection state tracking**:
- `ConnectionState` — per-connection metadata (Active, Idle, Reusable, HttpVersion, stream capacity) tracked by `HostPoolActor`
- `HostKey` — connection identity: Scheme + Host + Port + Version

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

### Stage Inlet/Outlet Naming

All `GraphStage` inlet/outlet string names follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase). C# field names mirror the same pattern.

**String name pattern:**

| Shape Type | Inlet pattern | Outlet pattern | Example |
|-----------|--------------|----------------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| FanOutShape (1 in, 2+ out) | `StageName.In` | `StageName.Out.Role` | `"Redirect.In"` / `"Redirect.Out.Final"` / `"Redirect.Out.Redirect"` |
| FanInShape (2+ in, 1 out) | `StageName.In.Role` | `StageName.Out` | `"H2Correlation.In.Request"` / `"H2Correlation.In.Response"` / `"H2Correlation.Out"` |
| Custom multi-port | `StageName.In.Role` | `StageName.Out.Role` | `"H2Connection.In.Server"` / `"H2Connection.Out.Stream"` |

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

Stream tests: `src/TurboHttp.StreamTests/` — Akka graph construction and stage behaviour. Organised by RFC (mirroring `TurboHttp.Tests`):
- `RFC1945/` — HTTP/1.0 encoder/decoder/roundtrip stages, TCP fragmentation
- `RFC6265/` — Cookie injection and storage stage tests
- `RFC7541/` — HPACK stream integration tests
- `RFC9110/` — Decompression, redirect, retry stage tests
- `RFC9111/` — Cache lookup and storage stage tests
- `RFC9112/` — HTTP/1.1 encoder/decoder/chunked/correlation/pipeline/connection stages
- `RFC9113/` — HTTP/2 encoder/decoder/connection/stream/HPACK/pseudo-header/flow-control/correlation stages
- `Streams/` — stage infrastructure: connection, engine routing, enricher, buffer lifecycle, pipeline wiring
- `IO/` — ConnectionActor, HostPoolActor, ConnectionState, ConnectionHandle
- File naming: RFC subfolder files use descriptive names (`Http11EncoderStageTests.cs`); `Streams/` uses `NN_` prefix for ordered tests
- Base classes: `StreamTestBase` (extends `TestKit`, creates `IMaterializer`), `EngineTestBase` (full engine round-trip helper)

Integration tests: `src/TurboHttp.IntegrationTests/Shared/` — Kestrel fixtures (`KestrelFixture`, `KestrelH2Fixture`, `KestrelTlsFixture`) with 60+ routes registered. No end-to-end test classes yet — fixtures are infrastructure-only, ready for future integration tests.

## RFC Compliance

- **HTTP/1.0**: RFC 1945
- **HTTP/1.1**: RFC 9112 (message framing), RFC 9112 §9 (connection management)
- **HTTP/2**: RFC 9113 (protocol), RFC 7541 (HPACK)
- **HTTP Semantics**: RFC 9110 (redirects, retries, content negotiation)
- **Cookies**: RFC 6265
- **Caching**: RFC 9111 (freshness, validation, storage)

## Documentation Site

The documentation site lives in `docs/` and is built with [VitePress](https://vitepress.dev/) with interactive [LikeC4](https://likec4.dev/) architecture diagrams.

### Directory Structure

```
docs/
  .vitepress/
    config.ts             — VitePress configuration (nav, sidebar, base path)
    theme/index.ts        — Custom theme extending VitePress default
    components/
      LikeC4Diagram.vue   — Vue wrapper for LikeC4 React components
  likec4/                 — LikeC4 architecture model (.c4 files, 12+ views)
  logo/                   — Project logos (logo.svg, logo.png, logo_small.svg)
  public/
    diagrams/             — Static SVG fallback exports from LikeC4
  guide/                  — Getting started, architecture overview, protocols
  architecture/           — Per-layer and per-scenario architecture pages
  rfc/                    — RFC coverage details (one page per RFC)
  api/                    — Public API overview
  index.md                — VitePress home page (hero, features, CTA)
  package.json            — VitePress + LikeC4 npm dependencies
  vite.config.ts          — LikeC4 Vite plugin configuration
```

### Deployment

The docs site is automatically built and deployed to [https://st0o0.github.io/TurboHttp/](https://st0o0.github.io/TurboHttp/) via `.github/workflows/docs.yml` on every push to `main` that touches `docs/**` or `README.md`. GitHub Pages must be enabled in repository settings with "GitHub Actions" as the source.

### LikeC4 Architecture Views

The `docs/likec4/` workspace contains 12+ named views:
- `index` — System overview
- `turbohttp` — Container view
- `clientLayer`, `streamsLayer`, `ioLayer` — Layer views
- `http10Engine`, `http11Engine`, `http2Engine` — Engine views
- `pipelineFlow` — Full pipeline
- `scenarioHttp10`, `scenarioHttp11`, `scenarioHttp2` — Dynamic scenarios

## Current Limitations

- **No end-to-end integration tests**: Kestrel fixtures are defined with 60+ routes but no test classes consume them yet.
- **LikeC4 diagrams**: Require Node.js 20+ to render interactively. Static SVG fallbacks in `docs/public/diagrams/` are placeholders until regenerated via `npx likec4 export svg`.


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
