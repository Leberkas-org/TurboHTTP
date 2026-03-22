# Feature 002: Integration Tests — Foundation & Basic Request/Response

## Introduction

Die Integration-Test-Infrastruktur (4 Kestrel-Fixtures, 93 Routes, TestKit-Basis) existiert, aber es gibt **null Test-Klassen**. Dieses Feature baut die gemeinsame Test-Infrastruktur auf und verifiziert grundlegende Request/Response-Funktionalität für alle 4 HTTP-Versionen (1.0/1.1, 2.0, 3.0) plus TLS.

### Architecture Context

- **Components involved:**
  - `src/TurboHttp.IntegrationTests/Shared/` — Fixtures (KestrelFixture, KestrelH2Fixture, KestrelH3Fixture, KestrelTlsFixture), TestKit, Routes
  - `src/TurboHttp/Client/ITurboHttpClient.cs` — SendAsync API
  - `src/TurboHttp/Hosting/TurboClientServiceCollectionExtensions.cs` — DI-Registration
  - `src/TurboHttp/Middleware/TurboHttpClientBuilderExtensions.cs` — Builder-Pattern
- **New patterns:** xUnit Collection Definitions für Fixture-Sharing, DI-basierter `ClientHelper` für konsistentes Client-Setup
- **No architecture changes required**

## Goals

- xUnit Collection Definitions für alle 4 Fixtures erstellen
- DI-basierten `ClientHelper` implementieren (wie Endnutzer den Client verwenden)
- Grundlegende GET/POST/PUT/DELETE/PATCH/HEAD Tests für HTTP/1.x, HTTP/2, HTTP/3 und HTTPS
- Status-Codes, Header-Echo, Content-Types, leere Bodies verifizieren
- HTTP/3 Tests mit `[Trait("Category", "Http3")]` filterbar machen

## Tasks

### TASK-002-001: Shared Test Infrastructure
**Description:** Als Entwickler möchte ich wiederverwendbare Test-Infrastruktur (Collections, ClientHelper), damit alle folgenden Integration-Tests eine konsistente Basis haben.

**Token Estimate:** ~75k tokens
**Predecessors:** none
**Successors:** TASK-002-002, TASK-002-003
**Parallel:** no — alle weiteren Tasks hängen davon ab

**Acceptance Criteria:**
- [ ] `Shared/Collections.cs` mit 4 Collection Definitions:
  - `[CollectionDefinition("Http1Integration")] public sealed class Http1IntegrationCollection : ICollectionFixture<KestrelFixture>`
  - `[CollectionDefinition("Http2Integration")] public sealed class Http2IntegrationCollection : ICollectionFixture<KestrelH2Fixture>`
  - `[CollectionDefinition("Http3Integration")] public sealed class Http3IntegrationCollection : ICollectionFixture<KestrelH3Fixture>`
  - `[CollectionDefinition("TlsIntegration")] public sealed class TlsIntegrationCollection : ICollectionFixture<KestrelTlsFixture>`
- [ ] `Shared/ClientHelper.cs` mit statischer Factory-Methode:
  - `CreateClient(int port, Version version, Action<ITurboHttpClientBuilder>? configure = null)` → `ITurboHttpClient`
  - Intern: `ServiceCollection` + `AddTurboHttpClient` + `TurboClientOptions` mit `DangerousAcceptAnyServerCertificate = true`
  - Erstellt eigenes `ActorSystem` pro Client (oder shared per Test-Klasse)
  - Implementiert `IAsyncDisposable` für Cleanup (ActorSystem-Shutdown)
- [ ] Lösung baut fehlerfrei: `dotnet build src/TurboHttp.IntegrationTests/`
- [ ] Mindestens 1 Smoke-Test (GET /hello über HTTP/1.1) beweist, dass die Infrastruktur funktioniert

### TASK-002-002: Basic Tests für HTTP/1.x und TLS
**Description:** Als Entwickler möchte ich grundlegende Request/Response-Tests für HTTP/1.0, HTTP/1.1 und HTTPS, damit die Basis-Funktionalität per HTTP-Version verifiziert ist.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-002-001
**Successors:** none
**Parallel:** yes — kann parallel zu TASK-002-003 laufen

**Acceptance Criteria:**
- [ ] `Http1BasicTests.cs` mit `[Collection("Http1Integration")]` (~16 Tests):
  - GET /hello → 200 "Hello World"
  - HEAD /hello → 200, leerer Body
  - GET /ping → 200 "pong"
  - `[Theory]` GET /status/{code} für 200, 201, 204, 301, 400, 404, 500, 503
  - GET /headers/echo mit X-Custom-Header → Echo zurück
  - GET /headers/set?X-Foo=bar → Response-Header gesetzt
  - GET /multiheader → zwei X-Value Header (alpha, beta)
  - GET /empty-cl → Content-Length: 0, leerer Body
  - GET /edge/empty-body → 200 ohne Content-Length
  - GET /unknown-headers → X-Unknown-Foo/Bar vorhanden
  - POST /echo "Hello" → Body-Echo
  - PUT /echo "World" → Body-Echo
  - PATCH /echo "!" → Body-Echo
  - GET /large/10 → 10240 Bytes korrekt empfangen
  - `[Theory]` /any mit GET, POST, PUT, DELETE, PATCH → Method-Echo
