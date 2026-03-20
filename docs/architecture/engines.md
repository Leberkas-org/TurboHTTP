# Protocol Engines

The `Engine` stage demultiplexes requests by HTTP version and routes each request to the appropriate per-version engine. Each engine is a self-contained Akka.Streams sub-graph that handles encoding, decoding, and request/response correlation for its protocol version.

---

## HTTP/1.0 Engine

<LikeC4Diagram viewId="http10Engine" :height="440" />

HTTP/1.0 (RFC 1945) uses a **close-then-respond** model. Each connection handles exactly one request, then closes.

**Stage sequence:**

```
Http10EncoderStage → ConnectionStage → TCP → ConnectionStage → Http10DecoderStage → CorrelationHttp1XStage
```

| Stage | Role |
|-------|------|
| `Http10EncoderStage` | Serialises `HttpRequestMessage` to wire bytes; sets `Connection: close` |
| `ConnectionStage` | Opens a new TCP connection per request; closes it after the response |
| `Http10DecoderStage` | Parses the HTTP/1.0 response from raw bytes; handles EOF-delimited bodies |
| `CorrelationHttp1XStage` | FIFO correlation; since HTTP/1.0 is strictly sequential the queue depth is always 1 |

**Notable behaviours:**

- No keep-alive — every request opens and closes its own TCP connection
- No chunked transfer encoding
- Response body length determined by `Content-Length` header or connection close (EOF)
- Correlation signals are discarded after use; no feedback loop to `ConnectionStage`

---

## HTTP/1.1 Engine

<LikeC4Diagram viewId="http11Engine" :height="480" />

HTTP/1.1 (RFC 9112) adds **persistent connections** and **keep-alive control** via a feedback loop from the correlation stage back to `ConnectionStage`.

**Stage sequence:**

```
Http11EncoderStage → ConnectionStage → TCP → ConnectionStage
                                                  ↓
                               Http11DecoderStage → CorrelationHttp1XStage
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
| `CorrelationHttp1XStage` | FIFO correlation; depth > 1 enables request pipelining |
| `ConnectionReuseStage` | Evaluates `Connection: keep-alive` / `Connection: close` (RFC 9112 §9.3); emits a reuse or close signal |

**Keep-alive feedback loop:**

After decoding each response, `CorrelationHttp1XStage` emits two signals in parallel (via `MergePreferred`):
1. The decoded `HttpResponseMessage` continues downstream toward the response chain
2. A keep-alive / close decision is fed back to `ConnectionStage` via `ConnectionReuseStage`

If the decision is **reuse**, `ConnectionStage` keeps the TCP connection open for the next request. If **close**, it signals the I/O actor pool to reconnect.

---

## HTTP/2 Engine

<LikeC4Diagram viewId="http2Engine" :height="520" />

HTTP/2 (RFC 9113) provides **stream multiplexing** — many logical requests share a single TCP connection, each assigned a unique stream ID.

**Stage sequence:**

```
StreamIdAllocatorStage → Request2FrameStage → Http20ConnectionStage → Http20EncoderStage → PrependPrefaceStage → ConnectionStage → TCP
                                                        ↑
TCP → ConnectionStage → Http20DecoderStage → Http20ConnectionStage → Http20StreamStage → (response downstream)
```

| Stage | Role |
|-------|------|
| `StreamIdAllocatorStage` | Allocates client stream IDs (1, 3, 5, …) per the RFC 9113 §5.1 numbering rule |
| `Request2FrameStage` | HPACK-encodes request headers; emits `HEADERS` frame + `DATA` frame(s) |
| `Http20ConnectionStage` | Bidirectional flow control; handles `SETTINGS`, `PING`, `WINDOW_UPDATE`, `GOAWAY` frames |
| `Http20EncoderStage` | Serialises `Http2Frame` objects to wire bytes (9-byte frame header + payload) |
| `PrependPrefaceStage` | Injects the HTTP/2 connection preface (`PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n` + client `SETTINGS`) on the first connection |
| `ConnectionStage` | TCP transport; shared with HTTP/1.x via the `Engine` demultiplexer |
| `Http20DecoderStage` | Stateful parser; reassembles frames from TCP byte stream, handles partial frame delivery |
| `Http20StreamStage` | Assembles per-stream `HEADERS` + `DATA` frames into `HttpResponseMessage`; HPACK-decodes headers |
| `CorrelationHttp20Stage` | Maps stream IDs back to pending requests; supports concurrent in-flight streams |

**HPACK header compression:**

`HpackEncoder` and `HpackDecoder` maintain synchronised dynamic tables. Sensitive headers (`Authorization`, `Cookie`) are automatically marked `NeverIndex`. The dynamic table size is negotiated via `SETTINGS_HEADER_TABLE_SIZE` during the connection preface exchange.

**Flow control:**

`Http20ConnectionStage` tracks both **connection-level** and **stream-level** window sizes. It emits `WINDOW_UPDATE` frames when the consumer reads data, preventing the remote server from stalling. The Akka.Streams backpressure demand signal is translated into HTTP/2 flow-control credits.
