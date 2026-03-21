# Testing Architecture

TurboHttp hat **1800+ Unit Tests** und **500+ Stream Tests**, organisiert nach RFC-Spezifikation.

## Test-Projekte

| Projekt | Zweck | Tests |
|---------|-------|-------|
| `TurboHttp.Tests` | Unit Tests — Protokolllogik isoliert | ~1835 |
| `TurboHttp.StreamTests` | Akka.Streams Stage-Tests | ~500 |
| `TurboHttp.IntegrationTests` | End-to-End mit Kestrel | Infrastruktur ready |

## Organisation nach RFC

| Ordner | RFC | Tests |
|--------|-----|-------|
| `RFC1945/` | HTTP/1.0 | ~233 |
| `RFC9112/` | HTTP/1.1 | ~374 |
| `RFC9113/` | HTTP/2 | ~545 |
| `RFC7541/` | HPACK | ~419 |
| `RFC9110/` | HTTP Semantics | ~123 |
| `RFC9111/` | Caching | ~75 |
| `RFC6265/` | Cookies | ~66 |

## Konventionen

- **Datei:** `NN_ThemaTests.cs` (z.B. `07_ChunkedEncodingTests.cs`)
- **Klasse:** `public sealed class`, Namespace = RFC-Ordner
- **DisplayName:** `RFC-{rfc}-sec{section}-{cat}-{nr}: Beschreibung`
- **`[Fact]`** für Einzelfälle, **`[Theory]` + `[InlineData]`** für parametrisierte Tests

## Tests ausführen

```bash
# Alle Tests
dotnet test src/TurboHttp.sln

# Bestimmter RFC
dotnet test --filter "FullyQualifiedName~RFC9113"

# Bestimmte Klasse
dotnet test --filter "FullyQualifiedName~Http2FrameSerializationTests"
```

## Stream Tests

Prüfen **Graph-Level-Verhalten**: Stage-Komposition, Backpressure, TCP-Fragmentierung, Feedback-Loops, Fehler-Propagation. Basis-Klassen: `StreamTestBase` und `EngineTestBase`.
