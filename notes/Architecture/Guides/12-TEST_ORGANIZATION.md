---
title: Test Organization & Infrastructure
description: >-
  Test project structure, base classes, integration fixtures, folder mapping,
  and conventions
tags:
  - testing
  - infrastructure
  - conventions
  - xunit
aliases:
  - Test Structure
  - Test Infrastructure
  - Testing Guide
---
# Test Organization & Infrastructure

**Last Updated**: 2026-04-07

## Test Projects

| Project | Purpose | Count |
|---------|---------|-------|
| `src/TurboHTTP.Tests/` | Unit tests organized by component/protocol version | 260+ |
| `src/TurboHTTP.StreamTests/` | Akka.Streams stage behavior tests | — |
| `src/TurboHTTP.IntegrationTests/` | End-to-end tests with Kestrel | 515+ |
| `src/TurboHTTP.Benchmarks/` | BenchmarkDotNet performance tests | 25+ |

## Unit Tests (`TurboHTTP.Tests/`)

Organized by component/protocol version (post-Feature-040):

| Folder | Component | RFC | Example Files |
|--------|-----------|-----|----------------|
| `Http10/` | HTTP/1.0 | RFC 1945 | `Http10EncoderSpec.cs`, `Http10ParserSpec.cs` |
| `Http11/` | HTTP/1.1 | RFC 9112 | `Http11EncoderSpec.cs`, `Http11ChunkedDecoderSpec.cs` |
| `Http11/Encoding/` | HTTP/1.1 Encoding | RFC 9112 | `Http11EncoderSpec.cs` |
| `Http11/Decoding/` | HTTP/1.1 Decoding | RFC 9112 | `Http11DecoderSpec.cs` |
| `Http11/Chunking/` | HTTP/1.1 Chunked Transfer | RFC 9112 | `Http11ChunkedDecoderSpec.cs` |
| `Http2/` | HTTP/2 Frames & Streams | RFC 9113 | `Http2FrameDecoderSpec.cs`, `Http2ConnectionSpec.cs` |
| `Http2/Frames/` | HTTP/2 Frame Layer | RFC 9113 | `Http2FrameDecoderSpec.cs` |
| `Http2/Connection/` | HTTP/2 Connection | RFC 9113 | `Http2ConnectionSpec.cs` |
| `Http2/Stream/` | HTTP/2 Stream | RFC 9113 | `Http2StreamSpec.cs` |
| `Http2/Hpack/` | HPACK Header Compression | RFC 7541 | `HpackEncodingSpec.cs`, `HpackDecodingSpec.cs` |
| `Http3/` | HTTP/3 (QUIC) | RFC 9114 | `Http3ConnectionSpec.cs`, `Http3FrameDecoderSpec.cs` |
| `Http3/Frames/` | HTTP/3 Frame Layer | RFC 9114 | `Http3FrameDecoderSpec.cs` |
| `Http3/Connection/` | HTTP/3 Connection | RFC 9114 | `Http3ConnectionSpec.cs` |
| `Http3/Qpack/` | QPACK Header Compression | RFC 9204 | `QpackEncodingSpec.cs`, `QpackDecodingSpec.cs` |
| `Semantics/` | HTTP Semantics | RFC 9110 | `RedirectHandlingSpec.cs`, `RetryPolicySpec.cs` |
| `Caching/` | HTTP Caching | RFC 9111 | `CacheValidationSpec.cs`, `CacheStorageSpec.cs` |
| `Cookies/` | HTTP State Management | RFC 6265 | `CookieInjectionSpec.cs`, `CookieStorageSpec.cs` |
| `Transport/` | Connection pooling & management | — | `ConnectionPoolSpec.cs`, `LeaseManagementSpec.cs` |
| `Security/` | TLS, certificate validation | — | `CertificateValidationSpec.cs` |
| `Diagnostics/` | Telemetry & logging | — | `LoggingSpec.cs`, `TraceContextSpec.cs` |
| `Hosting/` | Client builder & DI | — | `ClientBuilderSpec.cs`, `HostingExtensionsSpec.cs` |

**File naming**: `<Subject>Spec.cs` — descriptive name with `Spec` suffix (Akka.NET convention). Numeric prefixes (`NN_`) are deprecated.

## Stream Tests (`TurboHTTP.StreamTests/`)

Tests Akka.Streams GraphStage behavior. Organized by component (mirroring `TurboHTTP.Tests`):

