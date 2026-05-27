# Server Request Pipeline

The server request pipeline shows how an incoming request flows through the server — from raw network bytes through protocol decoding, middleware, routing, and finally to your handler or actor.

<ClientOnly>
  <LikeC4Diagram viewId="serverPipeline" :height="500" />
</ClientOnly>

---

## Request Flow

Each connection is bound to a single `ConnectionActor` that owns the entire Akka.Streams graph:

```
Incoming TCP/QUIC Connection
    ↓
[Transport] — TCP or QUIC listener accepts connection
    ↓
[ProtocolRouter] — detects HTTP/1.0, 1.1, 2, or 3 from initial bytes
    ↓
[Protocol Decoder] — Http10/11/20/30ServerEngine decodes request
    ↓
[ApplicationBridgeStage] — wraps parsed request as IFeatureCollection (HttpContext)
    ↓
[Middleware] — runs registered middleware (Use/Run/Map/MapWhen)
    ↓
[Routing] — matches request path to registered route pattern
    ↓
[Dispatcher] — delegates to handler function or actor
    ↓
[Parameter Binding] — binds route values, query, body, headers to handler parameters
    ↓
[Handler / Entity Actor] — executes your code
    ↓
[Protocol Encoder] — encodes response to wire bytes
    ↓
Outgoing TCP/QUIC Bytes
```

---

## Pipeline Stages

| Stage                        | Role                                                                                                                                      |
| ---------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `Transport` (TCP/QUIC)       | `ListenerActor` binds transport, accepts incoming connections, spawns `ConnectionActor` per client                                      |
| `ProtocolRouter`             | Inspects initial bytes to detect HTTP version; routes to appropriate server engine state machine                                        |
| `Http*ServerEngine`          | Protocol-specific state machine: parses request bytes, manages connection/stream-level flow control, encodes response frames            |
| `ApplicationBridgeStage`      | Wraps the parsed protocol request as an `IFeatureCollection` (standard ASP.NET Core `HttpContext`)                                   |
| Middleware                   | Runs all registered middleware in order (outermost-first for request, innermost-first for response). Middleware can short-circuit.     |
| Routing                      | Matches the request path against registered route patterns; extracts route parameters (`{id}`, etc.) into route values              |
| Dispatcher                   | Selects and invokes the handler: function-based routes or actor-based routes                                                          |
| `ParameterBindingStage`      | (within dispatcher) Binds route parameters, query string, body, and headers to handler parameters using reflection and model binding  |

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

After the handler returns a response, the response object flows back through the pipeline in reverse:

1. The protocol engine encodes the `TurboHttpResponse` to wire bytes
2. The transport layer (`TcpConnectionStage` or `QuicConnectionStage`) sends the bytes to the client
3. For HTTP/1.1+, the connection can remain open and reuse for the next request
4. For HTTP/1.0, the connection closes after sending the response

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

## Middleware Pipeline Semantics

Middleware runs in **outermost-first order** for requests:

```csharp
app.UseTurbo<ILogger>()          // runs 3rd on request, 1st on response
    .UseTurbo<IAuth>()           // runs 2nd on request, 2nd on response
    .UseTurbo<IMetrics>();      // runs 1st on request, 3rd on response
    // Handler/Router below
```

Each middleware can:
- **Transform the request** — modify headers, body, or context
- **Short-circuit the chain** — return a response without calling `next(ctx)`, skipping downstream middleware and the handler
- **Transform the response** — modify status code, headers, or body
- **Observe execution** — wrap the downstream call to measure timing or log

::: tip
Unlike ASP.NET Core where middleware is registered in reverse order, TurboHTTP middleware is registered and executes in the order you call `UseTurbo<T>()`. This is more intuitive for declarative server configuration.
:::

---

## ASP.NET Core Integration

After `ApplicationBridgeStage` creates the `TContext` from the `IFeatureCollection`, ASP.NET Core's standard middleware pipeline takes over — routing, model binding, authentication, and handler execution are all handled by ASP.NET Core, not by TurboHTTP.

---

## Related Guides

- [ASP.NET Core Integration](/server/aspnet-core) — middleware, routing, and request handling
- [Hosting & Lifecycle](/server/hosting) — actor hierarchy and graceful shutdown
