---
title: Developer Onboarding Guide
description: >-
  Start here — orients new developers and fresh AI sessions to TurboHTTP
  architecture, workflows, and vault navigation
tags:
  - architecture
  - onboarding
  - guide
  - meta
created: '2026-03-28'
updated: '2026-04-07'
---
# Developer Onboarding Guide

Welcome to TurboHTTP. This note is the single starting point for new developers and fresh AI agent sessions. Read it once, then follow the links to deeper references.

## Project Purpose

TurboHTTP is a high-performance HTTP client library for .NET built on Akka.Streams. It implements HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with full RFC compliance, including:

- Connection pooling and keep-alive management
- Redirect following and retry logic
- Cookie management and cache support
- Response decompression and request compression
- Expect-Continue handshake handling

The library exposes an `ITurboHttpClient` interface compatible with `HttpMessageHandler`, enabling drop-in use with `HttpClient`.

## Tech Stack

| Component | Version | Role |
|-----------|---------|------|
| .NET | 10.0 | Target framework |
| Akka.Streams | 1.5.63 | Stream pipeline engine |
| Servus.Akka | 0.3.10 | Actor hosting utilities |
| xunit.v3 | 3.2.2 | Test framework |

## Repository Layout

```text
src/
├── TurboHTTP/                  # Main library
│   ├── Client/                 # ITurboHttpClient, factory, DI
│   ├── Handlers/               # TurboHandler (HttpMessageHandler bridge)
│   ├── Hosting/                # DI registration extensions
│   ├── Streams/                # GraphStages: Encoding/, Decoding/, Features/, Routing/
│   ├── Protocol/               # Encoders/Decoders, HPACK/QPACK, component subfolders
│   │   ├── Http10/             # HTTP/1.0 (RFC 1945)
│   │   ├── Http11/             # HTTP/1.1 (RFC 9112)
│   │   ├── Http2/              # HTTP/2 + Hpack/ (RFC 9113, RFC 7541)
│   │   ├── Http3/              # HTTP/3 + Qpack/ (RFC 9114, RFC 9204)
│   │   ├── Semantics/          # HTTP semantics: redirect, retry, compression (RFC 9110)
│   │   ├── Caching/            # HTTP caching (RFC 9111)
│   │   └── Cookies/            # Cookie management (RFC 6265)
│   └── Transport/              # Actor-free connection pool, Channels
│       ├── Connection/         # ConnectionPool, ConnectionLease, IConnectionScope, ConnectionStage
│       ├── Tcp/                # TcpTransportHandler, ClientState, ClientByteMover
│       └── Quic/               # QuicTransportHandler, QuicConnectionManager
├── TurboHTTP.Tests/            # Component-organized test suite
├── TurboHTTP.StreamTests/      # Akka.Streams stage tests
├── TurboHTTP.Benchmarks/       # BenchmarkDotNet performance suite
└── TurboHTTP.sln               # Solution file
notes/                          # This vault — single source of truth for non-code knowledge
docs/                           # VitePress documentation site
```

## Build Commands

```bash
# Restore and build
dotnet restore ./src/TurboHTTP.sln
dotnet build --configuration Release ./src/TurboHTTP.sln

# Run all tests
dotnet test ./src/TurboHTTP.sln

# Run specific test class (xUnit v3 MTP filter — note: args after --)
dotnet test ./src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- --filter-class "TurboHTTP.Tests.Http2.Http2DecoderBasicFrameTests"

# Run tests for a component
dotnet test ./src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- --filter-namespace "TurboHTTP.Tests.Http2"

# Run tests with specific RFC trait
dotnet test ./src/TurboHTTP.Tests/TurboHTTP.Tests.csproj -- --filter "Trait~RFC9113"

# Run benchmarks
dotnet run --configuration Release ./src/TurboHTTP.Benchmarks/TurboHTTP.Benchmarks.csproj
```

### Documentation Site (requires Node.js 20+)

```bash
cd docs && npm install
npm run docs:dev          # Dev server at http://localhost:5173/TurboHTTP/
npm run docs:build        # Static site output: docs/.vitepress/dist/
npm run docs:preview      # Preview production build
```

## How to Navigate This Vault

This Obsidian vault is the single source of truth for all non-code knowledge. Start with the index, then drill into relevant sections.

**Entry points:**

