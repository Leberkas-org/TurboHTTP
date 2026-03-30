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

# Run specific test class (xUnit v3 MTP filter — note: args after --)
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj -- --filter-class "TurboHttp.Tests.RFC9113.Http2DecoderBasicFrameTests"

# Run specific RFC section (by namespace)
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj -- --filter-namespace "TurboHttp.Tests.RFC9113"

# Run integration tests (H10/H11/H2/H3/TLS — requires network)
dotnet test ./src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj

# Run integration tests for one HTTP version
dotnet test ./src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj -- --filter-namespace "TurboHttp.IntegrationTests.H11"

# Run Akka.Streams stage tests
dotnet test ./src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj

# Run stage tests for one RFC section
dotnet test ./src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj -- --filter-namespace "TurboHttp.StreamTests.RFC9113"

# Run benchmarks
dotnet run --configuration Release ./src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj
```

### Documentation Site (requires Node.js 20+)

```bash
cd docs && npm install
npm run docs:dev          # Dev server at http://localhost:5173/TurboHttp/
npm run docs:build        # Static site output: docs/.vitepress/dist/
npm run docs:preview      # Preview production build
```

## Architecture (High-Level)

```
Client Surface (TurboHttp/)              — ITurboHttpClient, factory, builder, DI extensions
Streams Layer (TurboHttp/Streams/)       — Engines, GraphStages: Encoding/, Decoding/, Features/, Routing/
Protocol Layer (TurboHttp/Protocol/)     — Encoders/Decoders, HPACK/QPACK, business logic (RFC subfolders)
Transport Layer (TurboHttp/Transport/)   — Connection pool, leases, TCP/QUIC handlers
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
- Test classes: `public sealed class`, namespace matches RFC folder (e.g. `namespace TurboHttp.Tests.RFC9113;`)
- File naming: `NN_<ThemaTests>.cs` — two-digit prefix groups tests by RFC section
- Use `[Fact]` for single cases, `[Theory]` + `[InlineData]` for parameterised cases
- Use `DisplayName` attribute for RFC-tagged tests: `"RFC-section-cat-nnn: description"`
- Do NOT add `#nullable enable` at the top of test files
- **Max 500 lines per test class** — split into multiple files if exceeded
- **Timeout is REQUIRED** — all async tests must have `[Fact(Timeout = 5000)]` / `[Theory(Timeout = 5000)]` or `CancellationToken` with timeout in test body

## Dependencies

- **Akka.Streams** 1.5.63, **Servus.Akka** 0.3.10, **.NET 10.0**, **xunit.v3.mtp-v2** 3.2.2, **Akka.TestKit.Xunit** 1.5.63, **BenchmarkDotNet** 0.15.8

## Custom Agents (`.claude/agents/`)

| Agent | When to use |
|-------|-------------|
| `rfc-test-writer` | Generate a new RFC test file following exact project conventions |
| `akka-stage-builder` | Implement a new Akka.Streams `GraphStage` |
| `build-guardian` | Run full build + tests; produce RFC-breakdown coverage report |
| `namespace-refactorer` | Execute namespace reorganisation tasks |
| `stream-test-writer` | Generate `TurboHttp.StreamTests` files following `StreamTestBase` conventions |
| `stage-port-validator` | Scan all stages for port naming convention violations |
| `displayname-validator` | Validate `[Fact]`/`[Theory]` DisplayName attributes follow project naming convention |

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
