# CLAUDE.md

High-performance HTTP client for .NET built on Akka.Streams. Implements HTTP/1.0, 1.1, 2, 3 (QUIC) with full RFC compliance.

## Build & Test

All commands run from `src/` (where `global.json` lives). Restore/build use full paths from repo root.

```bash
dotnet restore ./src/TurboHTTP.sln
dotnet build --configuration Release ./src/TurboHTTP.sln

# Tests (xUnit v3 direct runner)
dotnet test --project TurboHTTP.Tests/TurboHTTP.Tests.csproj              # unit
dotnet test --project TurboHTTP.StreamTests/TurboHTTP.StreamTests.csproj  # stage
dotnet test --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj  # integration (network)

# Single class (preferred for integration — full suite is slow)
dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -class "TurboHTTP.IntegrationTests.H2.ConnectionSpec"

# Single method / namespace
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -namespace "TurboHTTP.Tests.RFC9113"
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.RFC9113.Http2DecoderErrorCodeTests"

# Benchmarks
dotnet run --configuration Release --project TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj

# Docs site (Node.js 20+)
cd docs && npm install && npm run docs:dev
```

## Architecture

```
Client Surface  (TurboHTTP/)                — ITurboHttpClient, factory, builder, DI
Streams Layer   (TurboHTTP/Streams/)        — Engines (Http10/11/20/30Engine), Stages/{Encoding,Decoding,Features,Routing}
Protocol Layer  (TurboHTTP/Protocol/)       — Http10/, Http11/, Http2/, Http3/, Cookies/, Caching/, Semantics/, AltSvc/
Transport Layer (TurboHTTP/Transport/)      — Connection/, Tcp/, Quic/
```

## Obsidian Vault (`notes/`)

Single source of truth for all non-code knowledge. **Use Obsidian MCP tools** (`search_notes`, `read_note`, `write_note`, `patch_note`) — never `Read`/`Write`/`Edit` on `notes/` files.

- Before RFC work → `search_notes("RFC XXXX")`
- Before architecture decisions → `search_notes("component name")`
- Before ending a session → write discoveries via `write_note` or `patch_note`

Key vault guides: `Architecture/Guides/10-TEST_CONVENTIONS`, `11-STAGE_PORT_NAMING`, `12-OBSIDIAN_WORKFLOW`

## Workflow Rules

- **Do NOT commit** unless the user explicitly asks
- **Always respond in English** regardless of input language
- **Write discoveries to Obsidian** after every session

## Code Style

- **Threading model**: Akka actor-thread confinement eliminates most cross-thread concerns.
  Fields in actor-owned types (StateMachines, Leases, Handles) don't need `volatile`/`Interlocked`
  — Akka message passing provides happens-before. Only add barriers at true system boundaries.
- **No `volatile` keyword** — prefer `CancellationToken` for cross-thread signaling, or
  plain fields when actor confinement guarantees single-thread access
- No decorative separator comments (`// ───`, `// ===`, `// ---` section dividers)
- Allman braces, 4 spaces, `_fieldName` for private fields
- `var` when type is apparent, `sealed` by default
- No `#nullable enable` (project-level), no `async void` / `.Result` / `.Wait()`
- Always pass `CancellationToken`, always use braces (even single-line)
- `Task<T>` not Future, `TimeSpan` not Duration
- Extend-only public APIs, preserve wire format compatibility
- Include unit tests with all changes

## Test Conventions (Quick Reference)

New tests use **component-based folders** (`Http10/`, `Http11/`, `Http2/`, etc.) not RFC folders. Key rules:
- `Spec` suffix, `sealed` class, BDD method names: `Subject_should_behavior()`
- `[Trait("RFC", "RFC9113-4.1")]` for traceability, `[Fact(Timeout = 5000)]` required
- `[Fact(DisplayName = ...)]` is deprecated — method name IS the documentation
- Max 500 lines per test class

Full details: `notes/Architecture/Guides/10-TEST_CONVENTIONS`

## Stage Port Naming (Quick Reference)

Ports follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase). Drop `Stage` suffix, no protocol prefix, globally unique names.

Full details: `notes/Architecture/Guides/11-STAGE_PORT_NAMING`

## Custom Agents (`.claude/agents/`)

| Agent | When to use |
|-------|-------------|
| `akka-stage-builder` | Implement a new Akka.Streams `GraphStage` |
| `build-guardian` | Run full build + tests; produce RFC-breakdown coverage report |
| `namespace-refactorer` | Execute namespace reorganisation tasks |
| `stage-port-validator` | Scan all stages for port naming convention violations |
| `spec-naming-validator` | Validate Spec naming conventions in new component-based test files |

## Agent Guidance: dotnet-skills

Prefer retrieval-led reasoning over pretraining for any .NET work.

- C# / quality: csharp-coding-standards, csharp-concurrency-patterns, csharp-api-design, type-design-performance
- Akka: akka-best-practices, akka-testing-patterns, akka-hosting-actor-patterns
- Testing: testcontainers, snapshot-testing
- Quality gates: slopwatch (after substantial code), crap-analysis (after test changes)
- Specialist agents: dotnet-concurrency-specialist, dotnet-performance-analyst, akka-net-specialist

## Roslyn Navigator — Required Before Commit

For any C# modification: inspect affected types and references, verify no downstream breakage, ensure zero compile-time diagnostics.
