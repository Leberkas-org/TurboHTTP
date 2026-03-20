# End-to-End Scenarios

These dynamic views trace a single HTTP request from application code through every TurboHttp layer to the remote server and back. Each scenario highlights the protocol-specific stages and interactions.

---

## HTTP/1.0 — Simple Request/Response

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp10" :height="680" />
</ClientOnly>

### Request Path

1. The application calls `SendAsync` on `ITurboHttpClient` with an `HttpRequestMessage` targeting HTTP/1.0.
2. `RequestEnricherStage` applies the base address and any default headers.
3. `CookieInjectionStage` injects matching cookies from `CookieJar`.
4. `CacheLookupStage` checks the cache — on a miss, the request continues.
5. `Engine` routes the request to `Http10Engine`.
6. `Http10EncoderStage` serialises the request to bytes with `Connection: close`.
7. `ConnectionStage` acquires a fresh TCP connection from `HostPoolActor` (via `EnsureHost` → `ConnectionReady`).
8. Bytes are written to the outbound channel; `ClientByteMover` forwards them to the TCP socket.

### Response Path

9. The server's response bytes arrive via TCP and flow through `ClientByteMover` into the inbound channel.
10. `Http10DecoderStage` parses the HTTP/1.0 response; body length is determined by `Content-Length` or EOF.
11. `CorrelationHttp1XStage` matches the response to the pending request.
12. `DecompressionStage` decompresses the body if needed.
13. `CookieStorageStage` stores any `Set-Cookie` headers.
14. `CacheStorageStage` caches the response if it is cacheable.
15. `RetryStage` passes the response through (no retry needed for a successful response).
16. `RedirectStage` passes the response through (no redirect needed for a `200`).
17. The final `HttpResponseMessage` is delivered to the application.

### Key Characteristic

After step 17, the TCP connection is closed. The next HTTP/1.0 request will go through the full connection setup again. There is no keep-alive feedback loop.

---

## HTTP/1.1 — Persistent Connection with Keep-Alive

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp11" :height="720" />
</ClientOnly>

HTTP/1.1 follows the same request/response path as HTTP/1.0 except for one critical difference: the connection can be **reused** after the response is delivered.

### Keep-Alive Branch

After `CorrelationHttp1XStage` matches the response, it emits two signals simultaneously:

- **Response** — continues downstream to `DecompressionStage` and the rest of the response chain
- **Keep-alive signal** — sent to `ConnectionReuseStage`

`ConnectionReuseStage` inspects the `Connection` response header:

- `Connection: keep-alive` (or HTTP/1.1 default) → sends a **reuse** signal to `ConnectionStage`
- `Connection: close` → sends a **close** signal to `ConnectionStage`

On **reuse**, `ConnectionStage` retains the TCP connection in its internal state and uses it for the next request from the pipeline — no actor round-trip, no new TCP handshake.

On **close**, `ConnectionStage` notifies `HostPoolActor`, which schedules a reconnect. The actor handles exponential backoff for flaky connections.

### Pipelining

`CorrelationHttp1XStage` uses a FIFO queue to correlate requests with responses, enabling HTTP/1.1 pipelining: multiple requests can be in-flight on the same connection simultaneously, and responses are matched to requests in order.

---

## HTTP/2 — Multiplexed Streams

<ClientOnly>
  <LikeC4Diagram viewId="scenarioHttp2" :height="740" />
</ClientOnly>

HTTP/2 is fundamentally different from HTTP/1.x. A single TCP connection carries many concurrent logical **streams**, each identified by an odd integer stream ID assigned by the client.

### Request Framing

1. `StreamIdAllocatorStage` assigns the next available stream ID (1, 3, 5, …).
2. `Request2FrameStage` HPACK-encodes the request headers into a `HEADERS` frame, and the body (if any) into `DATA` frame(s).
3. `Http20ConnectionStage` applies connection-level and stream-level flow control — it will withhold frames if the server's receive window is exhausted.
4. `Http20EncoderStage` serialises each `Http2Frame` to its 9-byte framed wire format.
5. `PrependPrefaceStage` injects the HTTP/2 connection preface (`PRI * HTTP/2.0…` + initial `SETTINGS`) on the first connection only.
6. Frames travel to TCP via `ConnectionStage` and `ClientByteMover`.

### Connection-Level Frames

While request/response streams are active, `Http20ConnectionStage` also handles:

- **`SETTINGS`** — initial and updated connection parameters; acknowledges server `SETTINGS` with `SETTINGS ACK`
- **`PING`** — round-trip latency measurement; responds to server `PING` with `PING ACK`
- **`WINDOW_UPDATE`** — flow control credits; emitted automatically as the consumer reads response data
- **`GOAWAY`** — graceful shutdown; after receiving `GOAWAY`, no new streams are opened on this connection

### Response Assembly

7. Raw bytes from TCP are parsed by `Http20DecoderStage` into `Http2Frame` objects (handles partial frames across TCP boundaries).
8. `Http20ConnectionStage` routes connection-level frames (SETTINGS, PING, GOAWAY) to internal handlers and forwards per-stream frames downstream.
9. `Http20StreamStage` groups `HEADERS` and `DATA` frames by stream ID and assembles them into an `HttpResponseMessage`; HPACK-decodes the response headers.
10. `CorrelationHttp20Stage` matches each assembled response back to its pending request using the stream ID.
11. The response continues through `DecompressionStage`, `CookieStorageStage`, `CacheStorageStage`, `RetryStage`, and `RedirectStage` — the same response chain as HTTP/1.x.

### Stream ID Exhaustion

Client-side stream IDs are 31-bit odd integers. When the maximum (`2^31 - 1`) is reached, the connection sends `GOAWAY` and a new connection is established. This is handled transparently by `HostPoolActor`.
