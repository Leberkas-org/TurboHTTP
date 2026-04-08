---
title: Layers Index
description: 'Index of per-layer architecture notes — client, transport, streams, protocol'
tags:
  - architecture
  - layers
  - index
---
# Layers

Per-layer deep dives into each architectural layer of TurboHTTP.

## Notes

- [[Architecture/Layers/13-CLIENT_LAYER|Client Layer]] — Public API surface, factory pattern, DI integration, and request lifecycle
- [[Architecture/Layers/14-TRANSPORT_LAYER|Transport Layer]] — Actor-free connection pool, Channels-based I/O, TCP/TLS/QUIC transport, and backpressure model
- [[Architecture/Layers/15-STREAMS_LAYER|Streams Layer]] — Akka.Streams pipeline architecture — stage categories, BidiFlow stacking, version demux
- [[Architecture/Layers/16-PROTOCOL_LAYER|Protocol Layer Architecture]] — Encoder/decoder patterns, HPACK/QPACK internals, RFC subfolder structure, and wire-format handling
