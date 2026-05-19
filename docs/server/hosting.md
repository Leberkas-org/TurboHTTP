# Hosting & Lifecycle

TurboHTTP Server manages connection lifetime through an actor hierarchy. When you start the server, it creates a supervisor that tracks listeners (one per endpoint) and connections, handles graceful shutdown, and ensures in-flight requests complete before the application exits.

## How the Server Starts

When your ASP.NET Core application starts with TurboHTTP Server configured, the hosting layer follows this sequence:

1. **ActorSystem**: Creates or reuses an Akka.NET ActorSystem (or reuses one from the DI container if already present)
2. **Materializer**: Creates a Streams materializer for the system
3. **ServerSupervisorActor**: Creates the top-level supervisor responsible for the entire server
4. **ListenerActors**: For each configured endpoint, creates a listener that binds the transport (TCP or QUIC)
5. **Coordinated Shutdown**: Hooks into Akka's shutdown lifecycle to ensure graceful termination

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);  // Creates one listener
    options.ListenLocalhost(5101);  // Creates another listener
});

var app = builder.Build();
// ... middleware and routes ...
await app.RunAsync();  // Blocks until shutdown signal
```

When `app.RunAsync()` is called, the TurboServerHostedService:
- Initializes the actor system and materializer
- Creates the ServerSupervisorActor
- Creates a ListenerActor for each endpoint
- Registers shutdown hooks with Akka Coordinated Shutdown

## Actor Hierarchy

<ClientOnly>
  <LikeC4Diagram viewId="serverHierarchy" :height="400" />
</ClientOnly>

TurboHTTP Server uses this actor structure:

```
ActorSystem (turbo-server)
  ├── ServerSupervisorActor
  │     ├── ListenerActor (endpoint 127.0.0.1:5100)
  │     │     ├── ConnectionActor (active connection 1)
  │     │     ├── ConnectionActor (active connection 2)
  │     │     └── ...
  │     └── ListenerActor (endpoint 127.0.0.1:5101)
  │           ├── ConnectionActor (active connection 3)
  │           └── ...
```

### ServerSupervisorActor

The supervisor watches over the entire server. It:
- Starts all configured listeners
- Tracks active connections globally
- Coordinates the shutdown sequence
- Logs connection counts and lifecycle events

When shutdown begins, the supervisor tells all listeners to stop accepting new connections, then drains active connections with a timeout.

### ListenerActor

Each endpoint has one listener. It:
- Binds the transport (TCP port or QUIC/UDP port)
- Accepts incoming connections
- Creates a ConnectionActor for each new connection
- Enforces MaxConcurrentConnections limit (when configured)

When a connection arrives, the listener materializes the full HTTP processing pipeline into a new actor and tells it to run.

### ConnectionActor

Each active connection runs in a ConnectionActor. It:
- Materializes the complete Akka.Streams graph:
  - Transport inbound/outbound flow
  - Protocol engine (HTTP/1.0, 1.1, 2, or 3)
  - Request/response handling
  - Middleware pipeline
  - Routing and handler execution
- Holds a kill switch to stop processing cleanly
- Reports completion (success, error, or shutdown) back to the supervisor

Once the handler completes or the connection closes, the ConnectionActor terminates and reports the completion reason.

## Connection Lifecycle

From the moment a client connects until it closes, here's what happens:

1. **Connection arrives**: ListenerActor receives an incoming connection from the transport
2. **ConnectionActor spawned**: A new actor is created for this connection, watched by the listener
3. **Pipeline materialized**: The full Akka.Streams graph is wired up:
   - Protocol engine decodes transport bytes into HTTP requests
   - Middleware processes each request
   - Router finds and executes the handler
   - Response is encoded back to bytes and sent
4. **Request loop**: The connection waits for the next request (keep-alive) or closes
5. **Completion**: When the connection closes (client disconnect, keep-alive timeout, error):
   - ConnectionActor reports completion reason to supervisor
   - Actor terminates
   - Resources are cleaned up

::: tip Keep-Alive Behavior
HTTP/1.1 connections reuse the same ConnectionActor for multiple requests. Each request flows through the pipeline independently, but the TCP/TLS connection and actor stay alive. HTTP/2 and 3 multiplex streams within one connection, all handled by the same actor.
:::

## Graceful Shutdown

When your application receives a shutdown signal (SIGTERM, Ctrl+C, or explicit `StopAsync`), the server enters graceful shutdown mode. This ensures all in-flight requests finish cleanly:

1. **Shutdown signal received**: Your application calls `await app.StopAsync()` or the OS sends SIGTERM
2. **Coordinated Shutdown phase 1 — BeforeServiceUnbind**:
   - ServerSupervisorActor receives `StopAccepting` message
   - All ListenerActors stop accepting new connections
   - Already-connected clients can still send requests
3. **Coordinated Shutdown phase 2 — ServiceUnbind**:
   - ServerSupervisorActor receives `BeginDrain` message
   - All ConnectionActors receive `GracefulStop` with a timeout value
   - Each connection cancels its pipeline (sends back `HTTP/1.1 503 Service Unavailable` or TCP RST for HTTP/2)
   - In-flight requests are interrupted
4. **Drain wait**: The application waits for up to `GracefulShutdownTimeout` (default 30 seconds)
   - Connections finish their active work and close
5. **Force close**: After the timeout expires:
   - Any remaining connections are killed
   - The ActorSystem shuts down
   - The application exits

::: warning GracefulShutdownTimeout
If a request handler is blocked indefinitely (e.g., waiting on unresponsive I/O), the connection will be forcefully closed after `GracefulShutdownTimeout` expires. Plan your timeout accordingly:
- **Short timeouts (5-10 seconds)**: Suitable for APIs with quick handlers
- **Medium timeouts (30 seconds, default)**: Works for most web applications
- **Long timeouts (60+ seconds)**: Use only if some handlers legitimately take a long time

Set the timeout in configuration:
```csharp
builder.Services.AddTurboKestrel(options =>
{
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(60);
});
```
:::

## Configuration

Key options control server and connection behavior:

### Connection Limits

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // Limit concurrent connections (0 = unlimited)
    options.MaxConcurrentConnections = 1000;
    
    // Limit concurrent HTTP/2 streams per connection
    options.Http2.MaxConcurrentStreams = 100;
    
    // Limit concurrent HTTP/3 streams per connection
    options.Http3.MaxConcurrentStreams = 100;
});
```