| Note | Purpose |
|------|---------|
| [[00-Index\|00-Index]] | Central hub — all categories linked from here |
| [[Architecture/Design/01-LAYERED_ARCHITECTURE\|Layered Architecture]] | Full 7-layer architecture diagram and design decisions |
| [[Architecture/Status/04-CURRENT_STATE_SUMMARY\|Current State Summary]] | Implementation completeness and next milestones |
| [[Architecture/Guides/09-CLAUDE_PREFERENCES\|Claude Preferences]] | AI session workflow, response style, knowledge capture |
| [[RFC/00-RFC_STATUS_MATRIX\|RFC Status Matrix]] | Per-RFC compliance scores and gaps (⭐ start here for RFC work) |
| [[VAULT_STYLE_GUIDE\|Vault Style Guide]] | Formatting, frontmatter, and linking conventions |

**Architecture notes (00–17):**

- `00` — This onboarding guide (start here)
- `01` — [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]]
- `02` — GraphStage Patterns
- `03` — Known Gaps & Limitations
- `04` — Current State Summary
- `05` — Benchmark Patterns
- `06` — Decoder Pipeline Architecture
- `07` — HTTP/1.0 Reconnection Limitation
- `08` — HTTP/2 Decoder Migration
- `09` — [[Architecture/Guides/09-CLAUDE_PREFERENCES|Claude Preferences]] (AI workflow)
- `11` — Stage Completion Audit
- `12` — Test Organization
- `13` — Client Layer
- `14` — Transport Layer
- `15` — Streams Layer
- `16` — Protocol Layer
- `17` — Diagnostics Integration

## AI Agent Workflow

### Per-Session Duties

Every AI agent session must follow this sequence:

1. **Orient** — Read `Architecture/Guides/09-CLAUDE_PREFERENCES` and `Architecture/Status/04-CURRENT_STATE_SUMMARY`
2. **Search before acting** — Before any RFC work: `search_notes("RFC XXXX section Y")`. Before architecture decisions: `search_notes("component name")`
3. **Work** — Implement the assigned task
4. **Capture** — Before ending the session, check: did I discover something important? If yes, write to vault

### MCP Tools to Use

| Task | MCP Tool |
|------|----------|
| Find existing notes | `search_notes` |
| Read a note | `read_note` |
| Create a new note | `write_note` |
| Update part of a note | `patch_note` |
| Read multiple notes at once | `read_multiple_notes` |

**NEVER** use `Read`/`Write`/`Edit` file tools on `notes/` files — Obsidian MCP tools only.

### Knowledge Capture Rules

| Discovery Type | Destination | Template |
|---|---|---|
| RFC compliance gaps | `notes/RFC/` | RFC-Note |
| Architecture decisions | `notes/Architecture/` | ADR |
| Protocol limitations | `notes/Architecture/` | ADR |
| Bug investigations | `notes/Debugging/` (git-ignored) | Bug-Investigation |
| Feature learnings | `notes/Features/` | — |
| Session work logs | `notes/Sessions/` (git-ignored) | Session-Log |

See [[Architecture/Guides/09-CLAUDE_PREFERENCES|Claude Preferences]] for the full knowledge capture workflow.

### Workflow Rules (AI)

- **Always respond in English** — regardless of input language
- **Do NOT commit** — write `COMMIT.md` in repo root but never run `git commit` or `git add` unless explicitly asked
- **Stage files**: `git add <specific-files>` only when asked, never `git add -A`
- **TreatWarningsAsErrors** is enabled globally — zero diagnostics required before any PR

## Human Developer Workflow

### Branching Strategy

- `main` — stable, production-ready commits only
- Feature branches: `feature/description` or task-scoped branches
- All work happens in feature branches; merge to `main` after full verification

### Feature Plans

New feature work starts with a numbered feature plan. Plans include:

- Goals and acceptance criteria with task breakdown
- Token estimates, predecessor/successor dependencies
- Model recommendations per task (haiku / sonnet / opus)

To create a feature plan, use the `maggus:maggus-plan` skill in Claude Code.

### PR Process

1. Implement in a feature branch
2. Run full test suite: `dotnet test ./src/TurboHTTP.sln`
3. Verify zero diagnostics via Roslyn Navigator `get_diagnostics`
4. Stage specific changed files: `git add <files>`
5. Write commit message to `COMMIT.md` in repo root
6. Create PR targeting `main`

