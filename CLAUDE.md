# CLAUDE.md

High-performance HTTP client and server for .NET built on Akka.Streams. Implements HTTP/1.0, 1.1, 2, 3 (QUIC) with full RFC compliance.

## Build & Test

All commands run from `src/` (where `global.json` lives).

```bash
dotnet restore TurboHTTP.slnx
dotnet build --configuration Release TurboHTTP.slnx

# Tests (xUnit v3 — use dotnet run, not dotnet test)
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj                          # all unit + stage
dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj    # integration (network)

# Single class (preferred for integration — full suite is slow)
dotnet run --project TurboHTTP.IntegrationTests/TurboHTTP.IntegrationTests.csproj -- -class "TurboHTTP.IntegrationTests.H2.ConnectionSpec"

# Single class / filter
dotnet run --project TurboHTTP.Tests/TurboHTTP.Tests.csproj -- -class "TurboHTTP.Tests.Protocol.Syntax.Http2.Frames.Http2DecoderErrorCodeSpec"

# Integration tests with specific backend (default: auto-detect Docker, fallback Kestrel)
$env:TURBOHTTP_TEST_BACKEND = "kestrel"   # force Kestrel (no Docker needed)
$env:TURBOHTTP_TEST_BACKEND = "docker"    # force Docker (fails if unavailable)

# Benchmarks
dotnet run --configuration Release --project TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj

# Docs site (Node.js 20+)
cd ../docs && npm install && npm run docs:dev
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

### RFC vault structure

`notes/RFC/RFC{number}/RFC{number}.md` (index) + `sections/` subfolder. Each section file has `rfc_section: "X.Y"` in frontmatter. Sub-sections (X.Y.Z) are `###` headings within the parent section file.

## Workflow Rules

- **Do NOT commit** unless the user explicitly asks
- **Always respond in English** regardless of input language

## Code Style

- **Threading model**: Akka actor-thread confinement eliminates most cross-thread concerns.
  Fields in actor-owned types (StateMachines, Leases, Handles) don't need `volatile`/`Interlocked`
  — Akka message passing provides happens-before. Only add barriers at true system boundaries.
- **No `volatile` keyword** — prefer `CancellationToken` for cross-thread signaling, or
  plain fields when actor confinement guarantees single-thread access
- No decorative separator comments (`// ───`, `// ===`, `// ---` section dividers)
- Allman braces, 4 spaces, `_fieldName` for private fields
- `var` when type is apparent, `sealed` by default
- No `#nullable enable` (project-level), no `async void` / `.Result` / `.Wait()` / `.GetAwaiter().GetResult()`
- Always pass `CancellationToken`, always use braces (even single-line)
- `Task<T>` not Future, `TimeSpan` not Duration
- Extend-only public APIs, preserve wire format compatibility
- Include unit tests with all changes
- **Size literals**: Always `N * 1024` or `N * 1024 * 1024`, never raw numbers like `65536` or `2_097_152`

## Performance Patterns

- **Snapshot semantics**: Decoder/FrameDecoder return values are held across calls by tests —
  cannot return reused lists directly. Use `.ToArray()` or `new List<>(buffer)` for public APIs.
  Akka back-pressure guarantees consumption in production, but test contracts require copies.
- **List reuse pattern**: Http2/RequestEncoder has `_reusableHeaders`/`_reusableFrames` —
  follow this pattern for any per-request collection (clear + repopulate, not new).
- **`string.Concat` over `$""`** for simple 2-3 part joins (avoids handler alloc)
- **`Span.IndexOf((byte)x)` over byte-by-byte loops** — delegates to SIMD `memchr`
- **`ArrayPool<T>.Shared`** for temp arrays in reconnect/flush paths (rent, use, return)
- **No `new List<T>` in per-request hot paths** — reuse via field + Clear()

## Test Conventions (Quick Reference)

New tests use **component-based folders** (`Http10/`, `Http11/`, `Http2/`, etc.) not RFC folders. Key rules:

