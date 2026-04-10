# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TurboHTTP is a high-performance HTTP client library for .NET built on Akka.Streams. It implements HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with full RFC compliance, including connection pooling, redirect handling, retry logic, and cookie management.

## Build Commands

> **Note:** All commands run from the **repository root** (`d:\GIT\Akka.Streams.Http\`).
> `dotnet test --project` paths are relative to `src/` (where `global.json` with MTP runner lives).
> `dotnet restore`/`build`/`run` use full paths from root.

```bash
# Restore and build
dotnet restore ./src/TurboHTTP.sln
dotnet build --configuration Release ./src/TurboHTTP.sln

# Run all tests (includes integration tests — requires network)
dotnet test --project TurboHTTP.sln

# Run unit tests only
dotnet test --project TurboHTTP.Tests/TurboHTTP.Tests.csproj

# Run specific test class (xUnit v3 direct runner — -class flag)
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.RFC9113.Http2DecoderErrorCodeTests"

# Run specific namespace (xUnit v3 direct runner — -namespace flag)
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.RFC9113"

# Run integration tests (H10/H11/H2/H3/TLS — requires network)
dotnet test --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj

# Run integration tests for one HTTP version (prefer per-class — full suite is slow)
dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -namespace "TurboHTTP.IntegrationTests.H2"

# Run a single integration test class (preferred — integration tests are slow)
dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -class "TurboHTTP.IntegrationTests.H2.ConnectionSpec"

# Run a single integration test method
dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -method "TurboHTTP.IntegrationTests.H2.ConnectionSpec.Concurrent_requests_should_be_multiplexed_over_single_connection"

# Run Akka.Streams stage tests
dotnet test --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj

# Run stage tests for one namespace
dotnet run --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj -- -namespace "TurboHTTP.StreamTests.RFC9113"

# Run benchmarks
dotnet run --configuration Release --project TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj
```

### Documentation Site (requires Node.js 20+)

```bash
cd docs && npm install
npm run docs:dev          # Dev server at http://localhost:5173/TurboHTTP/
npm run docs:build        # Static site output: docs/.vitepress/dist/
npm run docs:preview      # Preview production build
```

## Architecture (High-Level)

```
Client Surface (TurboHTTP/)              — ITurboHttpClient, factory, builder, DI extensions
Streams Layer (TurboHTTP/Streams/)       — Engines, GraphStages: Encoding/, Decoding/, Features/, Routing/
Protocol Layer (TurboHTTP/Protocol/)     — Encoders/Decoders, HPACK/QPACK, business logic (RFC subfolders)
Transport Layer (TurboHTTP/Transport/)   — Connection pool, leases, TCP/QUIC handlers
```

> **Details**: Architecture, protocol internals, transport design, decoder pipeline, known limitations, and project status are documented in the **Obsidian vault** at `notes/Architecture/`. Use the Obsidian MCP to search and read.

## Knowledge Management — Obsidian Vault (MANDATORY)

The project knowledge base lives in `notes/` as an Obsidian vault. **This is the single source of truth for all non-code knowledge.**

### Access Rules

- **ALWAYS use Obsidian MCP tools** (`search_notes`, `read_note`, `write_note`, `patch_note`, etc.) to interact with the vault — NEVER use `Read`/`Write`/`Edit` file tools on `notes/` files
- MCP ensures Obsidian indexes stay consistent and frontmatter is properly handled

### When to READ from Obsidian

- Before working on any RFC-related task → `search_notes("RFC XXXX section Y")`
- Before architecture decisions → `search_notes("component name")`
- When you don't know something about the project → search the vault first
- When investigating bugs → check `notes/Debugging/` and `notes/Architecture/`
- Before implementing features → check `notes/Features/`

### When to WRITE to Obsidian

| Discovery Type | Destination | MCP Action |
|----------------|-------------|------------|
| RFC compliance gaps | `RFC/` | `write_note` with RFC-Note template structure |
| Architecture decisions | `Architecture/` | `write_note` with ADR template structure |
| Protocol limitations | `Architecture/` | `write_note` or `patch_note` |
| Bug investigations | `Debugging/` | `write_note` with Bug-Investigation structure |
| Feature learnings | `Features/` | `write_note` |
| Benchmark findings | `Architecture/` | `patch_note` on existing benchmark note |

**Before ending any session**: Check — did I discover something important? If yes → `write_note` or `patch_note` in Obsidian.

### Vault Structure

```
notes/
├── 00-Index.md            # Central hub — START HERE
├── Architecture/          # ADRs, design decisions, patterns, preferences, limitations
├── RFC/                   # Per-RFC compliance tracking (with sections/ subfolders)
├── rfc/                   # RFC reference documents (quick refs, analysis)
├── Features/              # Feature plans and progress
├── Templates/             # Session-Log, RFC-Note, ADR, Bug-Investigation
└── Debugging/             # (git-ignored) Bug investigations
```

### Key Notes Reference

- `Architecture/Design/01-LAYERED_ARCHITECTURE` — Full layer-by-layer architecture
- `Architecture/Design/02-STAGE_PATTERNS` — GraphStage patterns and conventions
- `Architecture/Status/04-CURRENT_STATE_SUMMARY` — Project status, completeness scores
- `Architecture/Guides/05-BENCHMARK_PATTERNS` — BDN conventions, port assignments, TCP workarounds
- `Architecture/Design/06-DECODER_PIPELINE_ARCHITECTURE` — Three-layer decoder pattern
- `Architecture/Guides/09-CLAUDE_PREFERENCES` — Language, workflow, response style preferences

## Stage Inlet/Outlet Naming

All `GraphStage` inlet/outlet string names follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase). C# field names mirror the same pattern.

| Shape Type | Inlet pattern | Outlet pattern | Example |
|-----------|--------------|----------------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| FanOutShape (1 in, 2+ out) | `StageName.In` | `StageName.Out.Role` | `"Redirect.In"` / `"Redirect.Out.Final"` |
| FanInShape (2+ in, 1 out) | `StageName.In.Role` | `StageName.Out` | `"Http20Correlation.In.Request"` / `"Http20Correlation.Out"` |
| Custom multi-port | `StageName.In.Role` | `StageName.Out.Role` | `"Http20Connection.In.Server"` / `"Http20Connection.Out.Stream"` |

**C# fields**: `_in`/`_out` for simple, `_inRole`/`_outRole` for multi-port.

**Rules**: PascalCase, no protocol prefix, drop `Stage` suffix, semantic role names (`Request`, `Response`, `Final`, `Retry`, `Redirect`, `Signal`, `Miss`, `Hit`, `Server`, `Stream`, `App`), globally unique port names.

## Workflow Rules

- **Do NOT commit** — Claude must never run `git commit` or `git add` unless the user explicitly asks for it
- **Always respond in English** — regardless of input language
- **Knowledge capture** — Write important findings to Obsidian vault via MCP after every session

## Code Style and Conventions

### C# Style
- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`
- Use `var` when type is apparent
- Default to `sealed` classes and records
- Do NOT add `#nullable enable` — enabled at project level in csproj
- Never use `async void`, `.Result`, or `.Wait()`
- Always pass `CancellationToken` through async call chains
- Always use braces for control structures (even single-line)

### API Design
- `Task<T>` instead of Future, `TimeSpan` instead of Duration
- Extend-only — do not modify existing public APIs
- Preserve wire format compatibility for serialisation
- Include unit tests with all changes

### Test Conventions

#### New Structure (Component-Based, Post-Feature-040)

Starting with Feature 040, test files are organized by **component/protocol version**, not RFC number:

| Project | Structure |
|---------|-----------|
| `TurboHTTP.Tests/` | `Http10/`, `Http11/`, `Http2/`, `Http3/`, `Semantics/`, `Caching/`, `Cookies/`, `Transport/`, `Security/`, `Diagnostics/`, `Hosting/` |
| `TurboHTTP.StreamTests/` | `Http10/`, `Http11/`, `Http2/`, `Http3/`, `Semantics/`, `Caching/`, `Cookies/`, `Transport/`, `Dispatchers/`, `Streams/` |
| `TurboHTTP.IntegrationTests/` | Unchanged: `H10/`, `H11/`, `H2/`, `H3/`, `TLS/` |

#### RFC → Component Mapping

| RFC | Component | Folder | Example |
|-----|-----------|--------|---------|
| RFC 1945 | HTTP/1.0 | `Http10/` | `Http10EncoderSpec.cs` |
| RFC 9112 | HTTP/1.1 | `Http11/` (with `Encoding/`, `Decoding/`, `Chunking/` subfolders) | `Http11ChunkedDecoderSpec.cs` |
| RFC 9113 | HTTP/2 Frames & Streams | `Http2/Frames/`, `Http2/Connection/`, `Http2/Stream/` | `Http2FrameDecoderSpec.cs` |
| RFC 7541 | HPACK | `Http2/Hpack/` | `HpackEncodingSpec.cs` |
| RFC 9114 | HTTP/3 (QUIC) | `Http3/` (with `Frames/`, `Connection/`, `Qpack/` subfolders) | `Http3ConnectionSpec.cs` |
| RFC 9204 | QPACK | `Http3/Qpack/` | `QpackEncodingSpec.cs` |
| RFC 9110 | HTTP Semantics | `Semantics/` | `RedirectHandlingSpec.cs`, `RetryPolicySpec.cs` |
| RFC 9111 | HTTP Caching | `Caching/` | `CacheValidationSpec.cs` |
| RFC 6265 | HTTP State Management (Cookies) | `Cookies/` | `CookieInjectionSpec.cs` |

#### File & Class Naming Rules

**Old convention** (RFC-based, now deprecated):
```csharp
// File: RFC9113/01_Http2EncoderStageTests.cs
// Class: Http2EncoderStageTests
// Method: [Fact(DisplayName = "RFC9113-4.1-FRM-005: description")]
public async Task Should_SetKeyFromFrame() { }
```

**New convention** (component-based, post-Feature-040):
```csharp
// File: Http2/Encoding/Http2EncoderSpec.cs
// Namespace: TurboHTTP.StreamTests.Http2.Encoding
// Class: Http2EncoderSpec : StreamTestBase
// Method: [Trait("RFC", "RFC9113-4.1")]
public sealed class Http2EncoderSpec : StreamTestBase
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public async Task Http2Encoder_should_set_key_from_frame()
    {
        // BDD-style method name replaces DisplayName
    }
}
```

#### Naming Conventions (Post-Feature-040)

- **File names**: Drop numeric prefix `NN_`, use `Spec` suffix (Akka.NET convention)
  - `Http2EncoderSpec.cs`, `HpackEncodingSpec.cs`, `CacheValidationSpec.cs`
- **Class names**: `Spec` suffix, `sealed`
  - `public sealed class Http2EncoderSpec : StreamTestBase`
- **Method names**: BDD style `Subject_should_behavior()` or `Subject_must_behavior_when_condition()`
  - `Http2Encoder_should_set_key_from_frame()`
  - `Cache_must_reject_expired_entries_when_max_age_exceeded()`
- **Namespaces**: Component-based, matching folder structure
  - `TurboHTTP.Tests.Http2.Encoding`, `TurboHTTP.Tests.Caching`, `TurboHTTP.Tests.Cookies`
- **RFC traceability**: Use `[Trait("RFC", "RFC<number>-<section>")]` (replaces `DisplayName` RFC tags)
  - `[Trait("RFC", "RFC9113-4.1")]`, `[Trait("RFC", "RFC7541-6.3")]`, `[Trait("RFC", "RFC6265-4.1")]`
  - CI filter: `dotnet test --filter "Trait~RFC9113"` (tilde = contains)
- **`[Fact(DisplayName = ...)]` is deprecated** — method name IS the documentation
- **Timeouts REQUIRED**: `[Fact(Timeout = 5000)]` on all async tests or `CancellationToken` with timeout
- **`[Fact]` vs `[Theory]`**: unchanged
  - `[Fact]` for single cases
  - `[Theory]` + `[InlineData]` for parameterised cases
- Do NOT add `#nullable enable` at the top of test files
- **Max 500 lines per test class** — split into multiple files if exceeded

#### Migration Priority (Strangler Fig Strategy)

The RFC-based folders are being replaced incrementally. Migration order:

1. **Cookies (RFC 6265)** → `Cookies/` — 2-3 files (quick win)
2. **Caching (RFC 9111)** → `Caching/` — 6-8 files (quick win)
3. **Semantics (RFC 9110)** → `Semantics/` — ~17 files (opportunistic)
4. **Http10 (RFC 1945)** → `Http10/` — ~28 files (opportunistic)
5. **Http11 (RFC 9112)** → `Http11/` — ~44 files (opportunistic)
6. **Http2 + HPACK (RFC 9113 + RFC 7541)** → `Http2/` — ~36 files (Feature 40-62 Http2Decoder migration)
7. **Http3 + QPACK (RFC 9114 + RFC 9204)** → `Http3/` — ~60 files (opportunistic)

**No big-bang sprint:** New tests land directly in the new structure; old tests migrate as they are touched.

#### Guard-Rail: spec-naming-validator

The `spec-naming-validator` agent validates naming conventions in new component-based test files:
- Checks `Spec.cs` file names, `sealed` classes, BDD method names, `[Trait("RFC", ...)]` usage
- Does NOT block build/tests — it is a quality gate for new code
- Run after adding new test files: `spec-naming-validator` (`.claude/agents/spec-naming-validator`)

## Dependencies

- **Akka.Streams** 1.5.64, **Servus.Akka** 0.3.10, **.NET 10.0**, **xunit.v3.mtp-v2** 3.2.2, **Akka.TestKit.Xunit** 1.5.64, **BenchmarkDotNet** 0.15.8

## Custom Agents (`.claude/agents/`)

| Agent | When to use |
|-------|-------------|
| `akka-stage-builder` | Implement a new Akka.Streams `GraphStage` |
| `build-guardian` | Run full build + tests; produce RFC-breakdown coverage report |
| `namespace-refactorer` | Execute namespace reorganisation tasks |
| `stage-port-validator` | Scan all stages for port naming convention violations |
| `spec-naming-validator` | Validate Spec naming conventions (file, class, method, RFC traits) in new component-based test files |

## Agent Guidance: dotnet-skills

Prefer retrieval-led reasoning over pretraining for any .NET work.

- C# / code quality: csharp-coding-standards, csharp-concurrency-patterns, csharp-api-design, csharp-type-design-performance, serialization, project-structure, package-management
- Akka / distributed: akka-best-practices, akka-testing-patterns, akka-hosting-actor-patterns, akka-aspire-configuration, akka-management
- Testing: testcontainers, playwright-blazor, snapshot-testing
- Quality gates: slopwatch (after substantial code), crap-analysis (after test changes)
- Specialist agents: dotnet-concurrency-specialist, dotnet-performance-analyst, akka-net-specialist

## Roslyn Navigator (Code Navigation)

MCP-based semantic analyzer. Key tools: `find_symbol`, `find_references`, `find_callers`, `get_dependency_graph`, `get_symbol_detail`, `get_type_hierarchy`, `get_public_api`, `find_implementations`, `get_project_graph`.

### Required Before Commit

For any C# modification:

1. Inspect affected types and their references
2. Verify no downstream breakage
3. Check diagnostics — ensure zero compile-time errors
