# RFC Coverage — TurboHttp

**Updated:** 2026-03-19 (TASK-036)
**Scope:** Client-side only. Encoders: `HttpRequestMessage → bytes`. Decoders: `bytes → HttpResponseMessage`.

---

## Compliance Summary

| RFC | Standard | Sections Covered | Coverage | Unit Tests | Stream Tests | Integration Tests |
|-----|----------|-----------------|----------|-----------|-------------|-------------------|
| RFC 1945 | HTTP/1.0 | §4–§10 | 100% | 233 | 41 | — |
| RFC 9112 | HTTP/1.1 Message Framing | §2–§9 | 100% | 374 | 97 | — |
| RFC 9113 | HTTP/2 | §3–§8 | 100% | 545 | 180 | — |
| RFC 7541 | HPACK Header Compression | §2–§6, Appendix C | 100% | 419 | 8 | — |
| RFC 9110 | HTTP Semantics | §8.4, §9.2, §12.5, §15.4 | 100% | 123 | 55 | — |
| RFC 6265 | HTTP Cookies | §4–§5 | 100% | 66 | 12 | — |
| RFC 9111 | HTTP Caching | §3–§5 | 100% | 75 | 28 | — |
| — | IO Layer | TcpOptions, ClientManager, Actors | 100% | — | 47 | — |
| — | Stage Infrastructure | Connection, Engine, Enricher, buffers | 100% | — | 132 | — |
| **Total** | | | | **1835** | **600** | **0** |

> **Note:** Integration tests (`src/TurboHttp.IntegrationTests/`) have Kestrel fixtures with 60+ routes but no end-to-end test classes yet.

---

## Remaining Gaps

| Gap ID | RFC Section | Requirement | Priority | Status | Notes |
|--------|-------------|-------------|----------|--------|-------|
| GAP-001 | RFC 7231 §7.1.1 | Date/time format parsing (IMF-fixdate, RFC 850, asctime) | MUST | Deferred | Out of scope for protocol layer; `HttpResponseMessage` exposes raw `Date` header string. Consistent with .NET `HttpClient` behaviour. |

All other gaps from the original matrix have been closed:
- GAP-002/003 (DATA/HEADERS PADDED flag) → closed by `16_DecoderPaddingTests.cs`
- GAP-004 (Push-Promise state machine) → closed by `15_DecoderPushPromiseTests.cs`
- GAP-005 (StreamIdAllocatorStage) → closed by `StreamIdAllocatorStageTests.cs`
- GAP-006/007 (Correlation stages) → closed by `Http1XCorrelationStageTests.cs` and `Http20CorrelationStageTests.cs`
- GAP-008 (ExtractOptionsStage) → closed by `ExtractOptionsStageTests.cs`

---

## Test Structure Conventions

### File Naming

- **Pattern (TurboHttp.Tests):** `NN_<ThemaTests>.cs` — two-digit prefix groups tests by RFC section; all files are numbered
- **Pattern (TurboHttp.StreamTests RFC folders):** descriptive names without `NN_` prefix (e.g. `Http11EncoderStageTests.cs`)
- **Pattern (TurboHttp.StreamTests `Streams/`):** `NN_` prefix used for ordered tests; unnumbered for general stage tests

### DisplayName Format

```
"RFC<number>-<section>-<category>-<sequence>: <description>"
```

Examples:
- `"RFC9113-5.1.1-PP-001: PUSH_PROMISE moves stream to reserved(remote) state"`
- `"RFC9112-7.1-CH-003: Chunked body with multiple chunks"`
- `"RFC9111-§4.2: max-age=60 freshness lifetime"`

### Test Classes

- `public sealed class` with namespace matching RFC folder (e.g. `namespace TurboHttp.Tests.RFC9113;`)
- `[Fact]` for single cases, `[Theory]` + `[InlineData]` for parameterised cases
- Stream tests extend `StreamTestBase` (which extends `TestKit` and creates `IMaterializer`)

---

## Folder → RFC Section → Test File Mapping

### `src/TurboHttp.Tests/RFC1945/` — HTTP/1.0 (17 files, 233 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_EncoderRequestLineTests.cs` | §5.1 Request-Line | Request-line format, HTTP/1.0 version |
| `02_EncoderHeaderTests.cs` | §4 Headers | Header encoding, no Host/TE/Connection |
| `03_EncoderBodyTests.cs` | §7 Message Body | Content-Length, body encoding |
| `04_EncoderSecurityTests.cs` | §5 Encoder | Security edge cases |
| `05_DecoderStatusLineTests.cs` | §6 Status-Line | Status-line parsing, version |
| `06_DecoderHeaderTests.cs` | §4 Headers | Header parsing |
| `07_DecoderBodyTests.cs` | §7 Message Body | Body parsing, Content-Length |
| `08_DecoderConnectionTests.cs` | §8 Connection | Connection close/keep-alive |
| `09_DecoderFragmentationTests.cs` | §7 Message Body | TCP boundary handling |
| `10_DecoderStateTests.cs` | §6 Decoder | State machine, Reset() |
| `11_RoundTripMethodTests.cs` | §5 Methods | GET, HEAD, POST round-trip |
| `12_RoundTripStatusCodeTests.cs` | §6 Status Codes | All RFC 1945 status codes |
| `13_RoundTripHeaderTests.cs` | §4 Headers | Header round-trip fidelity |
| `14_RoundTripBodyTests.cs` | §7 Body | Body round-trip fidelity |
| `15_RoundTripFragmentationTests.cs` | §7 Body | Fragmented TCP round-trip |
| `16_RoundTripProtocolTests.cs` | §4–§8 | Cross-section protocol tests |
| `17_EncoderStageConversionExampleTests.cs` | §5 Encoder | Encoder stage conversion examples |

