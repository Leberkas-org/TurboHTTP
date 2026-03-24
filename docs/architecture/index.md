# How It Works

TurboHttp handles HTTP requests through a simple pipeline. You send a request, and the library automatically handles cookies, caching, retries, redirects, and connection reuse — all transparently.

<ClientOnly>
  <LikeC4Diagram viewId="index" :height="300" />
</ClientOnly>

## The Request Pipeline

When you call `SendAsync()`, your request passes through a series of processing stages:

```
Your Request
    ↓
[Enricher] — applies default headers, base address
    ↓
[Cookies] — injects matching cookies from the cookie jar
    ↓
[Cache] — checks if response is cached; returns immediately if fresh
    ↓
[Protocol Encoder] — converts to HTTP/1.0, 1.1, 2, or 3 bytes
    ↓
[Network] — sends over TCP or QUIC
    ↓
[Protocol Decoder] — parses response bytes
    ↓
[Decompression] — decompresses gzip/deflate/brotli
    ↓
[Cookies] — stores Set-Cookie headers
    ↓
[Cache] — caches the response if cacheable
    ↓
[Retry] — re-sends on transient errors or 503/429
    ↓
[Redirects] — follows 301-308 automatically
    ↓
Your Response
```

Each stage does one thing well. Most of the time you don't think about them — they just work.

## Key Characteristics

- **Automatic**: Cookies, caching, retries, redirects all work out of the box
- **Efficient**: HTTP/2 and HTTP/3 multiplexing, keep-alive connection reuse, lock-free data movement
- **Correct**: Follows HTTP specifications for freshness, method rewriting, retry idempotency
- **Observable**: See exactly what happens at each stage

## Learn More

- [**Pipeline Details**](./pipeline) — All stages and how they interact
- [**Scenarios**](./scenarios) — End-to-end walkthroughs for HTTP/1.0, 1.1, 2, and 3
- [**Connection Pooling**](../guide/connection-pooling) — How connections are reused