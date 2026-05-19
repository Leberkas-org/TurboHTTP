---
layout: home

hero:
  name: TurboHTTP
  text: High-Performance HTTP Client & Server for .NET
  tagline: Built on Akka.Streams — HTTP/1.0 through HTTP/3 with automatic retries, caching, cookies, middleware pipeline, routing, and entity gateway.
  image:
    src: /logo/logo.png
    alt: TurboHTTP
  actions:
    - theme: brand
      text: Quick Guide
      link: /quickstart/
    - theme: alt
      text: Client Guide
      link: /client/
    - theme: alt
      text: Server Guide
      link: /server/
    - theme: alt
      text: Architecture
      link: /architecture/

features:
  - icon: ⚡
    title: HTTP/1.0, HTTP/1.1, HTTP/2 & HTTP/3
    details: Automatic version negotiation, HPACK/QPACK compression, flow control, and multiplexed streams over TCP and QUIC. One library handles all versions — client and server.

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
    details: Span<T>, Memory<byte>, and pooled buffers throughout. Stateful decoders, zero GC pressure on the hot path.

  - icon: 🔧
    title: Middleware Pipeline
    details: ASP.NET Core-style middleware with Use, Run, Map, and MapWhen. Compose request processing from reusable components.

  - icon: 🗺️
    title: Routing & Entity Gateway
    details: Minimal API-style route registration with MapGet/Post/Put/Delete. Route directly to Akka.NET actors for stateful entity handling.

  - icon: 🏗️
    title: Actor Lifecycle
    details: Supervisor → Listener → Connection actor hierarchy with graceful shutdown, drain phases, and coordinated termination.

  - icon: 🔒
    title: Kestrel Integration
    details: Runs alongside ASP.NET Core via AddTurboKestrel. Full HTTPS support with certificate configuration and protocol negotiation.
---
