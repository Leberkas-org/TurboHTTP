# End-to-End Scenarios

Here's what happens when you send a request with different HTTP versions. The details differ, but the pipeline stages are the same — enrichment, cookies, cache, encoding, network, decoding, decompression, cookie storage, cache storage, retries, redirects.

---

## HTTP/1.0 — Simple Request/Response

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp10" />
</ClientOnly>

### Request Path

1. The application calls `SendAsync` on `ITurboHttpClient` with an `HttpRequestMessage` targeting HTTP/1.0.
2. `RequestEnricherStage` applies the base address and any default headers.
3. `CookieBidiStage` injects matching cookies from `CookieJar`.
4. `CacheBidiStage` checks the cache — on a miss, the request continues.
5. `Engine` routes the request to `Http10Engine`.
6. `Http10EncoderStage` serialises the request to bytes with `Connection: close`.
7. `ConnectionStage` acquires a lease from `ConnectionPool.AcquireAsync()`, which provides a fresh TCP connection if available connections are exhausted.
8. Bytes are written to the outbound channel; `ClientByteMover` forwards them to the TCP socket.

### Response Path

9. The server's response bytes arrive via TCP and flow through `ClientByteMover` into the inbound channel.
10. `Http10DecoderStage` parses the HTTP/1.0 response; body length is determined by `Content-Length` or EOF.
11. `Http1XCorrelationStage` matches the response to the pending request.
12. `ContentEncodingBidiStage` decompresses the body if needed.
13. `CookieBidiStage` stores any `Set-Cookie` headers.
14. `CacheBidiStage` caches the response if it is cacheable.
15. `RetryBidiStage` passes the response through (no retry needed for a successful response).
16. `RedirectBidiStage` passes the response through (no redirect needed for a `200`).
17. The final `HttpResponseMessage` is delivered to the application.

### Key Characteristic

After step 17, the TCP connection is closed. The next HTTP/1.0 request will go through the full connection setup again. There is no keep-alive feedback loop.

---

## HTTP/1.1 — Persistent Connection with Keep-Alive

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp11" />
</ClientOnly>

HTTP/1.1 follows the same request/response path as HTTP/1.0 except for one critical difference: the connection can be **reused** after the response is delivered.

### Keep-Alive Branch

After `Http1XCorrelationStage` matches the response, it emits two signals simultaneously:

- **Response** — continues downstream to `DecompressionBidiStage` and the rest of the response chain
- **Keep-alive signal** — sent to `ConnectionReuseStage`

`ConnectionReuseStage` inspects the `Connection` response header:

- `Connection: keep-alive` (or HTTP/1.1 default) → sends a **reuse** signal to `ConnectionStage`
- `Connection: close` → sends a **close** signal to `ConnectionStage`

On **reuse**, `ConnectionStage` returns the lease to `ConnectionPool`, which places it back in the idle queue for the next request to the same host.

On **close**, `ConnectionStage` releases the lease without returning it to the idle queue. The next request will trigger `ConnectionPool.AcquireAsync()` to establish a new connection.

### Pipelining

`Http1XCorrelationStage` uses a FIFO queue to correlate requests with responses, enabling HTTP/1.1 pipelining: multiple requests can be in-flight on the same connection simultaneously, and responses are matched to requests in order.

---

## HTTP/2 — Multiplexed Streams

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp2" />
</ClientOnly>

HTTP/2 is fundamentally different from HTTP/1.x. A single TCP connection carries many concurrent logical **streams**, each identified by an odd integer stream ID assigned by the client.

### Request Framing

1. `Http20ConnectionStage` assigns the next available stream ID (1, 3, 5, …), HPACK-encodes the request headers into a `HEADERS` frame, and serialises the body (if any) into `DATA` frame(s).
2. `Http20ConnectionStage` applies connection-level and stream-level flow control — it will withhold frames if the server's receive window is exhausted.
3. `Http20EncoderStage` serialises each `Http2Frame` to its 9-byte framed wire format; on the first connection it also injects the HTTP/2 connection preface (`PRI * HTTP/2.0…` + initial `SETTINGS`).
4. Frames travel to TCP via `ConnectionStage` and `ClientByteMover`.

