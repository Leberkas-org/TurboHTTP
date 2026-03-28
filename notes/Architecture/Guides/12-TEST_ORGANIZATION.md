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

**Last Updated**: 2026-03-26

## Test Projects

| Project | Purpose | Count |
|---------|---------|-------|
| `src/TurboHttp.Tests/` | Unit tests organized by RFC | 260+ |
| `src/TurboHttp.StreamTests/` | Akka.Streams stage behavior tests | — |
| `src/TurboHttp.IntegrationTests/` | End-to-end tests with Kestrel | 515+ |
| `src/TurboHttp.Benchmarks/` | BenchmarkDotNet performance tests | 25+ |

## Unit Tests (`TurboHttp.Tests/`)

Organized by RFC in subfolders:

| Folder | RFC | Files | Tests |
|--------|-----|-------|-------|
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

**File naming**: `NN_<ThemaTests>.cs` — two-digit prefix groups by RFC section.

## Stream Tests (`TurboHttp.StreamTests/`)

Tests Akka.Streams GraphStage behavior. Organized by RFC (mirroring `TurboHttp.Tests`):

| Folder | Coverage |
|--------|----------|
| `RFC1945/` | HTTP/1.0 encoder/decoder/roundtrip stages, TCP fragmentation |
| `RFC6265/` | Cookie injection and storage stage tests |
| `RFC7541/` | HPACK stream integration tests |
| `RFC9110/` | Decompression, redirect, retry stage tests |
| `RFC9111/` | Cache lookup and storage stage tests |
| `RFC9112/` | HTTP/1.1 encoder/decoder/chunked/correlation/pipeline/connection stages |
| `RFC9113/` | HTTP/2 encoder/decoder/connection/stream/HPACK/pseudo-header/flow-control/correlation stages |
| `RFC9114/` | HTTP/3 encoder/decoder/connection/stream/field-validation/origin-validation/certificate/idle-timeout stages |
| `RFC9204/` | QPACK stream stage tests |
| `Streams/` | Stage infrastructure: connection, engine routing, enricher, buffer lifecycle, pipeline wiring |
| `IO/` | ConnectionActor, HostPool, ConnectionState, ConnectionHandle, ClientByteMover, ClientRunner, QUIC tests |

**File naming**: RFC subfolder files use descriptive names (`Http11EncoderStageTests.cs`); `Streams/` uses `NN_` prefix for ordered tests.

## Base Classes

### StreamTestBase
- Extends `TestKit` (Akka.TestKit.Xunit)
- Creates `IMaterializer` for test-scoped stream materialization
- Used by all stream tests in `TurboHttp.StreamTests/`

### EngineTestBase
- Full engine round-trip helper
- Builds complete protocol engine graphs for integration-style stream tests
- Provides helper methods for encoding requests and decoding responses through the full pipeline

### IOActorTestBase
- Actor lifecycle tests in `TurboHttp.StreamTests/IO/`
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

## Conventions

- **Max 500 lines** per test file — split into multiple focused files if exceeded
- **Timeout REQUIRED** on all async tests: `[Fact(Timeout = 5000)]` or `CancellationToken`
- **DisplayName**: `"RFC-section-cat-nnn: description"` on all `[Fact]`/`[Theory]`
- **Sealed classes**: `public sealed class` for all test classes
- **Namespace**: matches RFC folder (e.g., `namespace TurboHttp.Tests.RFC9113;`)
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