| Folder | Coverage |
|--------|----------|
| `Http10/` | HTTP/1.0 encoder/decoder/roundtrip stages, TCP fragmentation |
| `Http11/` | HTTP/1.1 encoder/decoder/chunked/correlation/pipeline/connection stages |
| `Http2/Frames/` | HTTP/2 frame encoding/decoding stages |
| `Http2/Connection/` | HTTP/2 connection management stages |
| `Http2/Stream/` | HTTP/2 stream lifecycle stages |
| `Http2/Hpack/` | HPACK encoder/decoder stream integration |
| `Http3/Frames/` | HTTP/3 frame encoding/decoding stages |
| `Http3/Connection/` | HTTP/3 connection management stages |
| `Http3/Qpack/` | QPACK encoder/decoder stream integration |
| `Semantics/` | Decompression, redirect, retry stage tests |
| `Caching/` | Cache lookup and storage stage tests |
| `Cookies/` | Cookie injection and storage stage tests |
| `Streams/` | Stage infrastructure: connection, engine routing, enricher, buffer lifecycle, pipeline wiring |
| `IO/` | ConnectionActor, HostPool, ConnectionState, ConnectionHandle, ClientByteMover, ClientRunner, QUIC tests |

**File naming**: Component-based folder files use descriptive names with `Spec` suffix (`Http11EncoderSpec.cs`, `HpackEncodingSpec.cs`); `Streams/` and `IO/` use numeric prefix for ordered tests.

## Base Classes

### StreamTestBase
- Extends `TestKit` (Akka.TestKit.Xunit)
- Creates `IMaterializer` for test-scoped stream materialization
- Used by all stream tests in `TurboHTTP.StreamTests/`

### EngineTestBase
- Full engine round-trip helper
- Builds complete protocol engine graphs for integration-style stream tests
- Provides helper methods for encoding requests and decoding responses through the full pipeline

### IOActorTestBase
- Actor lifecycle tests in `TurboHTTP.StreamTests/IO/`
- Tests connection actors, host pools, and transport-level behavior

## Integration Test Fixtures

Kestrel-based fixtures for end-to-end HTTP testing:

| Fixture | Protocol | Purpose |
|---------|----------|---------|
| `KestrelFixture` | HTTP/1.1 (plaintext) | Standard HTTP/1.1 testing |
| `KestrelH2Fixture` | HTTP/2 (TLS) | HTTP/2 over HTTPS testing |
| `KestrelH3Fixture` | HTTP/3 (QUIC) | HTTP/3 over QUIC testing |
| `KestrelTlsFixture` | HTTP/1.1 (TLS) | TLS/HTTPS testing |

- **60+ routes** registered across fixtures
- **SmokeTests.cs** provides initial end-to-end coverage
- Each fixture starts a real Kestrel server with dynamic port discovery

## Conventions (Post-Feature-040)

- **Max 500 lines** per test class — split into multiple focused files if exceeded
- **Timeout REQUIRED** on all async tests: `[Fact(Timeout = 5000)]` or `CancellationToken`
- **RFC Traceability**: Use `[Trait("RFC", "RFC<number>-<section>")]` instead of `DisplayName` (e.g., `[Trait("RFC", "RFC9113-4.1")]`)
- **Method names**: BDD style `Subject_should_behavior()` (e.g., `Http2Encoder_should_set_key_from_frame()`)
- **Sealed classes**: `public sealed class` for all test classes
- **Namespace**: matches component folder (e.g., `namespace TurboHTTP.Tests.Http2;` or `TurboHTTP.Tests.Http2.Encoding;`)
- **File naming**: `<Subject>Spec.cs` with `Spec` suffix (Akka.NET convention)
- **No `#nullable enable`**: enabled at project level

## Completed Testing Phases

| Phase | Description | Result |
|-------|-------------|--------|
| 1-10 | RFC Compliance (HTTP/1.0, 1.1, 2.0, HPACK) | 260+ unit tests |
| 11 | Core Benchmarks | 26 benchmarks |
| 12-17 | Integration Tests | 515+ tests (real TCP + Kestrel) |
| 18 | Core Performance Validation | 15 benchmarks |
| 19 | Streaming & Protocol Efficiency | 14 benchmarks |
| 20 | Concurrency & Production Load Simulation | 16 benchmarks |
| 21 | Enterprise Stability & Real World Patterns | 21 benchmarks |
| 22 | Release Throughput Validation | 2 benchmarks |
| 39 | Http2Decoder deprecation | ✅ Marked [Obsolete], 509 warnings, 0 errors |
