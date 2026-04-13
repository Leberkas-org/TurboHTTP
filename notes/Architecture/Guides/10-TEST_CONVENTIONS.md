---
title: Test Conventions
tags: [architecture, testing, conventions]
created: 2026-04-13
updated: 2026-04-13
---

# Test Conventions

## Structure (Component-Based, Post-Feature-040)

Starting with Feature 040, test files are organized by **component/protocol version**, not RFC number:

| Project | Structure |
|---------|-----------|
| `TurboHTTP.Tests/` | `Http10/`, `Http11/`, `Http2/`, `Http3/`, `Semantics/`, `Caching/`, `Cookies/`, `Transport/`, `Security/`, `Diagnostics/`, `Hosting/` |
| `TurboHTTP.StreamTests/` | `Http10/`, `Http11/`, `Http2/`, `Http3/`, `Semantics/`, `Caching/`, `Cookies/`, `Transport/`, `Dispatchers/`, `Streams/` |
| `TurboHTTP.IntegrationTests/` | Unchanged: `H10/`, `H11/`, `H2/`, `H3/`, `TLS/` |

## RFC → Component Mapping

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

## File & Class Naming Rules

### Old Convention (RFC-based, deprecated)

```csharp
// File: RFC9113/01_Http2EncoderStageTests.cs
// Class: Http2EncoderStageTests
// Method: [Fact(DisplayName = "RFC9113-4.1-FRM-005: description")]
public async Task Should_SetKeyFromFrame() { }
```

### New Convention (component-based, post-Feature-040)

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

## Naming Conventions (Post-Feature-040)

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

## Migration Priority (Strangler Fig Strategy)

The RFC-based folders are being replaced incrementally. Migration order:

1. **Cookies (RFC 6265)** → `Cookies/` — 2-3 files (quick win)
2. **Caching (RFC 9111)** → `Caching/` — 6-8 files (quick win)
3. **Semantics (RFC 9110)** → `Semantics/` — ~17 files (opportunistic)
4. **Http10 (RFC 1945)** → `Http10/` — ~28 files (opportunistic)
5. **Http11 (RFC 9112)** → `Http11/` — ~44 files (opportunistic)
6. **Http2 + HPACK (RFC 9113 + RFC 7541)** → `Http2/` — ~36 files (Feature 40-62 Http2Decoder migration)
7. **Http3 + QPACK (RFC 9114 + RFC 9204)** → `Http3/` — ~60 files (opportunistic)

**No big-bang sprint:** New tests land directly in the new structure; old tests migrate as they are touched.

## Guard-Rail: spec-naming-validator

The `spec-naming-validator` agent validates naming conventions in new component-based test files:
- Checks `Spec.cs` file names, `sealed` classes, BDD method names, `[Trait("RFC", ...)]` usage
- Does NOT block build/tests — it is a quality gate for new code
- Run after adding new test files: `spec-naming-validator` (`.claude/agents/spec-naming-validator`)