- `Spec` suffix, `sealed` class, BDD method names: `Subject_should_behavior()`
- `[Trait("RFC", "RFC9113-4.1")]` for traceability, `[Fact(Timeout = 5000)]` required
- `[Fact(DisplayName = ...)]` is deprecated — method name IS the documentation
- Max 500 lines per test class

### Test File Naming Convention

**File name = class name, always.** Pattern: `{ProtocolPrefix?}{ProductionClassName}{TestConcern?}Spec`

- Protocol prefix (`Http2`, `Http3`, `Hpack`, `Qpack`) required when test is under `Protocol/Syntax/{HttpVersion}/` or class name is shared across namespaces
- Omit prefix when class is globally unique (`CookieJar`, `CacheStore`) or in protocol-agnostic folders (`Client/`, `Features/`)
- When a SUT has multiple test files, suffix describes **test concern** — never `Part1`/`Part2`/numbered
- Duplicates disambiguated by test focus, not location

### H2/H3 Test Folder Placement

| Folder | What belongs here | Types under test |
|--------|------------------|-----------------|
| **Frames/** | Wire format: serialize, deserialize, parse, validate | `FrameDecoder`, `Http2Frame`/`Http3Frame` subtypes |
| **Client/** | Client behavioral logic (subfolders at 5+ files) | `*ClientStateMachine`, `FlowController`, `*ClientEncoder`, `*ClientDecoder` |
| **Server/** | Server behavioral logic (subfolders at 5+ files) | `*ServerStateMachine`, `*ServerEncoder`, `*ServerDecoder` |
| **Hpack/** / **Qpack/** | Header compression (own RFC) | `HpackEncoder`/`QpackEncoder`, dynamic/static tables |
| **Security/** | Fuzz, adversarial, resource exhaustion | Any, from attacker perspective |
| **Stages/** | Akka Streams integration (GraphDsl) | `*ConnectionStage` |
| **Options/** | Configuration validation stubs | `*Options` types |

**Decision rule**: `FrameDecoder` + frame assertions → Frames/. `*StateMachine`/`*Encoder`/`*Decoder` → Client/ or Server/. Akka Streams graph → Stages/.

Http10/Http11 use flat `Client/` and `Server/` (no subfolders).

## Stage Port Naming (Quick Reference)

Ports follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase). Drop `Stage` suffix, no protocol prefix, globally unique names.

## Custom Agents (`.claude/agents/`)

| Agent             | When to use                                                                                                                                 |
| ----------------- | ------------------------------------------------------------------------------------------------------------------------------------------- |
| `spec-refactorer` | Refactor test specs: remove non-Protocol RFC traits, validate RFC section refs against Obsidian vault, strip `///` comments outside methods |

## Agent Guidance: dotnet-skills

Prefer retrieval-led reasoning over pretraining for any .NET work.

- C# / quality: csharp-coding-standards, csharp-concurrency-patterns, csharp-api-design, type-design-performance
- Akka: akka-best-practices, akka-testing-patterns, akka-hosting-actor-patterns
- Testing: testcontainers, snapshot-testing
- Quality gates: slopwatch (after substantial code), crap-analysis (after test changes)
- Specialist agents: dotnet-concurrency-specialist, dotnet-performance-analyst, akka-net-specialist

## Sequential Thinking MCP (`mcp__sequential-thinking__sequentialthinking`)

Use for multi-step reasoning where the full scope isn't clear upfront.

**When to use:**
- Complex debugging where the root cause isn't obvious
- Architecture/design decisions with multiple trade-offs
- RFC compliance analysis requiring cross-referencing multiple sections

Call repeatedly, one thought per step. Revise or branch freely when new information contradicts earlier steps.

## Roslyn Navigator — Required Before Commit

For any C# modification: inspect affected types and references, verify no downstream breakage, ensure zero compile-time diagnostics.