- [ ] `TlsBasicTests.cs` mit `[Collection("TlsIntegration")]` (~10 Tests):
  - GET /hello via HTTPS → 200
  - POST /echo via HTTPS → Body-Echo
  - HEAD /hello via HTTPS → 200, kein Body
  - `[Theory]` GET /status/{code} für 200, 400, 500 via HTTPS
  - GET /headers/echo via HTTPS
  - GET /large/10 via HTTPS
- [ ] Alle Tests grün: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http1BasicTests|TlsBasicTests"`

### TASK-002-003: Basic Tests für HTTP/2 und HTTP/3
**Description:** Als Entwickler möchte ich grundlegende Request/Response-Tests für HTTP/2 (h2c) und HTTP/3 (QUIC), damit die Multiplexing-Protokolle End-to-End verifiziert sind.

**Token Estimate:** ~80k tokens
**Predecessors:** TASK-002-001
**Successors:** none
**Parallel:** yes — kann parallel zu TASK-002-002 laufen

**Acceptance Criteria:**
- [ ] `Http2BasicTests.cs` mit `[Collection("Http2Integration")]` (~14 Tests):
  - GET /hello → 200 "Hello World"
  - HEAD /hello → 200, kein Body
  - `[Theory]` GET /status/{code} für 200, 201, 204, 400, 500
  - POST /echo → Body-Echo
  - GET /headers/echo mit X-Custom → Echo
  - GET /multiheader → zwei Header-Werte
  - GET /empty-cl → leerer Body
  - GET /large/10 → 10 KB
  - GET /h2/settings → "h2-ok"
  - GET /h2/many-headers → 20 Custom-Header dekodiert
  - POST /h2/echo-binary → Binärdaten exakt zurück
  - GET /h2/echo-path → :path Pseudo-Header im Body
- [ ] `Http3BasicTests.cs` mit `[Collection("Http3Integration")]` + `[Trait("Category", "Http3")]` (~12 Tests):
  - GET /hello → 200 "Hello World"
  - POST /echo → Body-Echo
  - `[Theory]` GET /status/{code} für 200, 400, 500
  - GET /headers/echo
  - GET /large/10
  - GET /h3/protocol → "HTTP/3" im Body
  - GET /h3/settings → "h3-ok" + X-Protocol Header
  - POST /h3/echo-binary → Binärdaten über QUIC
  - GET /h3/many-headers → 20 Custom-Header
- [ ] Alle Tests grün: `dotnet test src/TurboHttp.IntegrationTests/ --filter "Http2BasicTests|Http3BasicTests"`

## Task Dependency Graph

```
TASK-002-001 ──→ TASK-002-002
             └──→ TASK-002-003
```

| Task | Estimate | Predecessors | Parallel | Model |
|------|----------|--------------|----------|-------|
| TASK-002-001 | ~75k | none | no | — |
| TASK-002-002 | ~80k | 001 | yes (with 003) | — |
| TASK-002-003 | ~80k | 001 | yes (with 002) | — |

**Total estimated tokens:** ~235k

## Functional Requirements

- FR-1: Jede Test-Klasse muss über xUnit Collection eine Fixture teilen (kein Server-Neustart pro Test)
- FR-2: Client-Erstellung muss via DI erfolgen (`AddTurboHttpClient` + `ITurboHttpClientFactory`)
- FR-3: HTTP/3 Tests müssen `[Trait("Category", "Http3")]` tragen für Filterung auf nicht-QUIC-Systemen
- FR-4: Jeder Test muss `CancellationToken` mit Timeout verwenden (kein Hängenbleiben)
- FR-5: Client-Cleanup (ActorSystem-Shutdown) muss in `IAsyncLifetime.DisposeAsync()` passieren
- FR-6: Tests müssen mit `dotnet test --filter "Category!=Http3"` HTTP/3 ausschließen können

## Non-Goals

- Keine Body-Handling-Tests (→ Feature 003)
- Keine Redirect/Retry-Tests (→ Feature 004)
- Keine Cookie/Cache-Tests (→ Feature 005)
- Keine Edge-Case-Tests (→ Feature 006)
- Keine Performance-Benchmarks

## Technical Considerations

- `KestrelFixture` nutzt `PortFinder.FindFreeLocalPort()` — Tests brauchen keine festen Ports
- `KestrelH3Fixture` braucht Windows 11+ oder Linux mit libmsquic — daher `[Trait]` zum Filtern
- `DangerousAcceptAnyServerCertificate = true` für TLS/QUIC-Fixtures (Self-Signed Certs)
- xUnit parallelizeTestCollections=false in `xunit.runner.json` — Tests laufen seriell

## Success Metrics

- Alle 52 Tests grün auf Windows 11 (inkl. HTTP/3)
- Alle ~40 Tests (ohne HTTP/3) grün auf Systemen ohne QUIC
- Client-Setup + Teardown unter 5 Sekunden pro Test-Klasse
- Keine flaky Tests nach 10 wiederholten Runs

## Open Questions

_Keine — alle Fragen geklärt._
