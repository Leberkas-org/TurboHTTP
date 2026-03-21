# Protocol Layer

Die Protocol Layer enthält alle reinen HTTP-Komponenten — Encoder, Decoder, HPACK und Business Logic. **Kein I/O, keine Akka-Abhängigkeiten** — alles isoliert testbar.

<ClientOnly>
  <LikeC4Diagram viewId="protocolLayer" :height="600" />
</ClientOnly>

## Aufbau nach RFC

| Ordner | RFC | Inhalt |
|--------|-----|--------|
| `RFC1945/` | HTTP/1.0 | Encoder, Decoder |
| `RFC9112/` | HTTP/1.1 | Encoder, Decoder, Connection Reuse |
| `RFC9113/` | HTTP/2 | Frames, Encoder, Decoder |
| `RFC7541/` | HPACK | Header Compression |
| `RFC9110/` | HTTP Semantics | Redirects, Retries, Content Encoding |
| `RFC9111/` | Caching | Freshness, Validation, Storage |
| `RFC6265/` | Cookies | Domain/Path Matching, Attributes |

## Stage-Protocol Delegation

Jede Akka.Streams Stage delegiert an eine reine Protocol-Komponente. Die Stage handhabt Backpressure und Flow Control — das Protocol-Modul handhabt Encoding/Decoding.

<ClientOnly>
  <LikeC4Diagram viewId="stageDelegation" :height="700" />
</ClientOnly>

**Farbcode:** Grün = Encoder-Stages, Blau = Decoder-Stages, Amber = Business-Logic-Stages.

## Encoder-Muster

Alle Encoder sind **stateless** und schreiben in `IBufferWriter<byte>`:

```csharp
Http11Encoder.Encode(request, output); // Caller kontrolliert Speicher
```

## Decoder-Muster

Alle Decoder sind **stateful** — sie puffern unvollständige TCP-Daten in `_remainder`:

```csharp
var events = pipeline.Process(tcpBytes); // Gibt Events zurück, behält Rest
```

Drei Schichten pro Protokoll: `DecoderPipeline` → `EventAggregator` → `CompletionDecoder`.

## Fehlerbehandlung

| Exception | Kontext |
|-----------|---------|
| `HttpDecoderException` | Fehlerhafte HTTP-Nachrichten |
| `Http2Exception` | HTTP/2 Protokollfehler |
| `HpackException` | HPACK Violations |
| `RedirectException` | Redirect-Loop, Max exceeded |
