# Feature 003: Integration Tests — Body Handling & Transfer Encoding

## Introduction

Aufbauend auf der Infrastruktur aus Feature 002 testet dieses Feature das Request/Response-Body-Handling über alle HTTP-Versionen: Text, JSON, Binärdaten, Chunked Transfer Encoding (HTTP/1.1), Frame-basierte Übertragung (HTTP/2), QUIC-Streams (HTTP/3) und Streaming-Responses.

### Architecture Context

- **Components involved:**
  - `Http10Encoder/Decoder` — Content-Length-basierte Bodies
  - `Http11Encoder/Decoder` — Chunked Transfer-Encoding, Trailers
  - `Http20EncoderStage/Http20StreamStage` — Frame-basierte DATA-Frames
  - `Http30EncoderStage/Http30StreamStage` — QUIC-Stream-basierte Bodies
  - Pipeline: `DecompressionBidiStage` (hier noch ohne Decompression — das ist Feature 005)
- **Existing routes used:** `/echo`, `/echo/chunked`, `/chunked/{kb}`, `/chunked/exact/{count}/{bytes}`, `/chunked/trailer`, `/chunked/md5`, `/large/{kb}`, `/slow/{count}`, `/h2/echo-binary`, `/h2/large-headers/{kb}`, `/h2/priority/{kb}`, `/h3/echo-binary`, `/h3/stream/{count}`
- **Depends on:** Feature 002 (ClientHelper, Collections)

## Goals

- Request-Body-Echo für alle Content-Types (text, JSON, binary) verifizieren
- Chunked Transfer-Encoding (HTTP/1.1) korrekt dekodiert
- Chunked-Trailers empfangen und zugänglich
- Große Payloads (bis 512 KB) über alle Versionen
- Streaming-Responses (inkrementell empfangen)
- Zero-Length-Bodies korrekt behandelt
- Frame-basierte Body-Übertragung in HTTP/2 und HTTP/3

## Tasks

### TASK-003-001: HTTP/1.x Body & Chunked Tests
**Description:** Als Entwickler möchte ich Body-Handling und Chunked-Encoding Tests für HTTP/1.x, damit Text-, JSON-, Binär-Bodies und Chunked-Responses verifiziert sind.

**Token Estimate:** ~75k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — kann parallel zu TASK-003-002 laufen

**Acceptance Criteria:**
- [ ] `Http1BodyTests.cs` mit `[Collection("Http1Integration")]` (~18 Tests):
  - POST /echo mit "Hello World" (text/plain) → exakter Body-Echo
  - POST /echo mit JSON `{"key":"value"}` (application/json) → Echo + Content-Type erhalten
  - PUT /echo mit Binärdaten (256 Bytes random) → byte-für-byte identisch
  - PATCH /echo mit Text → Echo
  - POST /echo mit leerem Body → leere Response
  - `[Theory]` POST /echo mit 1 KB, 100 KB, 512 KB Body → exakter Echo
  - GET /chunked/10 → 10240 Bytes, chunked dekodiert
  - `[Theory]` GET /chunked/exact/{count}/{bytes} für (3,1024), (10,256), (1,65536)
  - POST /echo/chunked → Request-Body als Chunked-Response
  - GET /chunked/trailer → Response enthält Trailer X-Checksum: abc123
  - GET /chunked/md5 → Content-MD5 Header korrekt
  - GET /large/100 → 102400 Bytes
- [ ] `Http1StreamingTests.cs` mit `[Collection("Http1Integration")]` (~8 Tests):
  - GET /slow/100 → 100 Bytes empfangen
  - GET /slow/1000 → kompletter Stream
  - `[Theory]` GET /large/{kb} für 1, 10, 50, 100, 500
  - GET /delay/100 → erfolgreich nach ~100ms
- [ ] Alle Tests grün: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http1BodyTests|Http1StreamingTests"`

