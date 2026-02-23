# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TurboHttp is a high-performance HTTP protocol implementation library for .NET using Akka.NET streaming. It implements HTTP/1.0, HTTP/1.1, and HTTP/2 protocols with full RFC compliance.

## Build Commands

```bash
# Restore and build
dotnet restore ./src/TurboHttp.sln
dotnet build --configuration Release ./src/TurboHttp.sln

# Run all tests
dotnet test ./src/TurboHttp.sln

# Run specific test class
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "ClassName=TurboHttp.Tests.HpackTests"

# Run specific test method
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "FullyQualifiedName~HpackTests.Encode_Decode_RoundTrip"

# Run benchmarks
dotnet run --configuration Release ./src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj"
```

## Architecture

### Layered Design

```
Protocol Layer (TurboHttp/Protocol/)
    HTTP 1.0/1.1/2 Encoders/Decoders, HPACK compression
         ↓
I/O Layer (TurboHttp/IO/)
    Akka Actors + TcpClient + System.Threading.Channels
         ↓
Network (TcpStream)
```

### Protocol Layer (`TurboHttp/Protocol/`)

**Encoders** - Stateless, static methods that serialize `HttpRequestMessage` to bytes:
- `Http10Encoder.Encode()`, `Http11Encoder.Encode()`, `Http2Encoder.Encode()`
- Use `ref Span<byte>` or `ref Memory<byte>` for zero-allocation patterns

**Decoders** - Stateful classes that handle partial frame buffering across TCP boundaries:
- Maintain `_remainder` field for incomplete messages
- Methods: `TryDecode()` for normal parsing, `TryDecodeEof()` for connection close scenarios
- `Reset()` to clear state between connections

**HPACK (RFC 7541)** - Stateful header compression for HTTP/2:
- `HpackEncoder`/`HpackDecoder` maintain synchronized dynamic tables
- `HpackDynamicTable` - FIFO queue with 32-byte per-entry overhead
- `HuffmanCodec` - Static Huffman encoding/decoding
- Sensitive headers (Authorization, Cookie) automatically use NeverIndex

### I/O Layer (`TurboHttp/IO/`)

Actor-based connection management using Akka.NET:
- `TcpConnectionManagerActor` - Supervises TCP connections
- `TcpClientRunner` - Per-connection lifecycle actor
- `TcpClientState` - Shared state wrapper (channels + buffers)
- `TcpClientByteMover` - Async byte transfer between TCP and channels

### HTTP/2 Frame Types (`Http2Frame.cs`)

All frame types inherit from `Http2Frame` base class with:
- `SerializedSize` property for buffer pre-allocation
- `WriteTo(ref Span<byte>)` for serialization
- Frame header: 9 bytes (length:24, type:8, flags:8, stream:31)

## Key Patterns

### Memory Management
- Use `ReadOnlyMemory<byte>` and `Span<T>` for buffer efficiency
- `IMemoryOwner<byte>` requires proper disposal
- `IBufferWriter<byte>` for zero-copy encoding output

### Error Handling
- `HpackException` - RFC 7541 violations
- `Http2Exception` - HTTP/2 protocol errors
- `HttpDecoderException` - General decode failures
- `HttpDecodeError` enum for error classification

## Code Style and Conventions

### C# Style
- Allman style braces (opening brace on new line)
- 4 spaces indentation, no tabs
- Private fields prefixed with underscore `_fieldName`
- Use `var` when type is apparent
- Default to `sealed` classes and records
- Enable `#nullable enable` in new/modified files
- Never use `async void`, `.Result`, or `.Wait()`
- Always pass `CancellationToken` through async call chains
- Always use braces for control structures (even single-line statements)

### API Design
- Use `Task<T>` instead of Future, `TimeSpan` instead of Duration
- Extend-only design - don't modify existing public APIs
- Preserve wire format compatibility for serialization
- Include unit tests with all changes

### Test Naming
- Use `DisplayName` attribute for descriptive test names
- Follow pattern: `Should_ExpectedBehavior_When_Condition`

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

## RFC Compliance

- **HTTP/1.0**: RFC 1945
- **HTTP/1.1**: RFC 9112 (message framing)
- **HTTP/2**: RFC 9113 (protocol), RFC 7541 (HPACK)

## Dependencies

- **Akka.Streams** 1.5.60 - Actor-based stream processing
- **Servus.Akka** 0.3.10 - TCP abstraction layer
- **.NET 10.0** - Target framework
