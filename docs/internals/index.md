# Internals

Dieser Bereich zeigt, **wie TurboHttp intern funktioniert** — erklärt durch interaktive Architektur-Diagramme mit kurzen Erläuterungen.

::: tip
Wenn du TurboHttp nur **verwenden** willst, starte mit dem [Guide](/guide/). Dieser Bereich ist für Contributors und neugierige Engineers.
:::

## Topics

| Topic | Was du lernst |
|-------|---------------|
| [Protocol Layer](./protocol-layer) | Encoder, Decoder, HPACK und Business Logic — alle reinen Protokoll-Komponenten |
| [Memory Management](./memory-management) | Zero-Copy Datenpfad von TCP bis `HttpResponseMessage` |
| [HPACK Compression](./hpack) | HTTP/2 Header-Kompression: Dynamic Tables, Huffman, Sensitive Headers |
| [Testing Architecture](./testing) | 1800+ Tests, RFC-Organisation, Konventionen |
| [RFC Compliance](./rfc-compliance) | Compliance-Matrix aller unterstützten RFCs |