User-visible changes are appended to the release notes after each completed task.

## Key Code Patterns

### GraphStage Port Naming

All `GraphStage` inlet/outlet string names follow `StageName.Direction` or `StageName.Direction.Role` (PascalCase):

| Shape | Inlet | Outlet | Example |
|-------|-------|--------|---------|
| FlowShape (1 in, 1 out) | `StageName.In` | `StageName.Out` | `"Http11Encoder.In"` / `"Http11Encoder.Out"` |
| FanOutShape (1 in, 2+ out) | `StageName.In` | `StageName.Out.Role` | `"Redirect.In"` / `"Redirect.Out.Final"` |
| FanInShape (2+ in, 1 out) | `StageName.In.Role` | `StageName.Out` | `"Http20Correlation.In.Request"` |
| Custom multi-port | `StageName.In.Role` | `StageName.Out.Role` | `"Http20Connection.In.Server"` |

Rules: PascalCase, no protocol prefix, no `Stage` suffix, semantic role names (`Request`, `Response`, `Final`, `Retry`, `Signal`, `Hit`, `Miss`, `Server`, `Stream`, `App`), globally unique names across the solution.

### C# Style

```csharp
// Allman braces — opening brace on new line
public sealed class MyStage
{
    private readonly string _fieldName;

    public void DoSomething()
    {
        // Always use braces for control structures (even single-line)
        if (condition)
        {
            DoThing();
        }
    }
}
```

Key rules:

- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`
- Use `var` when type is apparent
- Default to `sealed` classes and records
- Do NOT add `#nullable enable` — enabled at project level in `.csproj`
- Never use `async void`, `.Result`, or `.Wait()`
- Always pass `CancellationToken` through async call chains
- Always use braces for control structures, even single-line

### Test Conventions (Post-Feature-040)

```csharp
// Namespace matches component folder
namespace TurboHTTP.Tests.Http2.Encoding;

public sealed class Http2EncoderSpec : StreamTestBase
{
    // Timeout is REQUIRED on all async tests
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9113-4.1")]
    public async Task Http2Encoder_should_encode_data_frame_correctly()
    {
        // ...
    }

    // Theory with InlineData for parameterised cases
    [Theory(Timeout = 5000)]
    [InlineData("GET"), InlineData("POST")]
    [Trait("RFC", "RFC9113-4.3")]
    public async Task Http2Encoder_must_include_method_pseudo_header(string method)
    {
        // ...
    }
}
```

Key rules (post-Feature-040):

- **Test classes**: `public sealed class`, namespace matches component folder (e.g., `TurboHTTP.Tests.Http2.Encoding`, `TurboHTTP.Tests.Caching`)
- **File naming**: `<Subject>Spec.cs` — descriptive name with `Spec` suffix (Akka.NET convention)
- **Use `[Fact]`** for single cases, **`[Theory]`** + **`[InlineData]`** for parameterised cases
- **RFC Traceability**: `[Trait("RFC", "RFC<number>-<section>")]` (e.g., `[Trait("RFC", "RFC9113-4.1")]`)
  - CI filter: `dotnet test --filter "Trait~RFC9113"`
- **Method names**: BDD style `Subject_should_behavior()` (e.g., `Http2Encoder_should_encode_data_frame_correctly()`)
- **Timeout is REQUIRED** — all async tests must have `[Fact(Timeout = 5000)]` or `[Theory(Timeout = 5000)]`
- **Max 500 lines per test class** — split into multiple files if exceeded
- Do NOT add `#nullable enable` at the top of test files

## See Also

- [[VAULT_STYLE_GUIDE|Vault Style Guide]] — how to write notes, frontmatter standards, quality checklist
- [[Architecture/Guides/09-CLAUDE_PREFERENCES|Claude Preferences]] — AI session workflow in detail
- [[Architecture/Design/01-LAYERED_ARCHITECTURE|Layered Architecture]] — full architecture reference
- [[Architecture/Status/04-CURRENT_STATE_SUMMARY|Current State Summary]] — project status and roadmap
- [[RFC/00-RFC_STATUS_MATRIX|RFC Status Matrix]] — compliance tracking by RFC
- [[Architecture/Guides/12-TEST_ORGANIZATION|Test Organization]] — detailed test folder mapping and structure
