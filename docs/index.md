---
layout: home

hero:
  name: TurboHTTP
  text: High-Performance HTTP Client for .NET
  tagline: Built on Akka.Streams — automatic retries, caching, cookies, and HTTP/2 & HTTP/3 multiplexing out of the box.
  image:
    src: /logo/logo.png
    alt: TurboHTTP
  actions:
    - theme: brand
      text: Get Started
      link: /guide/
    - theme: alt
      text: Architecture
      link: /architecture/
    - theme: alt
      text: GitHub
      link: https://github.com/st0o0/TurboHTTP

features:
  - icon: ⚡
    title: HTTP/1.0, HTTP/1.1, HTTP/2 & HTTP/3
    details: Automatic version negotiation, HPACK/QPACK compression, flow control, and multiplexed streams over TCP and QUIC. One client handles all versions.

  - icon: 🔄
    title: Automatic Retries
    details: Smart retry with idempotency detection — GET, PUT, DELETE are retried automatically. Respects Retry-After headers. POST is never retried.

  - icon: 📦
    title: Built-in Caching
    details: In-memory LRU cache with Vary support, conditional requests (ETag, Last-Modified), and freshness evaluation. Zero config needed.

  - icon: 🔀
    title: Redirect Following
    details: Automatic redirect chain following with method rewriting (301/302 → GET), body preservation (307/308), loop detection, and cross-origin safety.

  - icon: 🍪
    title: Cookie Management
    details: Automatic cookie storage and injection. Domain/path matching, Secure/HttpOnly/SameSite attributes, expiration handling.

  - icon: 🔗
    title: Connection Pooling
    details: Per-host connection pools with automatic reconnect, idle eviction, and configurable concurrency limits.

  - icon: 🗜️
    title: Content Encoding
    details: Automatic gzip, deflate, and Brotli decompression. Server-driven content negotiation via Accept-Encoding.

  - icon: 🚀
    title: Zero-Allocation Internals
    details: Span<T>, Memory<byte>, and IBufferWriter throughout. Pooled buffers, stateful decoders, zero GC pressure on the hot path.
---