### TASK-003-002: HTTP/2, HTTP/3 & TLS Body Tests
**Description:** Als Entwickler möchte ich Body-Handling Tests für HTTP/2 (Frame-basiert), HTTP/3 (QUIC) und HTTPS, damit protokollspezifische Übertragung verifiziert ist.

**Token Estimate:** ~75k tokens
**Predecessors:** Feature 002 (TASK-002-001)
**Successors:** none
**Parallel:** yes — kann parallel zu TASK-003-001 laufen

**Acceptance Criteria:**
- [ ] `Http2BodyTests.cs` mit `[Collection("Http2Integration")]` (~12 Tests):
  - POST /h2/echo-binary mit 256 Bytes → exakt zurück
  - `[Theory]` POST /h2/echo-binary mit 1 KB, 100 KB, 512 KB
  - POST /echo mit JSON über HTTP/2 → Content-Type erhalten
  - POST /echo mit leerem Body über HTTP/2
  - GET /large/100 über HTTP/2 → 102400 Bytes
  - GET /h2/large-headers/5 → Header + Body kombiniert
  - GET /h2/priority/10 → 10 KB Body
- [ ] `Http3BodyTests.cs` mit `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]` (~8 Tests):
  - POST /h3/echo-binary mit 256 Bytes → exakt zurück
  - `[Theory]` POST /h3/echo-binary mit 1 KB, 100 KB, 512 KB
  - GET /h3/stream/500 → Streaming über QUIC
  - GET /large/50 über HTTP/3
  - POST /echo mit JSON über HTTP/3
- [ ] `TlsBodyTests.cs` mit `[Collection("TlsIntegration")]` (~6 Tests):
  - POST /echo mit Text über HTTPS → Echo
  - POST /echo mit 100 KB über HTTPS
  - GET /large/50 über HTTPS
  - GET /chunked/10 über HTTPS
- [ ] Alle Tests grün: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http2BodyTests|Http3BodyTests|TlsBodyTests"`

## Task Dependency Graph

```
Feature 002 ──→ TASK-003-001
            └──→ TASK-003-002
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-003-001 | ~75k | Feature 002 | yes (with 002) | — |
| TASK-003-002 | ~75k | Feature 002 | yes (with 001) | — |

**Total estimated tokens:** ~150k

## Functional Requirements

- FR-1: Body-Echo muss byte-für-byte identisch sein (kein Encoding-Verlust)
- FR-2: Content-Type Header muss bei Echo-Requests erhalten bleiben
- FR-3: Chunked-Responses müssen transparent dekodiert werden (kein Raw-Chunk-Format sichtbar)
- FR-4: Chunked-Trailers müssen über `HttpResponseMessage.TrailingHeaders` zugänglich sein
- FR-5: Große Bodies (512 KB) dürfen keinen OutOfMemoryError verursachen
- FR-6: Streaming-Responses müssen komplett empfangen werden (kein vorzeitiger Abbruch)

## Non-Goals

- Keine Content-Encoding/Decompression (→ Feature 005)
- Keine Multipart-Form-Data Tests (Route existiert, aber out of scope)
- Keine Upload-Streaming (nur Download-Streaming getestet)

## Technical Considerations

- HTTP/1.0 hat kein Chunked-Encoding — nur Content-Length-Bodies
- HTTP/2 nutzt DATA-Frames statt Chunked — Chunked-Tests nur für HTTP/1.1
- `GET /slow/{count}` schreibt 1 Byte pro 1ms — Tests brauchen ausreichend Timeout
- Binärdaten-Vergleich: `ReadOnlySpan<byte>.SequenceEqual()` für exakten Vergleich
- `[Theory]` mit großen Bodies: Tests können 1-2 Sekunden dauern

## Success Metrics

- Alle 52 Body-Tests grün
- Keine Memory-Leaks bei großen Payloads (kein GC-Druck in Tests)
- Streaming-Tests stabil (kein Timeout bei /slow/1000)

## Open Questions

_Keine — alle Fragen geklärt._