### Connection-Level Frames

While request/response streams are active, `Http20ConnectionStage` also handles:

- **`SETTINGS`** — initial and updated connection parameters; acknowledges server `SETTINGS` with `SETTINGS ACK`
- **`PING`** — round-trip latency measurement; responds to server `PING` with `PING ACK`
- **`WINDOW_UPDATE`** — flow control credits; emitted automatically as the consumer reads response data
- **`GOAWAY`** — graceful shutdown; after receiving `GOAWAY`, no new streams are opened on this connection

### Response Assembly

7. Raw bytes from TCP are parsed by `Http20DecoderStage` into `Http2Frame` objects (handles partial frames across TCP boundaries).
8. `Http20ConnectionStage` routes connection-level frames (`SETTINGS`, `PING`, `GOAWAY`) to internal handlers, assembles per-stream `HEADERS` + `DATA` frames into an `HttpResponseMessage`, HPACK-decodes response headers, and correlates each assembled response back to its pending request using the stream ID.
9. The response continues through `ContentEncodingBidiStage`, `CookieBidiStage`, `CacheBidiStage`, `RetryBidiStage`, and `RedirectBidiStage` — the same response chain as HTTP/1.x.

### Stream ID Exhaustion

Client-side stream IDs are 31-bit odd integers. When the maximum (`2^31 - 1`) is reached, the connection sends `GOAWAY` and a new connection is established. This is handled transparently by `HostConnections`.

---

## HTTP/3 — Multiplexed over QUIC

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp3" />
</ClientOnly>

HTTP/3 replaces TCP with **QUIC**, a UDP-based transport that provides built-in encryption and independent stream delivery. Each request uses its own QUIC stream, so a lost packet on one stream does not block other in-flight requests.

### Request Framing

1. `Http30Request2FrameStage` QPACK-encodes the request headers into a `HEADERS` frame, and the body (if any) into `DATA` frame(s).
2. `Http30ConnectionStage` manages connection-level concerns — `SETTINGS`, `GOAWAY`, and stream lifecycle.
3. `Http30EncoderStage` serialises each HTTP/3 frame to wire bytes using QUIC variable-length integer encoding.
4. `Http3ConnectionStage` acquires a QUIC connection from the pool and sends the bytes over the network.

### Connection-Level Frames

While request/response streams are active, `Http30ConnectionStage` handles:

- **`SETTINGS`** — connection parameters exchanged at startup
- **`GOAWAY`** — graceful shutdown; after receiving `GOAWAY`, no new streams are opened on this connection

### Response Assembly

5. Raw bytes from QUIC are parsed by `Http30DecoderStage` into HTTP/3 frame objects.
6. `Http30ConnectionStage` routes connection-level frames to internal handlers and forwards per-stream frames downstream.
7. `Http30StreamStage` groups `HEADERS` and `DATA` frames by stream and assembles them into an `HttpResponseMessage`; QPACK-decodes the response headers.
8. The response continues through `ContentEncodingBidiStage`, `CookieBidiStage`, `CacheBidiStage`, `RetryBidiStage`, and `RedirectBidiStage` — the same response chain as HTTP/1.x and HTTP/2.

### Key Differences from HTTP/2

| | HTTP/2 | HTTP/3 |
|---|--------|--------|
| **Transport** | TCP + TLS | QUIC (UDP + built-in TLS) |
| **Head-of-line blocking** | Yes — one lost TCP packet stalls all streams | No — each QUIC stream is independent |
| **Header compression** | HPACK | QPACK (adapted for out-of-order delivery) |
| **Connection preface** | Required (`PRI * HTTP/2.0...`) | Not needed — QUIC handles this |