### Timeouts

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // Time to wait for the next request on keep-alive connections
    options.KeepAliveTimeout = TimeSpan.FromSeconds(120);
    
    // Time to wait for request headers (includes TLS handshake)
    options.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    
    // Time to wait for request body to arrive
    options.BodyConsumptionTimeout = TimeSpan.FromSeconds(30);
    
    // Time to gracefully drain connections during shutdown
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
});
```

### Buffer and Chunk Sizes

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // Buffer size before reading request body into memory
    // Larger uploads are streamed
    options.BodyBufferThreshold = 64 * 1024;  // 64 KB
    
    // Chunk size when writing response body
    options.ResponseBodyChunkSize = 16 * 1024;  // 16 KB
});
```

### HTTP Protocol Options

```csharp
builder.Services.AddTurboKestrel(options =>
{
    // HTTP/1.x settings
    options.Http1.MaxPipelinedRequests = 16;
    options.Http1.MaxRequestLineLength = 8192;
    
    // HTTP/2 settings
    options.Http2.MaxFrameSize = 16 * 1024;
    options.Http2.MaxHeaderListSize = 8192;
    
    // HTTP/3 settings
    options.Http3.MaxHeaderListSize = 8192;
    options.Http3.EnableWebTransport = false;
});
```

## Graceful Shutdown with Dependencies

If your handlers depend on external services (databases, caches, message queues), register your own shutdown hook to clean them up before the application exits:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register your dependencies
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddSingleton<RedisCache>();

builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
    options.GracefulShutdownTimeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// Register a hosted service for custom shutdown logic
builder.Services.AddHostedService<GracefulShutdownHandler>();

app.MapTurboPost("/orders", async (CreateOrderRequest req, IOrderRepository repo) =>
{
    var order = await repo.CreateAsync(req.CustomerId, req.Items);
    return new { id = order.Id };
});

await app.RunAsync();

// Custom shutdown handler
public sealed class GracefulShutdownHandler : IHostedService
{
    private readonly ILogger<GracefulShutdownHandler> _logger;
    private readonly RedisCache _cache;

    public GracefulShutdownHandler(ILogger<GracefulShutdownHandler> logger, RedisCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down custom services");
        await _cache.FlushAsync();  // Flush pending writes
    }
}
```

Your handler's `StopAsync` is called during Coordinated Shutdown, before the ActorSystem shuts down. This gives you an opportunity to flush caches, close connections, or notify external systems.

::: tip Combining with health checks
For zero-downtime deployments, pair graceful shutdown with health check middleware:

```csharp
var shuttingDown = false;

app.UseTurbo(async (context, next) =>
{
    if (shuttingDown && context.Request.Path != "/health")
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("Service shutting down");
        return;
    }
    await next(context);
});

// Health endpoint stays up during graceful shutdown
app.MapTurboGet("/health", () => new { status = "ok" });
```

This way, load balancers detect the server is draining and route new requests elsewhere, while existing connections finish their work.
:::

## Transport Layer

TurboHTTP uses Servus.Akka.Transport for network I/O:

- **TCP**: `TcpListenerFactory` handles HTTP/1.0, HTTP/1.1, and HTTP/2 connections
- **QUIC**: `QuicListenerFactory` handles HTTP/3 connections

Protocol engines (`Http10ServerEngine`, `Http11ServerEngine`, `Http20ServerEngine`, `Http30ServerEngine`) are selected via ALPN negotiation when TLS is enabled, or default to HTTP/1.1 for plaintext connections.