### `src/TurboHttp.Tests/RFC9112/` — HTTP/1.1 (26 files, 374 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_EncoderRequestLineTests.cs` | §2.1 Request-Line | Method SP target SP version |
| `02_EncoderHostHeaderTests.cs` | §7.2 Host | Host header MUST be present |
| `03_EncoderHeaderTests.cs` | §3.2 Headers | Header field format, OWS |
| `04_EncoderConnectionTests.cs` | §6.1 Connection | Connection header, hop-by-hop stripping |
| `05_EncoderBodyTests.cs` | §3.3 Message Body | Content-Length, Transfer-Encoding |
| `06_EncoderRangeRequestTests.cs` | RFC 7233 §2.1 | Range header encoding |
| `07_EncoderLegacyTests.cs` | §3.1 | Legacy compatibility |
| `08_DecoderStatusLineTests.cs` | §3 Status-Line | HTTP/1.1 status parsing |
| `09_DecoderHeaderTests.cs` | §3.2 Headers | Header field parsing |
| `10_DecoderBodyTests.cs` | §3.3 Message Body | Content-Length body, zero-length |
| `11_DecoderChunkedTests.cs` | §4.1 Chunked | Chunked transfer decoding |
| `12_DecoderNoBodyTests.cs` | §3.3 | 1xx/204/304 no-body responses |
| `13_DecoderFragmentationTests.cs` | §3.3 | TCP fragmentation handling |
| `14_DecoderLegacyTests.cs` | §3 | LF-only endings, edge cases |
| `15_RoundTripMethodTests.cs` | §2.1/§3 | Method round-trip |
| `16_RoundTripChunkedTests.cs` | §4.1 | Chunked encoding round-trip |
| `17_RoundTripStatusCodeTests.cs` | §3 Status-Line | All status codes |
| `18_RoundTripPipeliningTests.cs` | §9.3 Pipelining | FIFO correlation |
| `19_RoundTripNoBodyTests.cs` | §3.3 | No-body round-trip |
| `20_RoundTripBodyTests.cs` | §3.3 | Body round-trip fidelity |
| `21_RoundTripFragmentationTests.cs` | §3.3 | Fragmented round-trip |
| `22_ConnectionReuseTests.cs` | §9 Connection Mgmt | Keep-alive, close, HTTP/1.0 opt-in |
| `23_PerHostLimiterTests.cs` | §9 Connection Mgmt | Per-host concurrency limits |
| `24_Http11DecoderChunkExtensionTests.cs` | §4.1.1 | Chunk extension edge cases |
| `25_Http11NegativePathTests.cs` | §2–§4 | Negative/error path testing |
| `26_Http11SecurityTests.cs` | §2–§4 | Security-focused tests |

### `src/TurboHttp.Tests/RFC9113/` — HTTP/2 (27 files, 545 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_ConnectionPrefaceTests.cs` | §3.4 | Client preface magic + SETTINGS |
| `02_FrameParsingTests.cs` | §4.1 | 9-byte frame header parsing |
| `03_StreamStateMachineTests.cs` | §5.1 | Stream state transitions |
| `04_SettingsTests.cs` | §6.5 | SETTINGS parameter negotiation |
| `05_FlowControlTests.cs` | §5.2 | WINDOW_UPDATE, flow control |
| `06_HeadersTests.cs` | §6.2 | HEADERS frame handling |
| `07_ErrorHandlingTests.cs` | §5.4 | Error code handling |
| `08_GoAwayTests.cs` | §6.8 | GOAWAY graceful shutdown |
| `09_ContinuationFrameTests.cs` | §6.10 | CONTINUATION frame assembly |
| `10_DecoderBasicFrameTests.cs` | §4.1–§6 | Frame-level decode tests |
| `11_DecoderStreamValidationTests.cs` | §5.1 | Stream ID validation |
| `12_DecoderStreamFlowControlTests.cs` | §5.2/§6.9 | Stream-level flow control |
| `13_DecoderErrorCodeTests.cs` | §7 | Error code mapping |
| `14_DecoderPushPromiseTests.cs` | §5.1.1/§6.6 | PUSH_PROMISE state machine |
| `15_DecoderPaddingTests.cs` | §6.1/§6.2 | PADDED flag on DATA/HEADERS |
| `16_EncoderBaselineTests.cs` | §4.1 | Frame serialisation baseline |
| `17_EncoderRfcTaggedTests.cs` | §4–§8 | RFC-tagged encoder tests |
| `18_EncoderStreamSettingsTests.cs` | §6.5 | SETTINGS encoder |
| `19_RequestEncoderFrameTests.cs` | §8.1 | Request → frames conversion |
| `20_EncoderPseudoHeaderTests.cs` | §8.3.1 | Pseudo-header validation |
| `21_FuzzHarnessTests.cs` | §4.2/§5.5/§6.4 | Fuzz/boundary testing |
| `22_SettingsMaxConcurrentTests.cs` | §6.5.2 | MAX_CONCURRENT_STREAMS |
| `23_ResourceExhaustionTests.cs` | §5.1/§6.9 | Resource limits |
| `24_HighConcurrencyTests.cs` | §5.1.2 | High concurrency scenarios |
| `25_CrossComponentValidationTests.cs` | §4.3/§6.8 | Cross-component validation |
| `26_SecurityTests.cs` | §6.4/§6.10 | Security edge cases |
| `27_Http2FrameTests.cs` | — | Frame utility helpers |

