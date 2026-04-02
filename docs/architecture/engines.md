# Protocol Engines

The `Engine` stage demultiplexes requests by HTTP version and routes each request to the appropriate per-version engine. Each engine is a self-contained Akka.Streams sub-graph that handles encoding, decoding, and request/response correlation for its protocol version.

---

## HTTP/1.0 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http10Engine" :height="440" />
</ClientOnly>

HTTP/1.0 uses a **close-then-respond** model. Each connection handles exactly one request, then closes.

**Stage sequence:**

```
Http10EncoderStage → ConnectionStage → TCP → ConnectionStage → Http10DecoderStage → Http1XCorrelationStage
```

| Stage | Role |
|-------|------|
| `Http10EncoderStage` | Serialises `HttpRequestMessage` to wire bytes; sets `Connection: close` |
| `ConnectionStage` | Opens a new TCP connection per request; closes it after the response |
| `Http10DecoderStage` | Parses the HTTP/1.0 response from raw bytes; handles EOF-delimited bodies |
| `Http1XCorrelationStage` | FIFO correlation; since HTTP/1.0 is strictly sequential the queue depth is always 1 |

**Notable behaviours:**

- No keep-alive — every request opens and closes its own TCP connection
- No chunked transfer encoding
- Response body length determined by `Content-Length` header or connection close (EOF)
- Correlation signals are discarded after use; no feedback loop to `ConnectionStage`

---

## HTTP/1.1 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http11Engine" :height="480" />
</ClientOnly>

HTTP/1.1 adds **persistent connections** and **keep-alive control** via a feedback loop from the correlation stage back to `ConnectionStage`.

**Stage sequence:**

```
Http11EncoderStage → ConnectionStage → TCP → ConnectionStage
                                                  ↓
                               Http11DecoderStage → Http1XCorrelationStage
                                                         ↓              ↓
                                              ConnectionReuseStage   (response downstream)
                                                         ↓
                                              ConnectionStage (reuse or close)
```

| Stage | Role |
|-------|------|
| `Http11EncoderStage` | Serialises request; adds `Host`, `Connection`, `Transfer-Encoding: chunked` as needed |
| `ConnectionStage` | Manages a persistent TCP connection; accepts reuse/close signals |
| `Http11DecoderStage` | Parses HTTP/1.1 responses; handles chunked transfer decoding |
| `Http1XCorrelationStage` | FIFO correlation; depth > 1 enables request pipelining |
| `ConnectionReuseStage` | Evaluates `Connection: keep-alive` / `Connection: close`; emits a reuse or close signal |

**Keep-alive feedback loop:**

After decoding each response, `Http1XCorrelationStage` emits two signals in parallel (via `MergePreferred`):
1. The decoded `HttpResponseMessage` continues downstream toward the response chain
2. A keep-alive / close decision is fed back to `ConnectionStage` via `ConnectionReuseStage`

If the decision is **reuse**, `ConnectionStage` keeps the TCP connection open for the next request. If **close**, it signals the I/O actor pool to reconnect.

---

## HTTP/2 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http2Engine" :height="520" />
</ClientOnly>

HTTP/2 provides **stream multiplexing** — many logical requests share a single TCP connection, each assigned a unique stream ID.

**Stage sequence:**

```
Http20ConnectionStage → [frame batch] → Http20EncoderStage → ConnectionStage → TCP
TCP → ConnectionStage → Http20DecoderStage → Http20ConnectionStage
```

| Stage | Role |
|-------|------|
| `Http20ConnectionStage` | Central bidirectional stage: allocates client stream IDs (1, 3, 5, …), HPACK-encodes request headers and emits `HEADERS` + `DATA` frames, handles connection-level frames (`SETTINGS`, `PING`, `WINDOW_UPDATE`, `GOAWAY`), assembles per-stream `HEADERS` + `DATA` frames into `HttpResponseMessage`, and correlates responses back to pending requests by stream ID |
| `Http20EncoderStage` | Serialises `Http2Frame` objects to wire bytes (9-byte frame header + payload); emits the HTTP/2 connection preface (`PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n` + client `SETTINGS`) on its first pull |
| `Http20DecoderStage` | Stateful parser; reassembles frames from TCP byte stream, handles partial frame delivery |
| `ConnectionStage` | TCP transport; shared with HTTP/1.x via the `Engine` demultiplexer |

**HPACK header compression:**

`HpackEncoder` and `HpackDecoder` maintain synchronised dynamic tables. Sensitive headers (`Authorization`, `Cookie`) are automatically marked `NeverIndex`. The dynamic table size is negotiated via `SETTINGS_HEADER_TABLE_SIZE` during the connection preface exchange.

**Flow control:**

`Http20ConnectionStage` tracks both **connection-level** and **stream-level** window sizes. It emits `WINDOW_UPDATE` frames when the consumer reads data, preventing the remote server from stalling. The Akka.Streams backpressure demand signal is translated into HTTP/2 flow-control credits.

---

## HTTP/3 Engine

<ClientOnly>
  <LikeC4Diagram viewId="http3Engine" :height="520" />
</ClientOnly>

HTTP/3 runs over **QUIC** instead of TCP. Each request uses an independent QUIC stream, which eliminates the head-of-line blocking that affects HTTP/2 over a single TCP connection.

**Stage sequence:**

```
Http30Request2FrameStage → Http30ConnectionStage → Http30EncoderStage → Http3ConnectionStage → QUIC
                                     ↑
QUIC → Http3ConnectionStage → Http30DecoderStage → Http30ConnectionStage → Http30StreamStage → (response downstream)
```

| Stage | Role |
|-------|------|
| `Http30Request2FrameStage` | QPACK-encodes request headers; emits `HEADERS` frame + `DATA` frame(s) |
| `Http30ConnectionStage` | Bidirectional connection manager; handles `SETTINGS`, `GOAWAY`, and stream lifecycle |
| `Http30EncoderStage` | Serialises HTTP/3 frames to wire bytes using QUIC variable-length encoding |
| `Http3ConnectionStage` | QUIC transport bridge; acquires a QUIC connection from the pool, writes/reads bytes |
| `Http30DecoderStage` | Parses wire bytes into HTTP/3 frames |
| `Http30StreamStage` | Assembles per-stream `HEADERS` + `DATA` frames into `HttpResponseMessage`; QPACK-decodes headers |

**QPACK header compression:**

QPACK is the HTTP/3 equivalent of HPACK, adapted for QUIC's out-of-order delivery. `QpackEncoder` and `QpackDecoder` maintain synchronised dynamic tables and communicate updates via dedicated encoder/decoder instruction streams.

**No head-of-line blocking:**

Unlike HTTP/2 where a single lost TCP packet can stall all streams, HTTP/3's QUIC transport delivers each stream independently. A lost packet on one stream does not affect other in-flight requests.
