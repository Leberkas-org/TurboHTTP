# Server Request Pipeline

The server request pipeline shows how an incoming request flows through the server — from raw network bytes through protocol decoding to the ASP.NET Core application layer, where middleware, routing, and handlers run.

<ClientOnly>
  <LikeC4Diagram viewId="serverPipeline" :height="500" />
</ClientOnly>

---

## Request Flow

Each connection is bound to a single `ConnectionActor` that owns the entire Akka.Streams graph:

```
Incoming TCP/QUIC Connection
    ↓
[Transport] — TCP or QUIC listener accepts connection (Servus.Akka)
    ↓
[ListenerActor] — spawns ConnectionActor per client
    ↓
[ProtocolRouter] — detects HTTP/1.0, 1.1, 2, or 3 from initial bytes
    ↓
[Http*ServerEngine] — protocol-specific decoder (Http10/11/20/30ServerEngine)
    ↓
[ApplicationBridgeStage] — wraps parsed request as IFeatureCollection
    ↓
╔════════════════════════════════════════════════════════════════╗
║  ASP.NET Core takes over (Middleware → Routing → Handlers)  ║
╚════════════════════════════════════════════════════════════════╝
    ↓
[Protocol Encoder] — encodes response to wire bytes
    ↓
Outgoing TCP/QUIC Bytes
```

---

## Pipeline Stages

| Stage                        | Role                                                                                                                                      |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `Transport` (TCP/QUIC)       | Accepts incoming connections over TCP or QUIC (via Servus.Akka.Transport)                                                              |
| `ListenerActor`              | Binds to a port and spawns a `ConnectionActor` for each incoming connection                                                             |
| `ProtocolRouter`             | Inspects initial bytes to detect HTTP version; routes to appropriate server engine                                                    |
| `Http*ServerEngine`          | Protocol-specific state machine: parses request bytes, manages connection/stream-level flow control, encodes response frames            |
| `ApplicationBridgeStage`      | Wraps the parsed protocol request as an `IFeatureCollection` (standard ASP.NET Core `HttpContext`); then ASP.NET Core takes over    |
| **(ASP.NET Core)**           | **Middleware** (app.Use/UseMiddleware) → **Routing** (endpoint routing) → **Model Binding** → **Handler Execution** (Minimal APIs, Controllers, etc.) |

---

## Connection Lifecycle

Each connection is managed by a dedicated `ConnectionActor`:

1. **Bind** — `ListenerActor` binds to a TCP or QUIC port
2. **Accept** — When a client connects, `ListenerActor` spawns a new `ConnectionActor` for that connection
3. **Materialize** — `ConnectionActor` materialises the Akka.Streams graph (protocol engine → middleware → routing → dispatcher)
4. **Process** — The graph processes requests and generates responses for the lifetime of the connection
5. **Cleanup** — When the client disconnects (or after idle timeout), the actor terminates and releases resources

---

## Response Flow

After the handler returns a response, it flows back through the pipeline:

1. ASP.NET Core populates the `IHttpResponseFeature` (status code, headers, response body stream)
2. The protocol engine encodes the response to wire bytes using the appropriate HTTP version (1.0, 1.1, 2, or 3)
3. The transport layer (via `ConnectionActor` and Servus.Akka.Transport) sends the bytes to the client
4. For HTTP/1.1+, the connection can remain open and reuse for the next request; for HTTP/1.0, the connection closes after sending the response

---

## Protocol Detection

When a new connection arrives, `ProtocolRouter` inspects the initial bytes to determine which server engine to use:

- **HTTP/1.x** — First line is `METHOD /path HTTP/1.x` (ASCII text)
- **HTTP/2** — First bytes are the HTTP/2 connection preface (`PRI * HTTP/2.0`) or `SETTINGS` frame
- **HTTP/3** — Connection arrives over QUIC (UDP-based transport)

With TLS (HTTPS), ALPN negotiation happens during the TLS handshake:
- `h2` → HTTP/2
- `h3` → HTTP/3
- `http/1.1` or `http/1.0` → HTTP/1.1 (fallback)

For plaintext connections, the router auto-detects from the initial bytes.

---

## ASP.NET Core Integration

After `ApplicationBridgeStage` creates the `TContext` from the `IFeatureCollection`, ASP.NET Core's standard middleware pipeline takes over — routing, model binding, authentication, and handler execution are all handled by ASP.NET Core, not by TurboHTTP.

---

## Related Guides

- [ASP.NET Core Integration](/server/aspnet-core) — middleware, routing, and request handling
- [Hosting & Lifecycle](/server/hosting) — actor hierarchy and graceful shutdown