### `src/TurboHttp.Tests/RFC7541/` — HPACK (7 files, 419 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_StaticTableTests.cs` | §2.3.1/Appendix A | Static table lookup |
| `02_DynamicTableTests.cs` | §2.3.2 | Dynamic table FIFO, eviction |
| `03_HpackTests.cs` | §2–§6 | General HPACK integration |
| `04_HuffmanTests.cs` | §5.2 | Huffman encoding/decoding |
| `05_HeaderBlockDecodingTests.cs` | §6.1–§6.2 | Indexed + literal header fields |
| `06_TableSizeTests.cs` | §4.2 | Table size updates |
| `07_SensitiveHeaderTests.cs` | §7.1.3 | NeverIndex for sensitive headers |

### `src/TurboHttp.Tests/RFC9110/` — HTTP Semantics (2 files, 123 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_RedirectHandlerTests.cs` | §15.4 | 301/302/303/307/308 redirects, method rewriting, HTTPS→HTTP protection, loop detection |
| `02_RetryEvaluatorTests.cs` | §9.2 | Idempotency-based retry, Retry-After parsing |

### `src/TurboHttp.Tests/RFC9111/` — HTTP Caching (4 files, 75 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_CacheControlParserTests.cs` | §5.2 | Cache-Control directive parsing |
| `02_CacheFreshnessTests.cs` | §4.2 | Freshness lifetime, current age, heuristic |
| `03_ConditionalRequestTests.cs` | §4.3 | If-None-Match, If-Modified-Since, 304 merge |
| `04_CacheStoreTests.cs` | §3 | LRU cache, Vary support, invalidation |

### `src/TurboHttp.Tests/RFC6265/` — HTTP Cookies (2 files, 66 tests)

| File | RFC Section | Coverage |
|------|-------------|----------|
| `01_CookieJarTests.cs` | §4–§5 | Domain/path matching, Secure/HttpOnly/SameSite, Max-Age/Expires |
| `02_CookieJarThreadSafetyTests.cs` | §4–§5 | Thread-safety of CookieJar under concurrent access |

### `src/TurboHttp.StreamTests/` — Akka.Streams Stage Tests (600 tests)

| Subfolder | Files | Tests | Coverage |
|-----------|-------|-------|----------|
| `RFC1945/` | 8 | 41 | HTTP/1.0 encoder/decoder/roundtrip stages, TCP fragmentation |
| `RFC6265/` | 2 | 12 | Cookie injection and storage stages |
| `RFC7541/` | 1 | 8 | HPACK stream integration |
| `RFC9110/` | 3 | 55 | Decompression, redirect, retry stages |
| `RFC9111/` | 2 | 28 | Cache lookup and storage stages |
| `RFC9112/` | 13 | 97 | HTTP/1.1 encoder/decoder/chunked/correlation/pipeline/connection mgmt stages |
| `RFC9113/` | 22 | 180 | HTTP/2 encoder/decoder/connection/stream/HPACK/pseudo-header/flow-control/correlation stages |
| `Streams/` | 16 | 132 | Connection, engine routing, enricher, buffer lifecycle, pipeline wiring |
| `IO/` | 7 | 47 | ConnectionActor, HostPoolActor, ConnectionState, ConnectionHandle |

### `src/TurboHttp.IntegrationTests/` — Kestrel Fixtures (infrastructure only)

| File | Purpose |
|------|---------|
| `Shared/KestrelFixture.cs` | HTTP/1.1 test server |
| `Shared/KestrelH2Fixture.cs` | HTTP/2 test server |
| `Shared/KestrelTlsFixture.cs` | HTTPS/TLS test server |
| `Shared/Routes.cs` | 60+ registered test routes |
| `Shared/TestKit.cs` | Test infrastructure helpers |

> No end-to-end test classes yet — fixtures are ready for future integration tests.

---

## Pre-Existing Test Failures

| Test | Project | Issue | Status |
|------|---------|-------|--------|
| 6 RFC9113 tests | TurboHttp.Tests | H2 concurrency timing issues | Known, pre-existing |
| RFC9110-15.4-RH-015 | TurboHttp.Tests | Relative Location URI resolution | Known, pre-existing |
